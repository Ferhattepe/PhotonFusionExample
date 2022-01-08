using Fusion;
using UnityEngine;

namespace Sources
{
    public struct NetworkInputData : INetworkInput
    {
        public const byte MOUSEBUTTON1 = 1;
        public const byte MOUSEBUTTON2 = 2;
        public byte buttons;
        public Vector3 direction;
    }
}