using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;

namespace CORCleanup.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _pageTitle = "Settings";
    [ObservableProperty] private bool _isDarkTheme = false;

    // ================================================================
    // About
    // ================================================================

    public string AppName => "COR Cleanup";
    public string AppVersion => $"Version {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"}";
    public string AppCopyright => $"\u00a9 {DateTime.Now.Year} COR Solutions Services Ltd";
    public string CompanyName => "COR Solutions Services Ltd";
    public string TradingAs => "Trading as COR Intelligence";
    public string CompanyNumber => "Company No. 15027891";
    public string VatNumber => "VAT GB473618471";
    public string CompanyAddress => "The Studio, 6 Park Lane, Henlow, SG16 6AT";
    public string CompanyPhone => "01462 434082";
    public string CompanyEmail => "enquiries@corsolutions.co.uk";
    public string CompanyWebsite => "https://corintelligence.co.uk";

    // ================================================================
    // Terms & Conditions
    // ================================================================

    public string TermsAndConditions => """
        TERMS AND CONDITIONS OF USE
        COR Cleanup - System Administration Tool
        Last Updated: February 2026

        1. ACCEPTANCE OF TERMS
        By installing, copying, or using COR Cleanup ("the Software"), you agree to be bound by these Terms and Conditions. If you do not agree, do not install or use the Software.

        2. LICENCE GRANT
        COR Solutions Services Ltd ("the Company") grants you a limited, non-exclusive, non-transferable licence to use the Software for legitimate system administration and maintenance purposes.

        3. INTENDED USE
        The Software is designed for use by IT professionals and system administrators. It provides tools for system diagnostics, cleanup, network analysis, registry maintenance, and other administrative functions. The Software requires administrator privileges to perform certain operations.

        4. SYSTEM MODIFICATIONS
        The Software may modify system settings, registry entries, startup configurations, services, network settings, and file system content (such as the hosts file). You acknowledge that:
        (a) System modifications carry inherent risk and may affect system stability.
        (b) You are responsible for creating appropriate backups before performing any modifications.
        (c) The Company is not liable for any system instability, data loss, or other adverse effects resulting from the use of the Software.

        5. REGISTRY AND CLEANUP OPERATIONS
        Registry cleaning, temporary file removal, and system cleanup operations may remove files or entries that are no longer referenced but may still be required by certain applications. You should:
        (a) Review items before deletion where the Software provides a preview.
        (b) Create a system restore point before performing bulk operations.
        (c) Test system functionality after performing cleanup operations.

        6. NETWORK TOOLS
        The Software includes network diagnostic tools such as ping, traceroute, port scanning, DNS lookup, and Wi-Fi profile management. You agree to:
        (a) Use network tools only on networks and systems you are authorised to test.
        (b) Comply with all applicable laws regarding network scanning and analysis.
        (c) Not use the Software for unauthorised access, reconnaissance, or any malicious purpose.

        7. UNINSTALLATION FEATURES
        The Software can remove installed applications and their residual files. Forced removal operations bypass standard uninstallation procedures and may leave incomplete configurations. Use forced removal only when standard methods have failed.

        8. NO WARRANTY
        THE SOFTWARE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NON-INFRINGEMENT. THE COMPANY DOES NOT WARRANT THAT THE SOFTWARE WILL BE ERROR-FREE OR UNINTERRUPTED.

        9. LIMITATION OF LIABILITY
        TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW, IN NO EVENT SHALL THE COMPANY BE LIABLE FOR ANY INDIRECT, INCIDENTAL, SPECIAL, CONSEQUENTIAL, OR PUNITIVE DAMAGES, INCLUDING BUT NOT LIMITED TO LOSS OF DATA, LOSS OF PROFITS, OR BUSINESS INTERRUPTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OF THE SOFTWARE.

        10. INTELLECTUAL PROPERTY
        The Software and all associated intellectual property rights are owned by COR Solutions Services Ltd. You may not reverse engineer, decompile, disassemble, or create derivative works based on the Software.

        11. UPDATES
        The Company may release updates to the Software from time to time. Updates may modify functionality, add features, or address security issues. Continued use of the Software after an update constitutes acceptance of any modified terms.

        12. TERMINATION
        This licence is effective until terminated. Your rights under this licence will terminate automatically if you fail to comply with any of its terms.

        13. GOVERNING LAW
        These Terms shall be governed by and construed in accordance with the laws of England and Wales. Any disputes shall be subject to the exclusive jurisdiction of the courts of England and Wales.

        14. CONTACT
        COR Solutions Services Ltd
        Company No. 15027891 | VAT GB473618471
        The Studio, 6 Park Lane, Henlow, SG16 6AT
        Email: enquiries@corsolutions.co.uk
        Phone: 01462 434082
        """;

    // ================================================================
    // Privacy Policy
    // ================================================================

    public string PrivacyPolicy => """
        PRIVACY POLICY
        COR Cleanup - System Administration Tool
        Last Updated: February 2026

        1. INTRODUCTION
        COR Solutions Services Ltd ("we", "us", "our") is committed to protecting your privacy. This Privacy Policy explains how COR Cleanup ("the Software") handles information when you use it.

        2. DATA CONTROLLER
        COR Solutions Services Ltd (Company No. 15027891) is the data controller for the purposes of the UK General Data Protection Regulation (UK GDPR) and the Data Protection Act 2018.

        3. LOCAL-ONLY OPERATION
        COR Cleanup operates entirely on your local machine. The Software does NOT:
        (a) Transmit any data to external servers.
        (b) Collect personal information for remote storage.
        (c) Use analytics, telemetry, or tracking services.
        (d) Require an internet connection for core functionality.
        (e) Create user accounts or require registration.

        4. INFORMATION ACCESSED LOCALLY
        To provide its system administration features, the Software accesses the following information locally on your device:
        (a) System Information: Hardware specifications, operating system details, installed drivers, and system health metrics for diagnostic purposes.
        (b) Installed Software: Application names, versions, publishers, and installation paths for the uninstaller and software inventory features.
        (c) Network Configuration: IP addresses, network adapter details, Wi-Fi profiles (including saved passwords), DNS settings, and routing information for network diagnostic tools.
        (d) Registry Data: Windows Registry entries for the registry cleaner and startup manager features.
        (e) File System Data: Temporary files, cache directories, log files, and the Windows hosts file for cleanup and administration features.
        (f) Services and Processes: Windows services status, startup items, and event log entries for system administration.
        (g) Printer Information: Installed printers, drivers, and print queue status for printer management.

        5. DATA STORAGE
        All information processed by the Software remains on your local machine. The Software may create log files in your user profile directory (%APPDATA%\COR Cleanup\Logs\) for error tracking and diagnostics. These logs contain only technical information about the Software's operation, not personal data.

        6. WI-FI PASSWORDS
        The Wi-Fi password viewer feature displays saved wireless network passwords stored by Windows. These passwords are already present on your system and are accessed using standard Windows APIs. The Software does not transmit, store, or copy these passwords beyond displaying them in the application interface.

        7. THIRD-PARTY SERVICES
        The Software does not integrate with any third-party services, advertising networks, or analytics platforms. Network diagnostic tools (ping, traceroute, DNS lookup, port scanner) communicate only with the target addresses you specify.

        8. DATA RETENTION
        The Software does not retain any personal data. Application log files are stored locally and can be deleted at any time by removing the %APPDATA%\COR Cleanup\ directory.

        9. YOUR RIGHTS UNDER UK GDPR
        As the Software processes data locally and does not collect personal data remotely, most data subject rights under UK GDPR are exercised through your own device management. You retain full control over all data on your machine.

        10. CHILDREN'S PRIVACY
        The Software is designed for IT professionals and system administrators. It is not intended for use by children under the age of 18.

        11. CHANGES TO THIS POLICY
        We may update this Privacy Policy from time to time. Changes will be reflected in updated versions of the Software. The "Last Updated" date at the top of this policy indicates the most recent revision.

        12. CONTACT US
        If you have questions about this Privacy Policy or our data practices, contact us at:

        COR Solutions Services Ltd
        Trading as COR Intelligence
        The Studio, 6 Park Lane, Henlow, SG16 6AT
        Email: enquiries@corsolutions.co.uk
        Phone: 01462 434082
        ICO Registration: Available upon request
        """;

    // ================================================================
    // Commands
    // ================================================================

    partial void OnIsDarkThemeChanged(bool value)
    {
        ApplicationThemeManager.Apply(
            value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = CompanyWebsite,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenEmail()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = $"mailto:{CompanyEmail}",
            UseShellExecute = true
        });
    }
}
