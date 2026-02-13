using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Network;

[SupportedOSPlatform("windows")]
public sealed class TracerouteService : ITracerouteService
{
    public async IAsyncEnumerable<TracerouteHop> TraceAsync(
        string target,
        int maxHops = 30,
        int timeoutMs = 3000,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Resolve hostname to IP first
        IPAddress targetIp;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(target, ct);
            targetIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                       ?? addresses.First();
        }
        catch (SocketException)
        {
            yield break;
        }

        var buffer = new byte[32];
        Array.Fill(buffer, (byte)0x41);

        for (int ttl = 1; ttl <= maxHops; ttl++)
        {
            ct.ThrowIfCancellationRequested();

            var options = new PingOptions(ttl, true);
            long[] roundtrips = new long[3];
            string hopAddress = "*";
            bool timedOut = true;
            bool reachedTarget = false;

            // Send 3 probes per hop (standard traceroute behaviour)
            for (int probe = 0; probe < 3; probe++)
            {
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(targetIp, timeoutMs, buffer, options);

                    if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success)
                    {
                        hopAddress = reply.Address?.ToString() ?? "*";
                        roundtrips[probe] = reply.RoundtripTime;
                        timedOut = false;

                        if (reply.Status == IPStatus.Success)
                            reachedTarget = true;
                    }
                    else
                    {
                        roundtrips[probe] = 0;
                    }
                }
                catch (PingException)
                {
                    roundtrips[probe] = 0;
                }
            }

            // Reverse DNS lookup for the hop (non-blocking, best-effort)
            string? hostname = null;
            if (!timedOut && hopAddress != "*")
            {
                try
                {
                    if (IPAddress.TryParse(hopAddress, out var hopIp))
                    {
                        var entry = await Dns.GetHostEntryAsync(hopIp);
                        if (entry.HostName != hopAddress)
                            hostname = entry.HostName;
                    }
                }
                catch
                {
                    // Reverse DNS failure is expected — many hops don't resolve
                }
            }

            yield return new TracerouteHop
            {
                HopNumber = ttl,
                Address = hopAddress,
                Hostname = hostname,
                RoundtripMs1 = roundtrips[0],
                RoundtripMs2 = roundtrips[1],
                RoundtripMs3 = roundtrips[2],
                TimedOut = timedOut
            };

            if (reachedTarget)
                yield break;
        }
    }
}
