using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Tools;

[SupportedOSPlatform("windows")]
public sealed class BsodViewerService : IBsodViewerService
{
    private const string MinidumpPath = @"C:\Windows\Minidump";
    private const uint MinidumpSignature = 0x504D444D; // 'MDMP'

    public Task<List<BsodCrashEntry>> GetCrashEntriesAsync()
    {
        var entries = new List<BsodCrashEntry>();

        if (!Directory.Exists(MinidumpPath))
            return Task.FromResult(entries);

        foreach (var file in Directory.EnumerateFiles(MinidumpPath, "*.dmp"))
        {
            try
            {
                var entry = ParseMinidump(file);
                if (entry is not null)
                    entries.Add(entry);
            }
            catch
            {
                // Skip corrupt or unreadable dump files
            }
        }

        // Also check for full memory dump
        const string fullDump = @"C:\Windows\MEMORY.DMP";
        if (File.Exists(fullDump))
        {
            try
            {
                var entry = ParseMinidump(fullDump);
                if (entry is not null)
                    entries.Add(entry);
            }
            catch { }
        }

        return Task.FromResult(
            entries.OrderByDescending(e => e.CrashTime).ToList());
    }

    private static BsodCrashEntry? ParseMinidump(string filePath)
    {
        var fi = new FileInfo(filePath);
        if (fi.Length < 32)
            return null;

        using var fs = File.OpenRead(filePath);
        using var reader = new BinaryReader(fs);

        // MINIDUMP_HEADER: Signature (4), Version (4), NumberOfStreams (4),
        // StreamDirectoryRva (4), CheckSum (4), TimeDateStamp (4)
        uint signature = reader.ReadUInt32();
        if (signature != MinidumpSignature)
            return null;

        // Version — low 16 bits = implementation version, high 16 bits = internal
        reader.ReadUInt32(); // skip version

        // NumberOfStreams
        reader.ReadUInt32();

        // StreamDirectoryRva
        reader.ReadUInt32();

        // CheckSum
        reader.ReadUInt32();

        // TimeDateStamp — Unix epoch seconds
        uint timestamp = reader.ReadUInt32();
        var crashTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;

        // Try to find bug check code from stream directory
        // For minidumps, the bug check code is typically in the exception stream
        // We'll extract it by reading additional data
        uint bugCheckCode = 0;
        string? faultingModule = null;

        try
        {
            // Seek back and read the stream directory to find exception info
            fs.Position = 4 + 4; // After signature and version
            uint streamCount = reader.ReadUInt32();
            uint streamDirRva = reader.ReadUInt32();

            fs.Position = streamDirRva;

            for (uint i = 0; i < Math.Min(streamCount, 64); i++)
            {
                uint streamType = reader.ReadUInt32();
                uint dataSize = reader.ReadUInt32();
                uint dataRva = reader.ReadUInt32();

                if (streamType == 6) // ExceptionStream
                {
                    long savedPos = fs.Position;
                    fs.Position = dataRva;

                    // MINIDUMP_EXCEPTION_STREAM:
                    // ThreadId (4), __alignment (4)
                    reader.ReadUInt32(); // ThreadId
                    reader.ReadUInt32(); // alignment

                    // MINIDUMP_EXCEPTION:
                    // ExceptionCode (4), ExceptionFlags (4)
                    bugCheckCode = reader.ReadUInt32();

                    fs.Position = savedPos;
                }
                else if (streamType == 7) // SystemInfoStream
                {
                    // System info — we already have crash time
                }
                else if (streamType == 0) // ModuleListStream
                {
                    long savedPos = fs.Position;
                    fs.Position = dataRva;

                    try
                    {
                        uint moduleCount = reader.ReadUInt32();
                        if (moduleCount > 0 && moduleCount < 1000)
                        {
                            // First module is typically the faulting one in minidumps
                            // MINIDUMP_MODULE: BaseOfImage(8), SizeOfImage(4), Checksum(4),
                            // TimeDateStamp(4), ModuleNameRva(4), ...
                            reader.ReadUInt64(); // BaseOfImage
                            reader.ReadUInt32(); // SizeOfImage
                            reader.ReadUInt32(); // Checksum
                            reader.ReadUInt32(); // TimeDateStamp
                            uint nameRva = reader.ReadUInt32();

                            // Read module name
                            if (nameRva > 0 && nameRva < fi.Length - 4)
                            {
                                fs.Position = nameRva;
                                uint nameLen = reader.ReadUInt32(); // Length in bytes (UTF-16)
                                if (nameLen > 0 && nameLen < 512)
                                {
                                    var nameBytes = reader.ReadBytes((int)nameLen);
                                    var fullPath = System.Text.Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
                                    faultingModule = Path.GetFileName(fullPath);
                                }
                            }
                        }
                    }
                    catch { }

                    fs.Position = savedPos;
                }
            }
        }
        catch
        {
            // If we can't parse streams, we still have timestamp
        }

        return new BsodCrashEntry
        {
            CrashTime = crashTime,
            BugCheckCode = $"0x{bugCheckCode:X8}",
            BugCheckName = GetBugCheckName(bugCheckCode),
            FaultingModule = faultingModule,
            DumpFilePath = filePath,
            DumpFileSizeBytes = fi.Length
        };
    }

    /// <summary>
    /// Maps common Windows bug check codes to human-readable names.
    /// </summary>
    private static string GetBugCheckName(uint code) => code switch
    {
        0x0000000A => "IRQL_NOT_LESS_OR_EQUAL",
        0x0000001E => "KMODE_EXCEPTION_NOT_HANDLED",
        0x00000024 => "NTFS_FILE_SYSTEM",
        0x0000002E => "DATA_BUS_ERROR",
        0x0000003B => "SYSTEM_SERVICE_EXCEPTION",
        0x0000003F => "NO_MORE_SYSTEM_PTES",
        0x00000050 => "PAGE_FAULT_IN_NONPAGED_AREA",
        0x0000007E => "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
        0x0000007F => "UNEXPECTED_KERNEL_MODE_TRAP",
        0x0000009F => "DRIVER_POWER_STATE_FAILURE",
        0x000000BE => "ATTEMPTED_WRITE_TO_READONLY_MEMORY",
        0x000000C2 => "BAD_POOL_CALLER",
        0x000000C5 => "DRIVER_CORRUPTED_EXPOOL",
        0x000000D1 => "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
        0x000000D5 => "DRIVER_PAGE_FAULT_IN_FREED_SPECIAL_POOL",
        0x000000EF => "CRITICAL_PROCESS_DIED",
        0x000000F4 => "CRITICAL_OBJECT_TERMINATION",
        0x00000116 => "VIDEO_TDR_TIMEOUT_DETECTED",
        0x00000117 => "VIDEO_TDR_ERROR",
        0x00000119 => "VIDEO_SCHEDULER_INTERNAL_ERROR",
        0x0000011B => "DRIVER_RETURNED_HOLDING_CANCEL_LOCK",
        0x0000012B => "FAULTY_HARDWARE_CORRUPTED_PAGE",
        0x00000133 => "DPC_WATCHDOG_VIOLATION",
        0x00000139 => "KERNEL_SECURITY_CHECK_FAILURE",
        0x0000013A => "KERNEL_MODE_HEAP_CORRUPTION",
        0x00000154 => "UNEXPECTED_STORE_EXCEPTION",
        0x00000187 => "VIDEO_DXGKRNL_FATAL_ERROR",
        0x000001CA => "SYNTHETIC_WATCHDOG_TIMEOUT",
        0xC0000005 => "ACCESS_VIOLATION",
        0xC000021A => "STATUS_SYSTEM_PROCESS_TERMINATED",
        _ => BugCheckCodeToHex(code)
    };

    private static string BugCheckCodeToHex(uint code)
    {
        return code == 0 ? "Unknown" : $"BUGCHECK_0x{code:X8}";
    }
}
