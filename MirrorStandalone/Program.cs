using Mirror;
using Mirror.KCP;

namespace MirrorStandalone
{
    class Program
    {
        static void Main(string[] args)
        {
            NetworkServer server = new NetworkServer();
            server.Transport = new KcpTransport();

            _ = server.ListenAsync();

            while(true)
            {

            }
        }
    }
}
