using System;
using UnityEngine;

namespace CaveRoyale {
    public interface DebrisSystem : IDisposable
    {
        void Update();

        void DispatchEmitIndirect(ComputeBuffer uploads);
        void DispatchDestroyTerrain();

        void EmitExplosion(Vector2 position, float radius);
        void Emit(Vector2 position, Vector2 velocity);
    }
}
