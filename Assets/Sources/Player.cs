using Fusion;
using UnityEngine;

namespace Sources
{
    public class Player : NetworkBehaviour
    {
        [SerializeField] private Ball _ballPrefab;
        [SerializeField] private PhysxBall _physxBallPrefab;


        private Vector3 _forward;
        private Material _material;

        Material material
        {
            get
            {
                if (_material == null)
                    _material = GetComponentInChildren<MeshRenderer>().material;
                return _material;
            }
        }

        [Networked(OnChanged = nameof(OnBallSpawned))]
        public NetworkBool spawned { get; set; }

        [Networked] private TickTimer delay { get; set; }

        [Networked] private Vector3 NetworkedPosition { get; set; }

        // private NetworkCharacterController _characterController;

        private void Awake()
        {
            // _characterController = GetComponent<NetworkCharacterController>();
        }

        public static void OnBallSpawned(Changed<Player> changed)
        {
            changed.Behaviour.material.color = Color.white;
        }

        public override void Render()
        {
            material.color = Color.Lerp(material.color, Color.blue, Time.deltaTime);
            transform.position = NetworkedPosition;
        }

        private void Update()
        {
            if (Object.HasInputAuthority && Input.GetKeyDown(KeyCode.R))
            {
                RPC_SendMessage("Hey Mate");
            }
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_SendMessage(string message, RpcInfo info = default)
        {
            if (info.Source == Runner.Simulation.LocalPlayer)
                Debug.LogError($"You said: {message}");
            else
                Debug.LogError($"Some other player said: {message}");
        }

        public override void FixedUpdateNetwork()
        {
            if (GetInput(out NetworkInputData data))
            {
                data.direction.Normalize();
                transform.position += data.direction * Runner.DeltaTime * 5;
                // _characterController.Move(data.direction * Runner.DeltaTime * 5);
                if (Object.HasStateAuthority)
                {
                    NetworkedPosition = transform.position;
                }

                if (data.direction.sqrMagnitude > 0)
                {
                    _forward = data.direction;
                }

                if (delay.ExpiredOrNotRunning(Runner))
                {
                    if ((data.buttons & NetworkInputData.MOUSEBUTTON1) != 0)
                    {
                        delay = TickTimer.CreateFromSeconds(Runner, 0.5f);
                        Runner.Spawn(_ballPrefab, transform.position + _forward, Quaternion.LookRotation(_forward),
                            Object.InputAuthority,
                            (runner, networkObject) => { networkObject.GetComponent<Ball>().Init(); });
                        spawned = !spawned;
                    }
                    else if ((data.buttons & NetworkInputData.MOUSEBUTTON2) != 0)
                    {
                        delay = TickTimer.CreateFromSeconds(Runner, 0.5f);
                        Runner.Spawn(_physxBallPrefab, transform.position + _forward, Quaternion.LookRotation(_forward),
                            Object.InputAuthority,
                            (runner, networkObject) =>
                            {
                                networkObject.GetComponent<PhysxBall>().Init(_forward * 10f);
                            });
                    }
                }
            }
        }
    }
}