using System.Net;
using System.Net.Sockets;

namespace Zaldaryon.Pharos.Platform;

/// <summary>Ports nothing on this machine listens on.</summary>
internal static class FreePorts
{
    /// <summary>
    /// <paramref name="count"/> different ports, each free on every address: a mod usually binds
    /// all of them. The ports are held until they are all found, then released for the caller to
    /// use. Another process could take one between the release and its use; the ports come from
    /// the system's ephemeral range, which makes that rare.
    /// </summary>
    public static IReadOnlyList<int> Take(int count, ICollection<int>? exclude = null)
    {
        List<int> ports = [];
        List<TcpListener> held = [];
        try
        {
            for (int attempt = 0; ports.Count < count; attempt++)
            {
                if (attempt >= count + 50) throw new InvalidOperationException($"Pharos could not find {count} free ports.");

                // Every probe stays open until the end, so the system does not hand its port out twice.
                TcpListener probe = new(IPAddress.Any, 0);
                probe.Start();
                held.Add(probe);
                int port = Port(probe);
                if (exclude?.Contains(port) != true && FreeOnIPv6(port)) ports.Add(port);
            }

            return ports;
        }
        finally
        {
            foreach (TcpListener probe in held) probe.Stop();
        }
    }

    private static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    private static bool FreeOnIPv6(int port)
    {
        if (!Socket.OSSupportsIPv6) return true;
        try
        {
            using Socket socket = new(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            socket.DualMode = false;
            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
