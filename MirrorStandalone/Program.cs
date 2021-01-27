using Mirror.KCP;

namespace Mirror.Standalone
{
    public static class Globals
    {
        //I know...this is temporary!
        public const string productName = "CHANGE_ME";
    }

    class Program
    {
        static void Main(string[] args)
        {
            NetworkServer server = new NetworkServer();
            server.Transport = new KcpTransport();

            _ = server.ListenAsync();

            NetworkClient client = new NetworkClient();
            client.Transport = new KcpTransport();

            client.ConnectAsync("localhost");

            while(true)
            {

            }
        }
    }
}
