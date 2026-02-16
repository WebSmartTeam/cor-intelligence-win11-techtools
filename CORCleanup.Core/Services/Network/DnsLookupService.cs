using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Core.Security;

namespace CORCleanup.Core.Services.Network;

/// <summary>
/// DNS lookup using nslookup.exe (available on all Windows versions).
/// For A/AAAA we also use System.Net.Dns as primary with nslookup fallback.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class DnsLookupService : IDnsLookupService
{
    // Well-known DNS servers for propagation checks
    private static readonly (string Name, string Ip)[] PropagationServers =
    [
        ("Google", "8.8.8.8"),
        ("Cloudflare", "1.1.1.1"),
        ("Quad9", "9.9.9.9"),
        ("OpenDNS", "208.67.222.222"),
        ("Comodo", "8.26.56.26"),
    ];

    public async Task<DnsLookupResult> LookupAsync(
        string domain,
        string recordType = "A",
        string? dnsServer = null,
        CancellationToken ct = default)
    {
        // Validate inputs to prevent command injection via nslookup arguments
        if (!InputSanitiser.IsValidHostnameOrIp(domain))
            throw new ArgumentException("Invalid domain name or IP address.", nameof(domain));

        if (!InputSanitiser.IsValidDnsRecordType(recordType))
            throw new ArgumentException($"Unsupported record type: {recordType}", nameof(recordType));

        if (dnsServer is not null && !InputSanitiser.IsValidIpAddress(dnsServer))
            throw new ArgumentException("DNS server must be a valid IP address.", nameof(dnsServer));

        var sw = Stopwatch.StartNew();

        // For simple A/AAAA without custom server, use .NET DNS API (faster)
        if ((recordType is "A" or "AAAA") && dnsServer is null)
        {
            return await DotNetDnsLookupAsync(domain, recordType, sw, ct);
        }

        // For all other record types or custom servers, use nslookup
        return await NslookupAsync(domain, recordType, dnsServer, sw, ct);
    }

    public async Task<List<DnsLookupResult>> PropagationCheckAsync(
        string domain,
        string recordType = "A",
        CancellationToken ct = default)
    {
        var tasks = PropagationServers.Select(server =>
            LookupAsync(domain, recordType, server.Ip, ct));

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private static async Task<DnsLookupResult> DotNetDnsLookupAsync(
        string domain, string recordType, Stopwatch sw, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(domain, ct);
            sw.Stop();

            var targetFamily = recordType == "AAAA"
                ? AddressFamily.InterNetworkV6
                : AddressFamily.InterNetwork;

            var records = addresses
                .Where(a => a.AddressFamily == targetFamily)
                .Select(a => new DnsRecord
                {
                    Type = recordType,
                    Name = domain,
                    Value = a.ToString()
                })
                .ToList();

            return new DnsLookupResult
            {
                Domain = domain,
                DnsServer = "System Default",
                RecordType = recordType,
                Records = records,
                QueryTimeMs = sw.ElapsedMilliseconds
            };
        }
        catch (SocketException ex)
        {
            sw.Stop();
            return new DnsLookupResult
            {
                Domain = domain,
                DnsServer = "System Default",
                RecordType = recordType,
                Records = new List<DnsRecord>(),
                QueryTimeMs = sw.ElapsedMilliseconds,
                Error = ex.Message
            };
        }
    }

    private static async Task<DnsLookupResult> NslookupAsync(
        string domain, string recordType, string? dnsServer, Stopwatch sw, CancellationToken ct)
    {
        var args = $"-type={recordType} {domain}";
        if (dnsServer is not null)
            args += $" {dnsServer}";

        // Declare outside try so catch can kill the process on timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "nslookup",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorOutput = await proc.StandardError.ReadToEndAsync(timeoutCts.Token);
            await proc.WaitForExitAsync(timeoutCts.Token);
            sw.Stop();

            // nslookup sends some output to stderr (server info), combine
            var fullOutput = output + "\n" + errorOutput;
            var records = ParseNslookupOutput(fullOutput, domain, recordType);

            return new DnsLookupResult
            {
                Domain = domain,
                DnsServer = dnsServer ?? "System Default",
                RecordType = recordType,
                Records = records,
                QueryTimeMs = sw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            try
            {
                proc.Kill(entireProcessTree: true);
                // Wait briefly for the process to actually terminate to prevent zombie processes
                proc.WaitForExit(TimeSpan.FromSeconds(5));
            }
            catch { }
            return new DnsLookupResult
            {
                Domain = domain,
                DnsServer = dnsServer ?? "System Default",
                RecordType = recordType,
                Records = new List<DnsRecord>(),
                QueryTimeMs = sw.ElapsedMilliseconds,
                Error = ct.IsCancellationRequested ? "Lookup cancelled" : "Lookup timed out"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DnsLookupResult
            {
                Domain = domain,
                DnsServer = dnsServer ?? "System Default",
                RecordType = recordType,
                Records = new List<DnsRecord>(),
                QueryTimeMs = sw.ElapsedMilliseconds,
                Error = ex.Message
            };
        }
        finally
        {
            proc.Dispose();
        }
    }

    private static List<DnsRecord> ParseNslookupOutput(string output, string domain, string recordType)
    {
        var records = new List<DnsRecord>();
        var lines = output.Split('\n', StringSplitOptions.TrimEntries);

        // Skip the server identification block (first "Answer" section starts after blank line)
        bool inAnswerSection = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                inAnswerSection = true;
                continue;
            }

            if (!inAnswerSection) continue;

            switch (recordType.ToUpperInvariant())
            {
                case "A" or "AAAA":
                    var addrMatch = AddressRegex().Match(line);
                    if (addrMatch.Success)
                    {
                        records.Add(new DnsRecord
                        {
                            Type = recordType,
                            Name = domain,
                            Value = addrMatch.Groups[1].Value.Trim()
                        });
                    }
                    break;

                case "MX":
                    var mxMatch = MxRegex().Match(line);
                    if (mxMatch.Success)
                    {
                        records.Add(new DnsRecord
                        {
                            Type = "MX",
                            Name = domain,
                            Value = mxMatch.Groups[2].Value.Trim(),
                            Priority = int.TryParse(mxMatch.Groups[1].Value, out var pri) ? pri : null
                        });
                    }
                    break;

                case "CNAME":
                    var cnameMatch = CnameRegex().Match(line);
                    if (cnameMatch.Success)
                    {
                        records.Add(new DnsRecord
                        {
                            Type = "CNAME",
                            Name = cnameMatch.Groups[1].Value.Trim(),
                            Value = cnameMatch.Groups[2].Value.Trim()
                        });
                    }
                    break;

                case "TXT":
                    var txtMatch = TxtRegex().Match(line);
                    if (txtMatch.Success)
                    {
                        records.Add(new DnsRecord
                        {
                            Type = "TXT",
                            Name = domain,
                            Value = txtMatch.Groups[1].Value.Trim().Trim('"')
                        });
                    }
                    break;

                case "NS":
                    var nsMatch = NsRegex().Match(line);
                    if (nsMatch.Success)
                    {
                        records.Add(new DnsRecord
                        {
                            Type = "NS",
                            Name = domain,
                            Value = nsMatch.Groups[1].Value.Trim()
                        });
                    }
                    break;

                case "SOA":
                    var soaMatch = SoaRegex().Match(line);
                    if (soaMatch.Success)
                    {
                        records.Add(new DnsRecord
                        {
                            Type = "SOA",
                            Name = domain,
                            Value = $"{soaMatch.Groups[1].Value.Trim()} {soaMatch.Groups[2].Value.Trim()}"
                        });
                    }
                    break;
            }
        }

        return records;
    }

    // "Address:  8.8.8.8" or "Addresses:  2607:f8b0:4004:800::200e"
    [GeneratedRegex(@"Address(?:es)?:\s+(\S+)", RegexOptions.Compiled)]
    private static partial Regex AddressRegex();

    // "MX preference = 10, mail exchanger = mx.example.com"
    [GeneratedRegex(@"MX preference\s*=\s*(\d+),\s*mail exchanger\s*=\s*(\S+)", RegexOptions.Compiled)]
    private static partial Regex MxRegex();

    // "canonical name = target.example.com"
    [GeneratedRegex(@"(\S+)\s+canonical name\s*=\s*(\S+)", RegexOptions.Compiled)]
    private static partial Regex CnameRegex();

    // "text = \"v=spf1 include:...\"" or just text lines
    [GeneratedRegex(@"text\s*=\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex TxtRegex();

    // "nameserver = ns1.example.com"
    [GeneratedRegex(@"nameserver\s*=\s*(\S+)", RegexOptions.Compiled)]
    private static partial Regex NsRegex();

    // "primary name server = ns1.example.com  responsible mail addr = ..."
    [GeneratedRegex(@"primary name server\s*=\s*(\S+).*?responsible mail addr\s*=\s*(\S+)", RegexOptions.Compiled)]
    private static partial Regex SoaRegex();
}
