using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using static Mirror.Tests.AsyncUtil;
using Object = UnityEngine.Object;

namespace Mirror.Tests
{
    [TestFixture]
    public class ClientObjectManagerTest
    {
        NetworkServer server;
        GameObject serverGO;
        NetworkClient client;
        ClientObjectManager clientObjectManager;

        GameObject gameObject;
        NetworkIdentity identity;

        [UnitySetUp]
        public IEnumerator SetUp() => RunAsync(async () =>
        {
            serverGO = new GameObject();
            serverGO.AddComponent<MockTransport>();

            server = serverGO.AddComponent<NetworkServer>();
            client = serverGO.AddComponent<NetworkClient>();
            clientObjectManager = serverGO.AddComponent<ClientObjectManager>();
            clientObjectManager.client = client;
            clientObjectManager.server = server;

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
        public void ReadyTest()
        {
            Assert.That(!clientObjectManager.ready);

            client.ConnectHost(server);

            clientObjectManager.Ready(client.Connection);
            Assert.That(clientObjectManager.ready);
            Assert.That(client.Connection.IsReady);
        }

        [Test]
        public void ReadyTwiceTest()
        {
            client.ConnectHost(server);

            clientObjectManager.Ready(client.Connection);

            Assert.Throws<InvalidOperationException>(() =>
            {
                clientObjectManager.Ready(client.Connection);
            });
        }

        [Test]
        public void ReadyNull()
        {
            client.ConnectHost(server);

            Assert.Throws<InvalidOperationException>(() =>
            {
                clientObjectManager.Ready(null);
            });
        }

        [UnityTest]
        public IEnumerable RemovePlayerTest()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = clientObjectManager.RemovePlayer();
            });

            PlayerSpawner spawner = serverGO.AddComponent<PlayerSpawner>();

            spawner.server = server;
            spawner.client = client;
            spawner.playerPrefab = identity;
            spawner.Start();

            client.ConnectHost(server);

            yield return null;

            Assert.That(client.LocalPlayer != null);

            Assert.That(clientObjectManager.RemovePlayer());
            Assert.That(identity == null);
            Assert.That(client.LocalPlayer == null);
        }

        [Test]
        public void RegisterPrefabTest()
        {
            Guid guid = Guid.NewGuid();
            clientObjectManager.RegisterPrefab(gameObject, guid);

            Assert.That(gameObject.GetComponent<NetworkIdentity>().AssetId == guid);
        }

        [Test]
        public void RegisterPrefabExceptionTest()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                clientObjectManager.RegisterPrefab(new GameObject());
            });
        }

        [Test]
        public void RegisterPrefabGuidExceptionTest()
        {
            Guid guid = Guid.NewGuid();

            Assert.Throws<InvalidOperationException>(() =>
            {
                clientObjectManager.RegisterPrefab(new GameObject(), guid);
            });
        }

        [Test]
        public void OnSpawnAssetSceneIDFailureExceptionTest()
        {
            SpawnMessage msg = new SpawnMessage();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            {
                clientObjectManager.OnSpawn(msg);
            });

            Assert.That(ex.Message, Is.EqualTo("OnObjSpawn netId: " + msg.netId + " has invalid asset Id"));
        }

        [Test]
        public void UnregisterPrefabExceptionTest()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                clientObjectManager.UnregisterPrefab(new GameObject());
            });
        }

        [UnityTest]
        public IEnumerable GetPrefabTest()
        {
            Guid guid = Guid.NewGuid();
            clientObjectManager.RegisterPrefab(gameObject, guid);

            yield return null;

            clientObjectManager.GetPrefab(guid, out GameObject result);

            Assert.That(result != null);
            Assert.That(result.GetComponent<NetworkIdentity>().AssetId == guid);
        }
    }
}
