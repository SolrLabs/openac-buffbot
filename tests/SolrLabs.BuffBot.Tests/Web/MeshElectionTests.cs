using System.Net;
using System.Net.Sockets;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class MeshElectionTests
{
    [Fact]
    public void ASecondListenerOnTheSamePortFailsToBind()
    {
        var first = new TcpListener(IPAddress.Loopback, 0);
        first.Start();
        int port = ((IPEndPoint)first.LocalEndpoint).Port;

        try
        {
            var second = new TcpListener(IPAddress.Loopback, port);
            var thrown = Assert.Throws<SocketException>(second.Start);
            Assert.Equal(SocketError.AddressAlreadyInUse, thrown.SocketErrorCode);
        }
        finally
        {
            first.Stop();
        }
    }
}
