using System.Diagnostics;
using System.Net.Sockets;
using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>登录前的连通性探测（1.5s 超时，不阻塞界面）。</summary>
public static class ConnectivityProbe
{
    public static (string host, int port) ResolveEndpoint(SapSystem system)
    {
        switch (system.Kind)
        {
            case ConnKind.LoadBalanced:
                return (system.MessageServer, 3600);
            case ConnKind.Router when !string.IsNullOrWhiteSpace(system.Router):
            {
                var host = ShortcutFileBuilder.ExtractRouterHost(system.Router);
                var port = ShortcutFileBuilder.ExtractRouterPort(system.Router) ?? 3299;
                return (host, port);
            }
            default:
                return (system.Host, system.Port);
        }
    }

    public static async Task<(ProbeState state, int latencyMs)> ProbeAsync(SapSystem system, int timeoutMs = 1500,
        CancellationToken ct = default)
    {
        var (host, port) = ResolveEndpoint(system);
        if (string.IsNullOrWhiteSpace(host) || port <= 0) return (ProbeState.Unknown, 0);

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port, ct).AsTask();
            var timeoutTask = Task.Delay(timeoutMs, ct);
            var finished = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
            if (finished != connectTask)
            {
                sw.Stop();
                return (ProbeState.Offline, 0);
            }

            await connectTask.ConfigureAwait(false);
            sw.Stop();
            return (client.Connected, (int)sw.ElapsedMilliseconds) switch
            {
                (true, var ms) => (ProbeState.Online, ms),
                _ => (ProbeState.Offline, 0)
            };
        }
        catch (OperationCanceledException)
        {
            return (ProbeState.Unknown, 0);
        }
        catch
        {
            sw.Stop();
            return (ProbeState.Offline, 0);
        }
    }

    public static async Task ProbeAllAsync(IEnumerable<SapSystem> systems, CancellationToken ct = default)
    {
        var tasks = systems.Select(async s =>
        {
            s.Probe = ProbeState.Probing;
            var (state, ms) = await ProbeAsync(s, 1500, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;
            s.Probe = state;
            s.ProbeLatency = ms;
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
