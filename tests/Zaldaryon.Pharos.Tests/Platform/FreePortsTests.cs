using System.Net;
using System.Net.Sockets;
using Xunit;
using Zaldaryon.Pharos.Platform;

namespace Zaldaryon.Pharos.Tests.Platform;

public sealed class FreePortsTests
{
    [Theory]
    [InlineData(SocketError.AddressAlreadyInUse)]
    [InlineData(SocketError.AccessDenied)]
    public void TakeTcpUdpRetriesWhenUdpCannotBindCandidate(SocketError error)
    {
        List<int> candidates = [];
        int port = FreePorts.TakeTcpUdp("127.0.0.1", (_, candidate) =>
        {
            Assert.InRange(candidate, 1, 65535);
            candidates.Add(candidate);
            if (candidates.Count == 1)
                throw new SocketException((int)error);
        }, maxAttempts: 3);

        Assert.Equal(2, candidates.Count);
        Assert.NotEqual(candidates[0], candidates[1]);
        Assert.InRange(port, 1, 65535);
    }

    [Fact]
    public void TakeTcpUdpReturnsPortBindableByBothProtocols()
    {
        int port = FreePorts.TakeTcpUdp("127.0.0.1");

        using TcpListener tcp = new(IPAddress.Loopback, port);
        tcp.Start();
        using Socket udp = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udp.Bind(new IPEndPoint(IPAddress.Loopback, port));
    }

    [Fact]
    public void TakeTcpUdpStopsAfterBoundedAttemptsWithLastBindError()
    {
        int attempts = 0;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            FreePorts.TakeTcpUdp("127.0.0.1", (_, _) =>
            {
                attempts++;
                throw new SocketException((int)SocketError.AccessDenied);
            }, maxAttempts: 3));

        Assert.Equal(3, attempts);
        Assert.Contains("after 3 attempts", error.Message, StringComparison.Ordinal);
        Assert.IsType<SocketException>(error.InnerException);
        Assert.Equal(SocketError.AccessDenied, ((SocketException)error.InnerException!).SocketErrorCode);
    }
}
