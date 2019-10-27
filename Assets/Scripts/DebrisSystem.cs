using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CaveRoyale {
    public class DebrisSystem : IDisposable
    {
        //group size
        private const int THREADS = 128;
        //Macros
        private const int READ = 0;
        private const int WRITE = 1;
        private float timestep;
        private Material material;
        private Bounds bounds;
        private float nextFrameTime = 0;
        private ComputeBuffer positionsBuffer;
        private ComputeBuffer velocitiesBuffer;
        private ComputeBuffer lifetimesBuffer;
        private ComputeBuffer[] predictedBuffers;
        private ComputeBuffer deadBuffer;
        private ComputeBuffer aliveBuffer;
        private ComputeBuffer emitBuffer;
        private ComputeBuffer counter;
        private ComputeBuffer argsBuffer;
        private Mesh mesh;
        private ComputeShader computeShader;
        private List<Vector4> emitList = new List<Vector4>();

        public DebrisSystem(int maxNumParticles, float timestep, Material material, Bounds bounds)
        {
            this.timestep = timestep;
            this.material = material;
            this.bounds = bounds;

            positionsBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector4)));
            velocitiesBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector4)));
            lifetimesBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector4)));
            predictedBuffers = new ComputeBuffer[2];
            predictedBuffers[0] = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector4)));
            predictedBuffers[1] = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector4)));
            deadBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Append);
            deadBuffer.SetCounterValue(0);
            aliveBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Append);
            aliveBuffer.SetCounterValue(0);
            emitBuffer = new ComputeBuffer(THREADS, Marshal.SizeOf(typeof(Vector4)));
            counter = new ComputeBuffer(4, Marshal.SizeOf(typeof(uint)), ComputeBufferType.IndirectArguments);
            counter.SetData(new int[] { 0, 1, 0, 0 });
            argsBuffer = new ComputeBuffer(5, Marshal.SizeOf(typeof(uint)), ComputeBufferType.IndirectArguments);

            GameObject o = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mesh = o.GetComponent<MeshFilter>().sharedMesh;
            GameObject.DestroyImmediate(o);
            uint[] argsData = new uint[] { mesh.GetIndexCount(0), 0, 0, 0, 0 };
            argsBuffer.SetData(argsData);

            computeShader = (ComputeShader)Resources.Load("DebrisSystem");
            computeShader.SetFloat("DT", timestep);

            int initKernel = computeShader.FindKernel("Init");
            computeShader.SetBuffer(initKernel, "PositionsWRITE", positionsBuffer);
            computeShader.SetBuffer(initKernel, "VelocitiesWRITE", velocitiesBuffer);
            computeShader.SetBuffer(initKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(initKernel, "Dead", deadBuffer);
            computeShader.Dispatch(initKernel, Groups(maxNumParticles), 1, 1);
        }

        public void Update()
        {
            DispatchEmit();

            nextFrameTime += Time.deltaTime;
            while (nextFrameTime > timestep) {
                nextFrameTime -= timestep;
                DispatchUpdate();
            }

            Render();
        }

        private void DispatchEmit()
        {
            if (emitList.Count > 0) {
                Debug.Log("Emit debris: " + emitList.Count);
                int emitKernel = computeShader.FindKernel("Emit");
                ComputeBuffer.CopyCount(deadBuffer, counter, 0);
                emitBuffer.SetData(emitList);
                computeShader.SetInt("CounterOffset", 0);
                computeShader.SetInt("Width", emitList.Count);
                computeShader.SetFloat("Lifetime", 10);
                computeShader.SetBuffer(emitKernel, "Counter", counter);
                computeShader.SetBuffer(emitKernel, "Uploads", emitBuffer);
                computeShader.SetBuffer(emitKernel, "Pool", deadBuffer);
                computeShader.SetBuffer(emitKernel, "PositionsWRITE", positionsBuffer);
                computeShader.SetBuffer(emitKernel, "VelocitiesWRITE", velocitiesBuffer);
                computeShader.SetBuffer(emitKernel, "Lifetimes", lifetimesBuffer);
                computeShader.Dispatch(emitKernel, Groups(emitList.Count), 1, 1);
                emitList.Clear();
            }
        }

        private void DispatchUpdate()
        {
            int updateKernel = computeShader.FindKernel("Update");
    		aliveBuffer.SetCounterValue(0);
            computeShader.SetBuffer(updateKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(updateKernel, "Dead", deadBuffer);
            computeShader.SetBuffer(updateKernel, "Alive", aliveBuffer);
            computeShader.Dispatch(updateKernel, Groups(lifetimesBuffer.count), 1, 1);
        }

        private void Render()
        {
            ComputeBuffer.CopyCount(aliveBuffer, argsBuffer, Marshal.SizeOf(typeof(uint)));
		    material.SetBuffer("_Positions", positionsBuffer);
		    material.SetBuffer("_Alive", aliveBuffer);
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer, 0);
        }

        public void Emit(Vector2 position, Vector2 velocity) {
            if (emitList.Count < emitBuffer.count) {
                Vector4 e = position;
                e.z = velocity.x;
                e.w = velocity.y;
                emitList.Add(e);
            }
        }

        private int Groups(int count)
        {
            int groups = count / THREADS;
            if (count % THREADS != 0) groups++;
            return groups;
        }

        public void Dispose()
        {
            ComputeUtilities.Release(ref positionsBuffer);
            ComputeUtilities.Release(ref velocitiesBuffer);
            ComputeUtilities.Release(ref lifetimesBuffer);
            ComputeUtilities.Release(predictedBuffers);
            ComputeUtilities.Release(ref deadBuffer);
            ComputeUtilities.Release(ref aliveBuffer);
            ComputeUtilities.Release(ref counter);
            mesh = null;
        }
    }
}
