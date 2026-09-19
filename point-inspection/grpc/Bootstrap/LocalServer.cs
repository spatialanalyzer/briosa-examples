using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Inspection.Bootstrap;

// This owns only the exact process it creates. RPCs stay in Program.cs.
internal sealed class LocalServer(Process process, BriosaInstallation installation, Uri endpoint) : IAsyncDisposable
{
    internal BriosaInstallation Installation { get; } = installation;
    internal Uri Endpoint { get; } = endpoint;
    internal bool HasExited => process.HasExited;

    internal static LocalServer Start(BriosaServerSelection options)
    {
        var selected = BriosaInstallations.Resolve(options);
        if (InstalledServerDiscovery.ReadExecutable(selected.ExecutablePath, selected.Scope) != selected)
            throw new IOException("server-installation-changed");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo(selected.ExecutablePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(selected.ExecutablePath)!,
        };
        start.ArgumentList.Add($"--Briosa:Endpoint:Port={port}");
        if (options.SpatialAnalyzerExecutablePath is { } sa)
            start.ArgumentList.Add("--Briosa:SpatialAnalyzer:ExecutablePath=" + sa);
        var process = Process.Start(start) ?? throw new IOException("server-process-not-created");
        return new(process, selected, new Uri($"http://127.0.0.1:{port}"));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
}
