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
                TcpListener? tcpProbe = null;
                try
                {
                    // Windows reserves port ranges for each protocol on its own (Hyper-V, WinNAT), and the system only
                    // skips the ranges of the protocol it is asked for. Every other attempt takes its candidate from
                    // UDP, so TCP candidates that all sit in a range reserved for UDP do not exhaust the attempts.
                    int candidate = attempt % 2 == 0 ? UdpCandidate(ipAddress) : 0;
                    tcpProbe = new TcpListener(ipAddress, candidate);
                    tcpProbe.Start();
                    // Keep rejected candidates reserved too: otherwise the OS can repeatedly
                    // hand the same ephemeral TCP port back after its UDP bind was denied.
                    held.Add(tcpProbe);
                    tcpProbe = null;
                    int port = Port(held[^1]);

                    // Keep TCP reserved while checking UDP. The game binds both protocols to
                    // this same port once Boot has finished configuring its network sockets.
                    udpProbe(ipAddress, port);
                    return port;
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
                {
                    tcpProbe?.Dispose();
                    lastUdpFailure = ex;
                }
            }

            throw new InvalidOperationException(
                $"Pharos could not find a port available for both TCP and UDP on {address} after {maxAttempts} attempts. Last bind error: {lastUdpFailure?.SocketErrorCode}.",
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

    private static int UdpCandidate(IPAddress address)
    {
        using Socket probe = new(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(address, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
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
