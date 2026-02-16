using CORCleanup.Core.Models;

namespace CORCleanup.Core;

/// <summary>
/// Exports a CorSpecReport to plain text (clipboard) or branded HTML (file export).
/// </summary>
public static class CorSpecExporter
{
    public static string ToPlainText(CorSpecReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"COR Spec -- {report.ComputerName}");
        sb.AppendLine($"Generated: {report.GeneratedAtFormatted}");
        sb.AppendLine();

        // Operating System
        if (report.System is { } sys)
        {
            sb.AppendLine("OPERATING SYSTEM");
            AppendRow(sb, "Edition", sys.OsEdition);
            AppendRow(sb, "Version", sys.OsVersion);
            AppendRow(sb, "Build", sys.OsBuild);
            AppendRow(sb, "Install Date", sys.InstallDate.ToString("dd/MM/yyyy"));
            AppendRow(sb, "Computer Name", sys.ComputerName);
            sb.AppendLine();
        }

        // Processor
        if (report.System is { } cpu)
        {
            sb.AppendLine("PROCESSOR");
            AppendRow(sb, "CPU", cpu.CpuName);
            AppendRow(sb, "Cores / Threads", $"{cpu.CpuCores} / {cpu.CpuThreads}");
            AppendRow(sb, "Max Clock", $"{cpu.CpuMaxClockMhz} MHz");
            sb.AppendLine();
        }

        // Memory
        if (report.Ram is { } ram)
        {
            sb.AppendLine("MEMORY");
            AppendRow(sb, "Installed", ram.InstalledFormatted);
            AppendRow(sb, "Slots", $"{ram.UsedSlots} of {ram.TotalSlots} used");
            AppendRow(sb, "Max Capacity", ram.MaxCapacityFormatted);
            AppendRow(sb, "Channel Config", ram.ChannelConfig);
            foreach (var dimm in ram.Dimms)
            {
                AppendRow(sb, $"  {dimm.SlotLabel}", $"{dimm.CapacityFormatted} {dimm.MemoryType}-{dimm.SpeedMhz} ({dimm.Manufacturer})");
            }
            sb.AppendLine();
        }

        // Motherboard
        if (report.System is { } mb)
        {
            sb.AppendLine("MOTHERBOARD");
            AppendRow(sb, "Manufacturer", mb.MotherboardManufacturer);
            AppendRow(sb, "Product", mb.MotherboardProduct);
            AppendRow(sb, "BIOS", $"{mb.BiosVersion} ({mb.BiosDate})");
            sb.AppendLine();
        }

        // Graphics
        if (report.System is { } gpu)
        {
            sb.AppendLine("GRAPHICS");
            AppendRow(sb, "GPU", gpu.GpuName);
            AppendRow(sb, "VRAM", gpu.GpuVramFormatted);
            AppendRow(sb, "Driver", gpu.GpuDriverVersion);
            sb.AppendLine();
        }

        // Storage
        if (report.HasDisks || report.HasVolumes)
        {
            sb.AppendLine("STORAGE");

            foreach (var disk in report.PhysicalDisks)
            {
                AppendRow(sb, "Drive", $"{disk.Model} ({disk.TypeSummary}, {disk.SizeFormatted})");
                AppendRow(sb, "  Health", disk.HealthDisplay);
                if (disk.TemperatureCelsius.HasValue)
                    AppendRow(sb, "  Temperature", disk.TemperatureDisplay);
                if (disk.PowerOnHours.HasValue)
                    AppendRow(sb, "  Power-On Hours", disk.PowerOnDisplay);
                if (disk.WearLevellingPercent.HasValue)
                    AppendRow(sb, "  Wear", disk.WearDisplay);
            }

            foreach (var vol in report.LogicalVolumes)
            {
                AppendRow(sb, "Volume", $"{vol.DisplayLabel} — {vol.FileSystem} — {vol.UsedFormatted} / {vol.SizeFormatted} ({vol.UsedPercentFormatted} used)");
            }

            sb.AppendLine();
        }

        // Audio
        sb.AppendLine("AUDIO");
        if (report.HasAudio)
        {
            foreach (var audio in report.AudioDevices)
                AppendRow(sb, "Device", $"{audio.Name} ({audio.Manufacturer}) — {audio.Status}");
        }
        else
        {
            sb.AppendLine("  No audio devices detected");
        }
        sb.AppendLine();

        // Network
        if (report.HasNetwork)
        {
            sb.AppendLine("NETWORK");
            foreach (var adapter in report.NetworkAdapters)
            {
                AppendRow(sb, "Adapter", $"{adapter.Name} — {adapter.Status}");
                if (adapter.IpAddress is not null)
                    AppendRow(sb, "  IP", adapter.IpAddress);
                if (adapter.MacAddress is not null)
                    AppendRow(sb, "  MAC", adapter.MacAddress);
                if (adapter.SpeedMbps > 0)
                    AppendRow(sb, "  Speed", adapter.SpeedDisplay);
            }
            sb.AppendLine();
        }

        // Battery
        if (report.HasBattery && report.Battery is { } bat)
        {
            sb.AppendLine("BATTERY");
            AppendRow(sb, "Health", $"{bat.HealthPercent:F1}%");
            AppendRow(sb, "Design Capacity", bat.DesignCapacityFormatted);
            AppendRow(sb, "Full Charge", bat.FullChargeFormatted);
            AppendRow(sb, "Cycle Count", bat.CycleCount.ToString());
            AppendRow(sb, "Chemistry", bat.Chemistry);
            if (bat.NeedsReplacement)
                sb.AppendLine("  *** Battery needs replacement ***");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"COR Intelligence -- Generated {report.GeneratedAtFormatted}");
        return sb.ToString();
    }

    public static string ToHtml(CorSpecReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine($"<title>COR Spec — {report.ComputerName}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(Css);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"container\">");

        // Header
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine($"<h1>COR Spec</h1>");
        sb.AppendLine($"<p class=\"subtitle\">{report.ComputerName} &mdash; {report.GeneratedAtFormatted}</p>");
        sb.AppendLine("</div>");

        // Operating System
        if (report.System is { } sys)
        {
            sb.AppendLine(SectionStart("Operating System"));
            HtmlRow(sb, "Edition", sys.OsEdition);
            HtmlRow(sb, "Version", sys.OsVersion);
            HtmlRow(sb, "Build", sys.OsBuild);
            HtmlRow(sb, "Install Date", sys.InstallDate.ToString("dd/MM/yyyy"));
            HtmlRow(sb, "Computer Name", sys.ComputerName);
            sb.AppendLine(SectionEnd());
        }

        // Processor
        if (report.System is { } cpu)
        {
            sb.AppendLine(SectionStart("Processor"));
            HtmlRow(sb, "CPU", cpu.CpuName);
            HtmlRow(sb, "Cores / Threads", $"{cpu.CpuCores} / {cpu.CpuThreads}");
            HtmlRow(sb, "Max Clock", $"{cpu.CpuMaxClockMhz} MHz");
            sb.AppendLine(SectionEnd());
        }

        // Memory
        if (report.Ram is { } ram)
        {
            sb.AppendLine(SectionStart("Memory"));
            HtmlRow(sb, "Installed", ram.InstalledFormatted);
            HtmlRow(sb, "Slots", $"{ram.UsedSlots} of {ram.TotalSlots} used");
            HtmlRow(sb, "Max Capacity", ram.MaxCapacityFormatted);
            HtmlRow(sb, "Channel Config", ram.ChannelConfig);
            foreach (var dimm in ram.Dimms)
                HtmlRow(sb, dimm.SlotLabel, $"{dimm.CapacityFormatted} {dimm.MemoryType}-{dimm.SpeedMhz} ({dimm.Manufacturer})");
            sb.AppendLine(SectionEnd());
        }

        // Motherboard
        if (report.System is { } mb)
        {
            sb.AppendLine(SectionStart("Motherboard"));
            HtmlRow(sb, "Manufacturer", mb.MotherboardManufacturer);
            HtmlRow(sb, "Product", mb.MotherboardProduct);
            HtmlRow(sb, "BIOS", $"{mb.BiosVersion} ({mb.BiosDate})");
            sb.AppendLine(SectionEnd());
        }

        // Graphics
        if (report.System is { } gpu)
        {
            sb.AppendLine(SectionStart("Graphics"));
            HtmlRow(sb, "GPU", gpu.GpuName);
            HtmlRow(sb, "VRAM", gpu.GpuVramFormatted);
            HtmlRow(sb, "Driver", gpu.GpuDriverVersion);
            sb.AppendLine(SectionEnd());
        }

        // Storage
        if (report.HasDisks || report.HasVolumes)
        {
            sb.AppendLine(SectionStart("Storage"));
            foreach (var disk in report.PhysicalDisks)
            {
                var healthClass = disk.OverallHealth switch
                {
                    DiskHealthStatus.Good => "badge-good",
                    DiskHealthStatus.Caution => "badge-caution",
                    DiskHealthStatus.Bad => "badge-bad",
                    _ => "badge-unknown"
                };
                HtmlRow(sb, disk.Model, $"{disk.TypeSummary} &mdash; {disk.SizeFormatted} &mdash; <span class=\"{healthClass}\">{disk.HealthDisplay}</span>");
                if (disk.TemperatureCelsius.HasValue)
                    HtmlRow(sb, "&nbsp;&nbsp;Temperature", disk.TemperatureDisplay);
                if (disk.PowerOnHours.HasValue)
                    HtmlRow(sb, "&nbsp;&nbsp;Power-On Hours", disk.PowerOnDisplay);
                if (disk.WearLevellingPercent.HasValue)
                    HtmlRow(sb, "&nbsp;&nbsp;Wear", disk.WearDisplay);
            }
            foreach (var vol in report.LogicalVolumes)
            {
                var pct = (int)vol.UsedPercent;
                var barClass = pct > 90 ? "bar-danger" : pct > 75 ? "bar-warn" : "bar-ok";
                HtmlRow(sb, vol.DisplayLabel,
                    $"{vol.FileSystem} &mdash; {vol.UsedFormatted} / {vol.SizeFormatted}" +
                    $"<div class=\"usage-bar\"><div class=\"usage-fill {barClass}\" style=\"width:{pct}%\"></div></div>");
            }
            sb.AppendLine(SectionEnd());
        }

        // Audio
        sb.AppendLine(SectionStart("Audio"));
        if (report.HasAudio)
        {
            foreach (var audio in report.AudioDevices)
                HtmlRow(sb, audio.Name, $"{audio.Manufacturer} &mdash; {audio.Status}");
        }
        else
        {
            sb.AppendLine("<tr><td colspan=\"2\" class=\"empty\">No audio devices detected</td></tr>");
        }
        sb.AppendLine(SectionEnd());

        // Network
        if (report.HasNetwork)
        {
            sb.AppendLine(SectionStart("Network"));
            foreach (var adapter in report.NetworkAdapters)
            {
                HtmlRow(sb, adapter.Name, $"{adapter.Status}{(adapter.IpAddress is not null ? $" &mdash; {adapter.IpAddress}" : "")}");
                if (adapter.MacAddress is not null)
                    HtmlRow(sb, "&nbsp;&nbsp;MAC", adapter.MacAddress);
                if (adapter.SpeedMbps > 0)
                    HtmlRow(sb, "&nbsp;&nbsp;Speed", adapter.SpeedDisplay);
            }
            sb.AppendLine(SectionEnd());
        }

        // Battery
        if (report.HasBattery && report.Battery is { } bat)
        {
            sb.AppendLine(SectionStart("Battery"));
            HtmlRow(sb, "Health", $"{bat.HealthPercent:F1}%{(bat.NeedsReplacement ? " <span class=\"badge-bad\">Needs Replacement</span>" : "")}");
            HtmlRow(sb, "Design Capacity", bat.DesignCapacityFormatted);
            HtmlRow(sb, "Full Charge", bat.FullChargeFormatted);
            HtmlRow(sb, "Cycle Count", bat.CycleCount.ToString());
            HtmlRow(sb, "Chemistry", bat.Chemistry);
            sb.AppendLine(SectionEnd());
        }

        // Footer
        sb.AppendLine("<div class=\"footer\">");
        sb.AppendLine($"<p>COR Intelligence &mdash; Generated {report.GeneratedAtFormatted}</p>");
        sb.AppendLine("<p class=\"legal\">COR Solutions Services Ltd | corsolutions.co.uk</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendRow(System.Text.StringBuilder sb, string label, string value)
    {
        sb.AppendLine($"  {label,-20} {value}");
    }

    private static string SectionStart(string title) =>
        $"<div class=\"section\"><h2>{title}</h2><table>";

    private static string SectionEnd() => "</table></div>";

    private static void HtmlRow(System.Text.StringBuilder sb, string label, string value)
    {
        sb.AppendLine($"<tr><td class=\"label\">{label}</td><td>{value}</td></tr>");
    }

    private const string Css = """
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: 'Segoe UI', -apple-system, sans-serif; background: #0f0f1a; color: #e0e0e0; }
        .container { max-width: 900px; margin: 0 auto; padding: 32px 24px; }
        .header { text-align: center; margin-bottom: 32px; border-bottom: 2px solid #06B6D4; padding-bottom: 16px; }
        .header h1 { font-size: 28px; color: #06B6D4; letter-spacing: 2px; }
        .header .subtitle { color: #9ca3af; margin-top: 8px; font-size: 14px; }
        .section { margin-bottom: 24px; background: #1a1a2e; border-radius: 8px; padding: 16px 20px; border: 1px solid #2a2a40; }
        .section h2 { font-size: 15px; font-weight: 600; color: #06B6D4; margin-bottom: 12px; text-transform: uppercase; letter-spacing: 1px; }
        table { width: 100%; border-collapse: collapse; }
        td { padding: 4px 0; vertical-align: top; font-size: 13px; }
        td.label { width: 170px; color: #9ca3af; font-weight: 500; }
        td.empty { color: #6b7280; font-style: italic; padding: 8px 0; }
        .badge-good { background: #10B981; color: #fff; padding: 2px 8px; border-radius: 4px; font-size: 12px; }
        .badge-caution { background: #F59E0B; color: #000; padding: 2px 8px; border-radius: 4px; font-size: 12px; }
        .badge-bad { background: #EF4444; color: #fff; padding: 2px 8px; border-radius: 4px; font-size: 12px; }
        .badge-unknown { background: #6B7280; color: #fff; padding: 2px 8px; border-radius: 4px; font-size: 12px; }
        .usage-bar { background: #2a2a40; border-radius: 4px; height: 8px; margin-top: 4px; overflow: hidden; }
        .usage-fill { height: 100%; border-radius: 4px; transition: width 0.3s; }
        .bar-ok { background: #06B6D4; }
        .bar-warn { background: #F59E0B; }
        .bar-danger { background: #EF4444; }
        .footer { text-align: center; margin-top: 32px; padding-top: 16px; border-top: 1px solid #2a2a40; color: #6b7280; font-size: 12px; }
        .footer .legal { margin-top: 4px; font-size: 11px; }
        """;
}
