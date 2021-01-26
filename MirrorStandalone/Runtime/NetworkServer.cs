using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Mirror
{

    /// <summary>
    /// The NetworkServer.
    /// </summary>
    /// <remarks>
    /// <para>NetworkServer handles remote connections from remote clients, and also has a local connection for a local client.</para>
    /// </remarks>
    //[AddComponentMenu("Network/NetworkServer")]
    //[DisallowMultipleComponent]
    public class NetworkServer : INetworkServer
    {
        static readonly ILogger logger = LogFactory.GetLogger(typeof(NetworkServer));

        bool initialized;

        /// <summary>
        /// The maximum number of concurrent network connections to support.
        /// <para>This effects the memory usage of the network layer.</para>
        /// </summary>
        //[Tooltip("Maximum number of concurrent connections.")]
        //[Min(1)]
        public int MaxConnections = 4;

        /// <summary>
        /// This is invoked when a server is started - including when a host is started.
        /// </summary>
        public UnityEvent Started = new UnityEvent();

        /// <summary>
        /// Event fires once a new Client has connect to the Server.
        /// </summary>
        public NetworkConnectionEvent Connected = new NetworkConnectionEvent();

        /// <summary>
        /// Event fires once a new Client has passed Authentication to the Server.
        /// </summary>
        public NetworkConnectionEvent Authenticated = new NetworkConnectionEvent();

        /// <summary>
        /// Event fires once a Client has Disconnected from the Server.
        /// </summary>
        public NetworkConnectionEvent Disconnected = new NetworkConnectionEvent();

        public UnityEvent Stopped = new UnityEvent();

        //[Header("Authentication")]
        //[Tooltip("Authentication component attached to this object")]
        public NetworkAuthenticator authenticator;

        /// <summary>
        /// Number of active player objects across all connections on the server.
        /// <para>This is only valid on the host / server.</para>
        /// </summary>
        public int NumPlayers => connections.Count;

        /// <summary>
        /// A list of local connections on the server.
        /// </summary>
        public readonly HashSet<INetworkConnection> connections = new HashSet<INetworkConnection>();

        /// <summary>
        /// <para>If you disable this, the server will not listen for incoming connections on the regular network port.</para>
        /// <para>This can be used if the game is running in host mode and does not want external players to be able to connect - making it like a single-player game. Also this can be useful when using AddExternalConnection().</para>
        /// </summary>
        public bool Listening = true;

        /// <summary>
        /// <para>Checks if the server has been started.</para>
        /// <para>This will be true after NetworkServer.Listen() has been called.</para>
        /// </summary>
        public bool Active { get; private set; }

        // Time kept in this server
        public readonly NetworkTime Time = new NetworkTime();

        // transport to use to accept connections
        public Transport Transport;

        /// <summary>
        /// This shuts down the server and disconnects all clients.
        /// </summary>
        public void Disconnect()
        {
            // make a copy,  during disconnect, it is possible that connections
            // are modified, so it throws
            // System.InvalidOperationException : Collection was modified; enumeration operation may not execute.
            var connectionscopy = new HashSet<INetworkConnection>(connections);
            foreach (INetworkConnection conn in connectionscopy)
            {
                conn.Disconnect();
            }
            if (Transport != null)
                Transport.Disconnect();
        }

        void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            Application.quitting += Disconnect;
            if (logger.LogEnabled()) logger.Log("NetworkServer Created version " + Version.Current);

            //Make sure connections are cleared in case any old connections references exist from previous sessions
            connections.Clear();

            if (Transport is null)
                Transport = GetComponent<Transport>();
            if (Transport == null)
                throw new InvalidOperationException("Transport could not be found for NetworkServer");

            if (authenticator != null)
            {
                authenticator.OnServerAuthenticated += OnAuthenticated;

                Connected.AddListener(authenticator.OnServerAuthenticateInternal);
            }
            else
            {
                // if no authenticator, consider every connection as authenticated
                Connected.AddListener(OnAuthenticated);
            }
        }

        /// <summary>
        /// Start the server, setting the maximum number of connections.
        /// </summary>
        /// <param name="maxConns">Maximum number of allowed connections</param>
        /// <returns></returns>
        public async UniTask ListenAsync()
        {
            Initialize();

            try
            {
                // only start server if we want to listen
                if (Listening)
                {
                    Transport.Started.AddListener(TransportStarted);
                    Transport.Connected.AddListener(TransportConnected);
                    await Transport.ListenAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogException(ex);
            }
            finally
            {
                Transport.Connected.RemoveListener(TransportConnected);
                Transport.Started.RemoveListener(TransportStarted);
                Cleanup();
            }
        }

        private void TransportStarted()
        {
            logger.Log("Server started listening");
            Active = true;
            // (useful for loading & spawning stuff from database etc.)
            Started.Invoke();
        }

        private void TransportConnected(IConnection connection)
        {
            INetworkConnection networkConnectionToClient = GetNewConnection(connection);
            ConnectionAcceptedAsync(networkConnectionToClient).Forget();
        }

        /// <summary>
        /// cleanup resources so that we can start again
        /// </summary>
        private void Cleanup()
        {

            if (authenticator != null)
            {
                authenticator.OnServerAuthenticated -= OnAuthenticated;
                Connected.RemoveListener(authenticator.OnServerAuthenticateInternal);
            }
            else
            {
                // if no authenticator, consider every connection as authenticated
                Connected.RemoveListener(OnAuthenticated);
            }

            Stopped.Invoke();
            initialized = false;
            Active = false;
        }

        /// <summary>
        /// Creates a new INetworkConnection based on the provided IConnection.
        /// </summary>
        public virtual INetworkConnection GetNewConnection(IConnection connection)
        {
            return new NetworkConnection(connection);
        }

        /// <summary>
        /// <para>This accepts a network connection and adds it to the server.</para>
        /// <para>This connection will use the callbacks registered with the server.</para>
        /// </summary>
        /// <param name="conn">Network connection to add.</param>
        public void AddConnection(INetworkConnection conn)
        {
            if (!connections.Contains(conn))
            {
                // connection cannot be null here or conn.connectionId
                // would throw NRE
                connections.Add(conn);
                conn.RegisterHandler<NetworkPingMessage>(Time.OnServerPing);
            }
        }

        /// <summary>
        /// This removes an external connection added with AddExternalConnection().
        /// </summary>
        /// <param name="connectionId">The id of the connection to remove.</param>
        public void RemoveConnection(INetworkConnection conn)
        {
            connections.Remove(conn);
        }

        /// <summary>
        /// Send a message to all connected clients.
        /// </summary>
        /// <typeparam name="T">Message type</typeparam>
        /// <param name="msg">Message</param>
        /// <param name="channelId">Transport channel to use</param>
        public void SendToAll<T>(T msg, int channelId = Channel.Reliable)
        {
            if (logger.LogEnabled()) logger.Log("Server.SendToAll id:" + typeof(T));
            NetworkConnection.Send(connections, msg, channelId);
        }

        async UniTaskVoid ConnectionAcceptedAsync(INetworkConnection conn)
        {
            if (logger.LogEnabled()) logger.Log("Server accepted client:" + conn);

            // are more connections allowed? if not, kick
            // (it's easier to handle this in Mirror, so Transports can have
            //  less code and third party transport might not do that anyway)
            // (this way we could also send a custom 'tooFull' message later,
            //  Transport can't do that)
            if (connections.Count >= MaxConnections)
            {
                conn.Disconnect();
                if (logger.LogEnabled()) logger.Log("Server full, kicked client:" + conn);
                return;
            }

            // add connection
            AddConnection(conn);

            // let everyone know we just accepted a connection
            Connected.Invoke(conn);

            // now process messages until the connection closes
            try
            {
                await conn.ProcessMessagesAsync();
            }
            catch (Exception ex)
            {
                logger.LogException(ex);
            }
            finally
            {
                OnDisconnected(conn);
            }
        }

        //called once a client disconnects from the server
        void OnDisconnected(INetworkConnection connection)
        {
            if (logger.LogEnabled()) logger.Log("Server disconnect client:" + connection);

            RemoveConnection(connection);

            Disconnected.Invoke(connection);

            connection.DestroyOwnedObjects();
            connection.Identity = null;
        }

        internal void OnAuthenticated(INetworkConnection conn)
        {
            if (logger.LogEnabled()) logger.Log("Server authenticate client:" + conn);

            Authenticated?.Invoke(conn);
        }
    }
}
