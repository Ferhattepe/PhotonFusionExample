using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sources
{
    public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] private NetworkPrefabRef playerPrefab;
        [SerializeField] private Canvas canvas;
        private Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();

        private NetworkRunner _runner;
        private bool _mouseButton0;
        private bool _mouseButton1;

        private void Update()
        {
            _mouseButton0 = _mouseButton0 | Input.GetMouseButton(0);
            _mouseButton1 = _mouseButton1 | Input.GetMouseButton(1);
        }

        private async void Start()
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;

            var result = await _runner.JoinSessionLobby(SessionLobby.ClientServer);

            if (result.Ok)
            {
                canvas.enabled = true;
            }
            else
            {
                Debug.LogError($"Failed to Start: {result.ShutdownReason}");
            }
        }

        public void ButtonHost()
        {
            StartGame(GameMode.Host);
        }

        public void ButtonClient()
        {
            StartGame(GameMode.Client);
        }

        async void StartGame(GameMode mode)
        {
            await _runner.StartGame(new StartGameArgs()
            {
                GameMode = mode,
                SessionName = "Dota",
                Scene = SceneManager.GetActiveScene().buildIndex,
                SceneObjectProvider = gameObject.AddComponent<NetworkSceneManagerDefault>(),
                CustomLobbyName = "PrivateLobby"
            });
            canvas.enabled = false;
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            var spawnPosition = new Vector3((player.RawEncoded % runner.Config.Simulation.DefaultPlayers) * 3, 1, 0);
            var networkPlayerObject = runner.Spawn(playerPrefab, spawnPosition, Quaternion.identity, player);
            _spawnedCharacters.Add(player, networkPlayerObject);
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (_spawnedCharacters.TryGetValue(player, out var networkObject))
            {
                runner.Despawn(networkObject);
                _spawnedCharacters.Remove(player);
            }
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            var data = new NetworkInputData();
            if (Input.GetKey(KeyCode.W))
            {
                data.direction += Vector3.forward;
            }

            if (Input.GetKey(KeyCode.S))
            {
                data.direction += Vector3.back;
            }

            if (Input.GetKey(KeyCode.A))
            {
                data.direction += Vector3.left;
            }

            if (Input.GetKey(KeyCode.D))
            {
                data.direction += Vector3.right;
            }

            if (_mouseButton0)
                data.buttons |= NetworkInputData.MOUSEBUTTON1;

            if (_mouseButton1)
                data.buttons |= NetworkInputData.MOUSEBUTTON2;

            _mouseButton0 = false;
            _mouseButton1 = false;
            input.Set(data);
        }

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
        {
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
        }

        public void OnConnectedToServer(NetworkRunner runner)
        {
        }

        public void OnDisconnectedFromServer(NetworkRunner runner)
        {
        }

        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request,
            byte[] token)
        {
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
        }

        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            Debug.LogError("OnSessionListUpdated");
            foreach (var session in sessionList)
            {
                Debug.LogError($"Session {session.Name}");
            }
        }

        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
        {
        }

        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ArraySegment<byte> data)
        {
        }

        public void OnSceneLoadDone(NetworkRunner runner)
        {
        }

        public void OnSceneLoadStart(NetworkRunner runner)
        {
        }
    }
}