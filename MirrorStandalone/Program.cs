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
            KcpTransport servertransport =  new KcpTransport();
            server.Transport = servertransport;
            _ = server.ListenAsync();

            NetworkClient client = new NetworkClient();
            KcpTransport clienttransport = new KcpTransport();
            client.Transport = clienttransport;
            client.ConnectAsync("localhost");

            while(true)
            {
                servertransport.Update();
                clienttransport.Update();
            }
        }
    }
}
