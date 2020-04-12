using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Mirror
{
    public enum ConnectState
    {
        None,
        Connecting,
        Connected,
        Disconnected
    }

    /// <summary>
    /// This is a network client class used by the networking system. It contains a NetworkConnection that is used to connect to a network server.
    /// <para>The <see cref="NetworkClient">NetworkClient</see> handle connection state, messages handlers, and connection configuration. There can be many <see cref="NetworkClient">NetworkClient</see> instances in a process at a time, but only one that is connected to a game server (<see cref="NetworkServer">NetworkServer</see>) that uses spawned objects.</para>
    /// <para><see cref="NetworkClient">NetworkClient</see> has an internal update function where it handles events from the transport layer. This includes asynchronous connect events, disconnect events and incoming data from a server.</para>
    /// <para>The <see cref="NetworkManager">NetworkManager</see> has a NetworkClient instance that it uses for games that it starts, but the NetworkClient may be used by itself.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkClient : MonoBehaviour
    {
        static readonly ILogger logger = LogFactory.GetLogger(typeof(NetworkClient));

        [Header("Authentication")]
        [Tooltip("Authentication component attached to this object")]
        public NetworkAuthenticator authenticator;

        public ClientObjectManager clientObjectManager;

        [Serializable] public class NetworkConnectionEvent : UnityEvent<INetworkConnection> { }

        public NetworkConnectionEvent Connected = new NetworkConnectionEvent();
        public NetworkConnectionEvent Authenticated = new NetworkConnectionEvent();
        public UnityEvent Disconnected = new UnityEvent();

        /// <summary>
        /// The NetworkConnection object this client is using.
        /// </summary>
        public INetworkConnection Connection { get; internal set; }

        /// <summary>
        /// NetworkIdentity of the localPlayer
        /// </summary>
        public NetworkIdentity LocalPlayer => Connection?.Identity;

        internal ConnectState connectState = ConnectState.None;

        /// <summary>
        /// active is true while a client is connecting/connected
        /// (= while the network is active)
        /// </summary>
        public bool Active => connectState == ConnectState.Connecting || connectState == ConnectState.Connected;

        /// <summary>
        /// This gives the current connection status of the client.
        /// </summary>
        public bool IsConnected => connectState == ConnectState.Connected;

        public readonly NetworkTime Time = new NetworkTime();

        public AsyncTransport Transport;

        /// <summary>
        /// The host server
        /// </summary>
        NetworkServer hostServer;

        /// <summary>
        /// NetworkClient can connect to local server in host mode too
        /// </summary>
        public bool IsLocalClient => hostServer != null;

        /// <summary>
        /// Connect client to a NetworkServer instance.
        /// </summary>
        /// <param name="uri">Address of the server to connect to</param>
        public async Task ConnectAsync(Uri uri)
        {
            if (logger.LogEnabled()) logger.Log("Client Connect: " + uri);

            AsyncTransport transport = Transport;
            if (transport == null)
                transport = GetComponent<AsyncTransport>();

            connectState = ConnectState.Connecting;

            try
            {
                IConnection transportConnection = await transport.ConnectAsync(uri);

                InitializeAuthEvents();

                // setup all the handlers
                Connection = new NetworkConnection(transportConnection);
                Time.Reset();
           
                RegisterMessageHandlers(Connection);
                Time.UpdateClient(this);
                _ = OnConnected();
            }
            catch (Exception)
            {
                connectState = ConnectState.Disconnected;
                throw;
            }
        }

        internal void ConnectHost(NetworkServer server)
        {
            logger.Log("Client Connect Host to Server");
            connectState = ConnectState.Connected;

            InitializeAuthEvents();

            // create local connection objects and connect them
            (IConnection c1, IConnection c2) = PipeConnection.CreatePipe();

            server.SetLocalConnection(this, c2);
            hostServer = server;
            Connection = new NetworkConnection(c1);
            RegisterHostHandlers(Connection);
            _ = OnConnected();
        }

        void InitializeAuthEvents()
        {
            if (authenticator != null)
            {
                authenticator.OnClientAuthenticated += OnAuthenticated;

                Connected.AddListener(authenticator.OnClientAuthenticateInternal);
            }
            else
            {
                // if no authenticator, consider connection as authenticated
                Connected.AddListener(OnAuthenticated);
            }
        }

        /// <summary>
        /// client that received the message
        /// </summary>
        /// <remarks>This is a hack, but it is needed to deserialize
        /// gameobjects when processing the message</remarks>
        /// 
        internal static NetworkClient Current { get; set; }

        async Task OnConnected()
        {
            // reset network time stats
            // the handler may want to send messages to the client
            // thus we should set the connected state before calling the handler
            connectState = ConnectState.Connected;
            Connected.Invoke(Connection);

            // start processing messages
            try
            {
                await Connection.ProcessMessagesAsync();
            }
            catch (Exception ex)
            {
                logger.LogException(ex);
            }
            finally
            {
                Cleanup();

                Disconnected.Invoke();
            }

        }

        public void OnAuthenticated(INetworkConnection conn)
        {
            Authenticated?.Invoke(conn);
        }

        /// <summary>
        /// Disconnect from server.
        /// <para>The disconnect message will be invoked.</para>
        /// </summary>
        public void Disconnect()
        {
            Connection?.Disconnect();
        }

        /// <summary>
        /// This sends a network message with a message Id to the server. This message is sent on channel zero, which by default is the reliable channel.
        /// <para>The message must be an instance of a class derived from MessageBase.</para>
        /// <para>The message id passed to Send() is used to identify the handler function to invoke on the server when the message is received.</para>
        /// </summary>
        /// <typeparam name="T">The message type to unregister.</typeparam>
        /// <param name="message"></param>
        /// <param name="channelId"></param>
        /// <returns>True if message was sent.</returns>
        public Task SendAsync<T>(T message, int channelId = Channels.DefaultReliable) where T : IMessageBase
        {
            return Connection.SendAsync(message, channelId);
        }

        public void Send<T>(T message, int channelId = Channels.DefaultReliable) where T : IMessageBase
        {
            _ = Connection.SendAsync(message, channelId);
        }

        internal void Update()
        {
            // local connection?
            if (!IsLocalClient && Active && connectState == ConnectState.Connected)
            {
                // only update things while connected
                Time.UpdateClient(this);
            }
        }

        internal void RegisterHostHandlers(INetworkConnection connection)
        {
            connection.RegisterHandler<NetworkPongMessage>(msg => { });
        }

        internal void RegisterMessageHandlers(INetworkConnection connection)
        {
            connection.RegisterHandler<NetworkPongMessage>(Time.OnClientPong);
        }

        /// <summary>
        /// Shut down a client.
        /// <para>This should be done when a client is no longer going to be used.</para>
        /// </summary>
        void Cleanup()
        {
            logger.Log("Shutting down client.");

            connectState = ConnectState.None;

            if (authenticator != null)
            { 
                authenticator.OnClientAuthenticated -= OnAuthenticated;

                Connected.RemoveListener(authenticator.OnClientAuthenticateInternal);
            }
            else
            {
                // if no authenticator, consider connection as authenticated
                Connected.RemoveListener(OnAuthenticated);
            }
        }
    }
}
