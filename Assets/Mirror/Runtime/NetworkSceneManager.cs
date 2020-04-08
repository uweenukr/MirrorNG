using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mirror
{
    [AddComponentMenu("Network/NetworkSceneManager")]
    [RequireComponent(typeof(NetworkServer))]
    [RequireComponent(typeof(NetworkClient))]
    public class NetworkSceneManager : MonoBehaviour
    {
        public NetworkClient client;
        public NetworkServer server;

        /// <summary>
        /// The name of the current network scene.
        /// </summary>
        /// <remarks>
        /// <para>This is populated if the NetworkManager is doing scene management. This should not be changed directly. Calls to ServerChangeScene() cause this to change. New clients that connect to a server will automatically load this scene.</para>
        /// <para>This is used to make sure that all scene changes are initialized by Mirror.</para>
        /// <para>Loading a scene manually wont set networkSceneName, so Mirror would still load it again on start.</para>
        /// </remarks>
        public string networkSceneName = "";

        public AsyncOperation loadingSceneAsync;

        /// <summary>
        /// This is true if the client loaded a new scene when connecting to the server.
        /// <para>This is set before OnClientConnect is called, so it can be checked there to perform different logic if a scene load occurred.</para>
        /// </summary>
        [NonSerialized]
        public bool clientLoadedScene;

        private void Start()
        {
            Initialize();

            // Set the networkSceneName to prevent a scene reload
            // if client connection to server fails.
            networkSceneName = null;

            // setup OnSceneLoaded callback
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void Initialize()
        {
            if (server != null)
                server.Authenticated.AddListener(OnServerAuthenticated);

            // subscribe to the client
            if (client != null)
                client.Authenticated.AddListener(OnClientAuthenticated);
        }

        // NetworkIdentity.UNetStaticUpdate is called from UnityEngine while LLAPI network is active.
        // If we want TCP then we need to call it manually. Probably best from NetworkManager, although this means that we can't use NetworkServer/NetworkClient without a NetworkManager invoking Update anymore.
        /// <summary>
        /// virtual so that inheriting classes' LateUpdate() can call base.LateUpdate() too
        /// </summary>
        public virtual void LateUpdate()
        {
            UpdateScene();
        }

        void UpdateScene()
        {
            if (loadingSceneAsync != null && loadingSceneAsync.isDone)
            {
                if (LogFilter.Debug) Debug.Log("ClientChangeScene done readyCon:" + client.Connection);
                FinishLoadScene();
                loadingSceneAsync.allowSceneActivation = true;
                loadingSceneAsync = null;
            }
        }

        /// <summary>
        /// virtual so that inheriting classes' OnValidate() can call base.OnValidate() too
        /// </summary>
        public virtual void OnValidate()
        {
            // add NetworkServer if there is none yet. makes upgrading easier.
            if (GetComponent<NetworkServer>() == null)
            {
                server = gameObject.AddComponent<NetworkServer>();
                Debug.Log("NetworkSceneManager: added NetworkServer because there was none yet.");
#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(gameObject, "Added NetworkServer");
#endif
            }

            // add NetworkClient if there is none yet. makes upgrading easier.
            if (GetComponent<NetworkClient>() == null)
            {
                client = gameObject.AddComponent<NetworkClient>();
                Debug.Log("NetworkSceneManager: added NetworkClient because there was none yet.");
#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(gameObject, "Added NetworkClient");
#endif
            }
        }

        // support additive scene loads:
        //   NetworkScenePostProcess disables all scene objects on load, and
        //   * NetworkServer.SpawnObjects enables them again on the server when
        //     calling OnStartServer
        //   * ClientScene.PrepareToSpawnSceneObjects enables them again on the
        //     client after the server sends ObjectSpawnStartedMessage to client
        //     in SpawnObserversForConnection. this is only called when the
        //     client joins, so we need to rebuild scene objects manually again
        // TODO merge this with FinishLoadScene()?
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive)
            {
                if (server.active)
                {
                    // TODO only respawn the server objects from that scene later!
                    server.SpawnObjects();
                    if (LogFilter.Debug) Debug.Log("Respawned Server objects after additive scene load: " + scene.name);
                }
                if (client.Active)
                {
                    client.PrepareToSpawnSceneObjects();
                    if (LogFilter.Debug) Debug.Log("Rebuild Client spawnableObjects after additive scene load: " + scene.name);
                }
            }
        }


        #region Server

        // called after successful authentication
        void OnServerAuthenticated(NetworkConnection conn)
        {
            if (LogFilter.Debug) Debug.Log("NetworkSceneManager.OnServerAuthenticated");

            // proceed with the login handshake by calling OnServerConnect
            if (!string.IsNullOrEmpty(networkSceneName))
            {
                var msg = new SceneMessage { sceneName = networkSceneName };
                conn.Send(msg);
            }
        }

        /// <summary>
        /// This causes the server to switch scenes and sets the networkSceneName.
        /// <para>Clients that connect to this server will automatically switch to this scene. This is called autmatically if onlineScene or offlineScene are set, but it can be called from user code to switch scenes again while the game is in progress. This automatically sets clients to be not-ready. The clients must call NetworkClient.Ready() again to participate in the new scene.</para>
        /// </summary>
        /// <param name="newSceneName"></param>
        public virtual void ServerChangeScene(string newSceneName)
        {
            if (string.IsNullOrEmpty(newSceneName))
            {
                Debug.LogError("ServerChangeScene empty scene name");
                return;
            }

            if (LogFilter.Debug) Debug.Log("ServerChangeScene " + newSceneName);
            server.SetAllClientsNotReady();
            networkSceneName = newSceneName;

            // Let server prepare for scene change
            OnServerChangeScene(newSceneName);

            loadingSceneAsync = SceneManager.LoadSceneAsync(newSceneName);

            // notify all clients about the new scene
            server.SendToAll(new SceneMessage { sceneName = newSceneName });
        }

        /// <summary>
        /// Called on the server when a scene is completed loaded, when the scene load was initiated by the server with ServerChangeScene().
        /// </summary>
        /// <param name="sceneName">The name of the new scene.</param>
        public virtual void OnServerSceneChanged(string sceneName) { }

        /// <summary>
        /// Called from ServerChangeScene immediately before SceneManager.LoadSceneAsync is executed
        /// <para>This allows server to do work / cleanup / prep before the scene changes.</para>
        /// </summary>
        /// <param name="newSceneName">Name of the scene that's about to be loaded</param>
        public virtual void OnServerChangeScene(string newSceneName) { }

        #endregion

        #region Client

        void RegisterClientMessages(NetworkConnection connection)
        {
            connection.RegisterHandler<NetworkConnectionToServer, SceneMessage>(OnClientSceneInternal, false);
        }
        void OnClientAuthenticated(NetworkConnectionToServer conn)
        {
            RegisterClientMessages(conn);

            if (LogFilter.Debug) Debug.Log("NetworkSceneManager.OnClientAuthenticated");

            // will wait for scene id to come from the server.
            clientLoadedScene = true;
        }

        void OnClientSceneInternal(NetworkConnection conn, SceneMessage msg)
        {
            if (LogFilter.Debug) Debug.Log("NetworkSceneManager.OnClientSceneInternal");

            if (client.IsConnected && !server.active)
            {
                ClientChangeScene(msg.sceneName, msg.sceneOperation, msg.customHandling);
            }
        }

        internal void ClientChangeScene(string newSceneName, SceneOperation sceneOperation = SceneOperation.Normal, bool customHandling = false)
        {
            if (string.IsNullOrEmpty(newSceneName))
            {
                Debug.LogError("ClientChangeScene empty scene name");
                return;
            }

            if (LogFilter.Debug) Debug.Log("ClientChangeScene newSceneName:" + newSceneName + " networkSceneName:" + networkSceneName);

            // vis2k: pause message handling while loading scene. otherwise we will process messages and then lose all
            // the state as soon as the load is finishing, causing all kinds of bugs because of missing state.
            // (client may be null after StopClient etc.)
            if (LogFilter.Debug) Debug.Log("ClientChangeScene: pausing handlers while scene is loading to avoid data loss after scene was loaded.");
            // Let client prepare for scene change
            OnClientChangeScene(newSceneName, sceneOperation, customHandling);

            // scene handling will happen in overrides of OnClientChangeScene and/or OnClientSceneChanged
            if (customHandling)
            {
                FinishLoadScene();
                return;
            }

            switch (sceneOperation)
            {
                case SceneOperation.Normal:
                    loadingSceneAsync = SceneManager.LoadSceneAsync(newSceneName);
                    break;
                case SceneOperation.LoadAdditive:
                    if (!SceneManager.GetSceneByName(newSceneName).IsValid())
                        loadingSceneAsync = SceneManager.LoadSceneAsync(newSceneName, LoadSceneMode.Additive);
                    else
                        Debug.LogWarningFormat("Scene {0} is already loaded", newSceneName);
                    break;
                case SceneOperation.UnloadAdditive:
                    if (SceneManager.GetSceneByName(newSceneName).IsValid())
                    {
                        if (SceneManager.GetSceneByName(newSceneName) != null)
                            loadingSceneAsync = SceneManager.UnloadSceneAsync(newSceneName, UnloadSceneOptions.UnloadAllEmbeddedSceneObjects);
                    }
                    else
                        Debug.LogWarning("Cannot unload the active scene with UnloadAdditive operation");
                    break;
            }

            // don't change the client's current networkSceneName when loading additive scene content
            if (sceneOperation == SceneOperation.Normal)
                networkSceneName = newSceneName;
        }

        /// <summary>
        /// Called from ClientChangeScene immediately before SceneManager.LoadSceneAsync is executed
        /// <para>This allows client to do work / cleanup / prep before the scene changes.</para>
        /// </summary>
        /// <param name="newSceneName">Name of the scene that's about to be loaded</param>
        /// <param name="sceneOperation">Scene operation that's about to happen</param>
        /// <param name="customHandling">true to indicate that scene loading will be handled through overrides</param>
        public virtual void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling) { }

        /// <summary>
        /// Called on clients when a scene has completed loaded, when the scene load was initiated by the server.
        /// <para>Scene changes can cause player objects to be destroyed. The default implementation of OnClientSceneChanged in the NetworkManager is to add a player object for the connection if no player object exists.</para>
        /// </summary>
        /// <param name="conn">The network connection that the scene change message arrived on.</param>
        public virtual void OnClientSceneChanged(NetworkConnectionToServer conn)
        {
            // always become ready.
            if (!client.ready)
                client.Ready(conn);
        }

        #endregion

        #region Needs Refactor
        void FinishLoadScene()
        {
            // host mode?
            if (client.IsLocalClient)
            {
                FinishLoadSceneHost();
            }
            // server-only mode?
            else if (server.active)
            {
                FinishLoadSceneServerOnly();
            }
            // client-only mode?
            else if (client.Active)
            {
                FinishLoadSceneClientOnly();
            }
        }

        // finish load scene part for host mode. makes code easier and is
        // necessary for FinishStartHost later.
        // (the 3 things have to happen in that exact order)
        void FinishLoadSceneHost()
        {
            // debug message is very important. if we ever break anything then
            // it's very obvious to notice.
            if (LogFilter.Debug) Debug.Log("Finished loading scene in host mode.");

            if (client.Connection != null)
            {
                client.OnAuthenticated(client.Connection);
                clientLoadedScene = true;
                //client.Connection = null; TODO: Is this needed? It was causing errors now
            }

            FinishStartHost();

            // call OnServerSceneChanged
            OnServerSceneChanged(networkSceneName);

            if (client.IsConnected)
            {
                // let client know that we changed scene
                OnClientSceneChanged(client.Connection);
            }
        }

        // finish load scene part for client-only. makes code easier and is
        // necessary for FinishStartClient later.
        void FinishLoadSceneClientOnly()
        {
            // debug message is very important. if we ever break anything then
            // it's very obvious to notice.
            if (LogFilter.Debug) Debug.Log("Finished loading scene in client-only mode.");

            if (client.Connection != null)
            {
                client.OnAuthenticated(client.Connection);
                clientLoadedScene = true;
                client.Connection = null;
            }

            if (client.IsConnected)
            {
                OnClientSceneChanged(client.Connection);
            }
        }

        // finish load scene part for server-only. . makes code easier and is
        // necessary for FinishStartServer later.
        void FinishLoadSceneServerOnly()
        {
            // debug message is very important. if we ever break anything then
            // it's very obvious to notice.
            if (LogFilter.Debug) Debug.Log("Finished loading scene in server-only mode.");

            server.SpawnObjects();
            OnServerSceneChanged(networkSceneName);
        }

        void FinishStartHost()
        {
            // server scene was loaded. now spawn all the objects
            server.SpawnObjects();

            // connect client and call OnStartClient AFTER server scene was
            // loaded and all objects were spawned.
            // DO NOT do this earlier. it would cause race conditions where a
            // client will do things before the server is even fully started.
            if (LogFilter.Debug) Debug.Log("StartHostClient called");

            server.ActivateHostScene();

            RegisterClientMessages(client.Connection);
        }

        #endregion
    }
}
