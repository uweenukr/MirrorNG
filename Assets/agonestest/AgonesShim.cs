using Agones;
using Mirror;
using UnityEngine;

public class AgonesShim : MonoBehaviour
{
    public NetworkServer server;
    public AgonesSdk sdk;

    public void Awake()
    {
        server.Started.AddListener(ConnectWrapper);
        server.Stopped.AddListener(ShutdownWrapper);
    }

    public async void ConnectWrapper()
    {
        if(await sdk.Connect())
        {
            _ = sdk.Ready();
        }
    }

    public void ShutdownWrapper()
    {
        sdk.healthEnabled = false;
        _ = sdk.Shutdown();
    }
}
