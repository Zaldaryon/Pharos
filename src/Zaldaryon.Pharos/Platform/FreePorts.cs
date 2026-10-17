using System.Net;
using System.Net.Sockets;

namespace Zaldaryon.Pharos.Platform;

/// <summary>Ports nothing on this machine listens on.</summary>
internal static class FreePorts
{
    private const int TcpUdpAttempts = 50;

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

    /// <summary>Finds a port that can be bound by both TCP and UDP on the given address.</summary>
    public static int TakeTcpUdp(string address) => TakeTcpUdp(address, ProbeUdp, TcpUdpAttempts);

    internal static int TakeTcpUdp(string address, Action<IPAddress, int> udpProbe, int maxAttempts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentNullException.ThrowIfNull(udpProbe);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        IPAddress ipAddress = IPAddress.Parse(address);
        SocketException? lastUdpFailure = null;
        List<TcpListener> held = [];

        try
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                TcpListener tcpProbe = new(ipAddress, 0);
                tcpProbe.Start();
                // Keep rejected candidates reserved too: otherwise the OS can repeatedly
                // hand the same ephemeral TCP port back after its UDP bind was denied.
                held.Add(tcpProbe);
                int port = Port(tcpProbe);
                try
                {
                    // Keep TCP reserved while checking UDP. The game binds both protocols to
                    // this same port once Boot has finished configuring its network sockets.
                    udpProbe(ipAddress, port);
                    return port;
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
                {
                    lastUdpFailure = ex;
                }
            }

            throw new InvalidOperationException(
                $"Pharos could not find a port available for both TCP and UDP on {address} after {maxAttempts} attempts. Last UDP bind error: {lastUdpFailure?.SocketErrorCode}.",
                lastUdpFailure);
        }
        finally
        {
            foreach (TcpListener probe in held) probe.Stop();
        }
    }

    private static void ProbeUdp(IPAddress address, int port)
    {
        using Socket probe = new(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(address, port));
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
