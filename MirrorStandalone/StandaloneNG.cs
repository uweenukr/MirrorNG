using Mirror.KCP;

namespace Mirror.Standalone
{
    public static class Globals
    {
        //I know...this is temporary!
        public const string productName = "CHANGE_ME";
    }

    public class StandaloneNG
    {
        public NetworkServer server;
        KcpTransport servertransport;

        public NetworkClient client;
        KcpTransport clienttransport;

        public StandaloneNG()
        {
            server = new NetworkServer();
            servertransport = new KcpTransport();
            server.Transport = servertransport;
            _ = server.ListenAsync();

            client = new NetworkClient();
            clienttransport = new KcpTransport();
            client.Transport = clienttransport;
            client.ConnectAsync("localhost");
        }

        public void Update()
        {
            servertransport.Update();
            clienttransport.Update();
        }
    }
}
