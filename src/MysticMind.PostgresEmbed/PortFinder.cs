using System.Net;
using System.Net.Sockets;

namespace MysticMind.PostgresEmbed;

internal static class PortFinder
{
    /// <summary>Asks the OS for a free TCP port on the loopback interface.</summary>
    public static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
