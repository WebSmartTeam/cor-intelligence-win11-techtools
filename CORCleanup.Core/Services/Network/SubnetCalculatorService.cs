using System.Net;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Network;

public sealed class SubnetCalculatorService : ISubnetCalculatorService
{
    public SubnetCalculation Calculate(string ipCidr)
    {
        // Parse "192.168.1.0/24" or "192.168.1.100/255.255.255.0"
        var parts = ipCidr.Trim().Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip))
            throw new ArgumentException($"Invalid CIDR notation: {ipCidr}");

        int cidr;
        if (int.TryParse(parts[1], out cidr))
        {
            if (cidr < 0 || cidr > 32)
                throw new ArgumentException($"CIDR prefix must be 0-32, got: {cidr}");
        }
        else if (IPAddress.TryParse(parts[1], out var maskIp))
        {
            cidr = CountBits(IpToUint(maskIp));
        }
        else
        {
            throw new ArgumentException($"Invalid subnet mask or CIDR: {parts[1]}");
        }

        uint ipInt = IpToUint(ip);
        uint mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
        uint wildcard = ~mask;
        uint network = ipInt & mask;
        uint broadcast = network | wildcard;

        // For /31 and /32, special handling
        uint firstUsable, lastUsable;
        long totalHosts = (long)(broadcast - network) + 1;
        long usableHosts;

        if (cidr >= 31)
        {
            firstUsable = network;
            lastUsable = broadcast;
            usableHosts = totalHosts;
        }
        else
        {
            firstUsable = network + 1;
            lastUsable = broadcast - 1;
            usableHosts = totalHosts - 2;
        }

        return new SubnetCalculation
        {
            InputCidr = ipCidr,
            NetworkAddress = UintToIp(network),
            BroadcastAddress = UintToIp(broadcast),
            SubnetMask = UintToIp(mask),
            WildcardMask = UintToIp(wildcard),
            FirstUsable = UintToIp(firstUsable),
            LastUsable = UintToIp(lastUsable),
            TotalHosts = totalHosts,
            UsableHosts = usableHosts,
            CidrNotation = cidr
        };
    }

    public bool AreOnSameSubnet(string ip1, string ip2, string subnetMask)
    {
        if (!IPAddress.TryParse(ip1, out var addr1) ||
            !IPAddress.TryParse(ip2, out var addr2) ||
            !IPAddress.TryParse(subnetMask, out var mask))
            throw new ArgumentException("Invalid IP address or subnet mask");

        uint a = IpToUint(addr1);
        uint b = IpToUint(addr2);
        uint m = IpToUint(mask);

        return (a & m) == (b & m);
    }

    private static uint IpToUint(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    private static string UintToIp(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return new IPAddress(bytes).ToString();
    }

    private static int CountBits(uint mask)
    {
        int count = 0;
        while (mask != 0)
        {
            count += (int)(mask & 1);
            mask >>= 1;
        }
        return count;
    }

    /// <summary>
    /// Common subnet reference table for quick lookups.
    /// </summary>
    public static readonly (int Cidr, string Mask, int Hosts, string Class)[] CommonSubnets =
    [
        (8, "255.0.0.0", 16777214, "A"),
        (16, "255.255.0.0", 65534, "B"),
        (20, "255.255.240.0", 4094, ""),
        (21, "255.255.248.0", 2046, ""),
        (22, "255.255.252.0", 1022, ""),
        (23, "255.255.254.0", 510, ""),
        (24, "255.255.255.0", 254, "C"),
        (25, "255.255.255.128", 126, ""),
        (26, "255.255.255.192", 62, ""),
        (27, "255.255.255.224", 30, ""),
        (28, "255.255.255.240", 14, ""),
        (29, "255.255.255.248", 6, ""),
        (30, "255.255.255.252", 2, ""),
        (31, "255.255.255.254", 2, "Point-to-Point"),
        (32, "255.255.255.255", 1, "Host"),
    ];
}
