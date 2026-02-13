using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Network;

public sealed class PingService : IPingService
{
    public async Task<PingResult> SinglePingAsync(string target, int timeoutMs = 3000)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(target, timeoutMs);
            return new PingResult
            {
                Timestamp = DateTime.Now,
                Status = reply.Status,
                RoundtripMs = reply.RoundtripTime,
                Ttl = reply.Options?.Ttl ?? 0,
                Target = target
            };
        }
        catch (PingException)
        {
            return new PingResult
            {
                Timestamp = DateTime.Now,
                Status = IPStatus.Unknown,
                RoundtripMs = 0,
                Ttl = 0,
                Target = target
            };
        }
    }

    public async IAsyncEnumerable<PingResult> ContinuousPingAsync(
        string target,
        int intervalMs = 1000,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            // Create a fresh Ping per request — avoids ObjectDisposedException
            // if the previous SendPingAsync overlaps with cancellation disposal.
            using var ping = new Ping();
            PingResult result;

            try
            {
                var reply = await ping.SendPingAsync(target, 3000);
                result = new PingResult
                {
                    Timestamp = DateTime.Now,
                    Status = reply.Status,
                    RoundtripMs = reply.RoundtripTime,
                    Ttl = reply.Options?.Ttl ?? 0,
                    Target = target
                };
            }
            catch (PingException)
            {
                result = new PingResult
                {
                    Timestamp = DateTime.Now,
                    Status = IPStatus.Unknown,
                    RoundtripMs = 0,
                    Ttl = 0,
                    Target = target
                };
            }

            yield return result;

            try
            {
                await Task.Delay(intervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }
}
