using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using static Mirror.Tests.AsyncUtil;
using Object = UnityEngine.Object;

namespace Mirror.Tests
{
    public class TestClientAuthenticator : NetworkAuthenticator
    {
        public int called;

        public override void OnClientAuthenticate(INetworkConnection conn)
        {
            ++called;
        }
    }

    [TestFixture]
    public class NetworkClientTest
    {
        NetworkServer server;
        GameObject serverGO;
        NetworkClient client;

        GameObject gameObject;
        NetworkIdentity identity;

        [UnitySetUp]
        public IEnumerator SetUp() => RunAsync(async () =>
        {
            serverGO = new GameObject();
            serverGO.AddComponent<MockTransport>();

            server = serverGO.AddComponent<NetworkServer>();
            client = serverGO.AddComponent<NetworkClient>();

            gameObject = new GameObject();
            identity = gameObject.AddComponent<NetworkIdentity>();

            await server.ListenAsync();
        });

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameObject);

            // reset all state
            server.Disconnect();
            Object.DestroyImmediate(serverGO);
        }

        [Test]
        public void IsConnectedTest()
        {
            Assert.That(!client.IsConnected);

            client.ConnectHost(server);

            Assert.That(client.IsConnected);
        }

        [Test]
        public void ConnectionTest()
        {
            Assert.That(client.Connection == null);

            client.ConnectHost(server);

            Assert.That(client.Connection != null);
        }

        [UnityTest]
        public IEnumerable LocalPlayerTest()
        {
            Assert.That(client.LocalPlayer == null);

            PlayerSpawner spawner = serverGO.AddComponent<PlayerSpawner>();

            spawner.server = server;
            spawner.client = client;
            spawner.playerPrefab = identity;
            spawner.Start();

            client.ConnectHost(server);

            yield return null;

            Assert.That(client.LocalPlayer != null);
        }

        [Test]
        public void CurrentTest()
        {
            Assert.That(NetworkClient.Current == null);
        }

        [UnityTest]
        public IEnumerable AuthenticatorTest()
        {
            Assert.That(client.authenticator == null);
            TestClientAuthenticator comp = serverGO.AddComponent<TestClientAuthenticator>();

            yield return null;

            Assert.That(client.authenticator != null);
            client.ConnectHost(server);

            Assert.That(comp.called, Is.EqualTo(1));
        }
    }
}
