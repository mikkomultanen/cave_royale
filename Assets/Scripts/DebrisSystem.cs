using System;
using UnityEngine;

namespace CaveRoyale {
    public interface DebrisSystem : IDisposable
    {
        void Update();

        void DispatchEmitIndirect(ComputeBuffer uploads);

        void Emit(Vector2 position, Vector2 velocity);
    }
}
