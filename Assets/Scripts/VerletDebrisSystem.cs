using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CaveRoyale {
    public class VerletDebrisSystem : DebrisSystem
    {
        //group size
        private const int THREADS = 128;
        //Macros
        private const int READ = 0;
        private const int WRITE = 1;
        private float timestep;
        private int maxIterations;
        private Material material;
        private Bounds bounds;
        private TerrainSystem terrainSystem;
        private float nextFrameTime = 0;
        private ComputeBuffer positionsBuffer;
        private ComputeBuffer velocitiesBuffer;
        private ComputeBuffer lifetimesBuffer;
        private ComputeBuffer[] predictedBuffers;
        private ComputeBuffer deadBuffer;
        private ComputeBuffer aliveBuffer;
        private ComputeBuffer emitBuffer;
        private ComputeBuffer counter;
        private ComputeBuffer emitCounter;
        private ComputeBuffer argsBuffer;
        private Mesh mesh;
        private ComputeShader computeShader;
        private GridHash hash;
        private List<Vector4> emitList = new List<Vector4>();

        public VerletDebrisSystem(int maxNumParticles, float timestep, int maxIterations, Material material, Bounds bounds, TerrainSystem terrainSystem)
        {
            this.timestep = timestep;
            this.maxIterations = maxIterations;
            this.material = material;
            this.bounds = bounds;
            this.terrainSystem = terrainSystem;

            positionsBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)));
            velocitiesBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)));
            lifetimesBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)));
            predictedBuffers = new ComputeBuffer[2];
            predictedBuffers[0] = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)));
            predictedBuffers[1] = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)));
            deadBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Append);
            deadBuffer.SetCounterValue(0);
            aliveBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Append);
            aliveBuffer.SetCounterValue(0);
            emitBuffer = new ComputeBuffer(THREADS, Marshal.SizeOf(typeof(Vector4)));
            counter = new ComputeBuffer(4, Marshal.SizeOf(typeof(uint)), ComputeBufferType.IndirectArguments);
            counter.SetData(new int[] { 0, 1, 0, 0 });
            emitCounter = new ComputeBuffer(4, Marshal.SizeOf(typeof(uint)), ComputeBufferType.IndirectArguments);
            emitCounter.SetData(new int[] { 0, 1, 0, 0 });
            argsBuffer = new ComputeBuffer(5, Marshal.SizeOf(typeof(uint)), ComputeBufferType.IndirectArguments);

            GameObject o = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mesh = o.GetComponent<MeshFilter>().sharedMesh;
            GameObject.DestroyImmediate(o);
            uint[] argsData = new uint[] { mesh.GetIndexCount(0), 0, 0, 0, 0 };
            argsBuffer.SetData(argsData);

            computeShader = (ComputeShader)Resources.Load("VerletDebrisSystem");
            computeShader.SetFloat("DT", timestep);
            computeShader.SetVector("Gravity", new Vector2(0, -98.1f));
            computeShader.SetFloat("Damping", 0.5f);
    		computeShader.SetVector("_TerrainDistanceFieldScale", terrainSystem.terrainDistanceFieldScale);
            computeShader.SetFloat("_TerrainDistanceFieldMultiplier", terrainSystem.terrainDistanceFieldMultiplier);

            int initKernel = computeShader.FindKernel("Init");
            computeShader.SetInt("Width", positionsBuffer.count);
            computeShader.SetBuffer(initKernel, "PositionsWRITE", positionsBuffer);
            computeShader.SetBuffer(initKernel, "VelocitiesWRITE", velocitiesBuffer);
            computeShader.SetBuffer(initKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(initKernel, "Dead", deadBuffer);
            computeShader.Dispatch(initKernel, Groups(maxNumParticles), 1, 1);

            hash = new GridHash(bounds, maxNumParticles, 1);
            computeShader.SetFloat("HashScale", hash.InvCellSize);
            computeShader.SetVector("HashSize", hash.Bounds.size);
            computeShader.SetVector("HashTranslate", hash.Bounds.min);
        }

        public void Update()
        {
            DispatchEmit();

            nextFrameTime += Time.deltaTime;
            for (int j = 0; j < maxIterations && nextFrameTime > timestep; j++) {
                nextFrameTime -= timestep;
                DispatchPredictPositions();
                hash.Process(predictedBuffers[READ], lifetimesBuffer);
                for (int i = 0; i < 4; i++) {
                    DispatchSolveConstraints();
                }
                DispatchUpdate();
            }

            Render();
        }

        public void DispatchEmitIndirect(ComputeBuffer uploads) 
        {
            int emitKernel = computeShader.FindKernel("EmitIndirect");
            ComputeBuffer.CopyCount(uploads, emitCounter, 0);
            ComputeBuffer.CopyCount(deadBuffer, counter, 0);
            computeShader.SetInt("UploadCounterOffset", 0);
            computeShader.SetInt("CounterOffset", 0);
            computeShader.SetFloat("Lifetime", 1000);
            computeShader.SetBuffer(emitKernel, "UploadCounter", emitCounter);
            computeShader.SetBuffer(emitKernel, "Counter", counter);
            computeShader.SetBuffer(emitKernel, "Uploads", uploads);
            computeShader.SetBuffer(emitKernel, "Pool", deadBuffer);
            computeShader.SetBuffer(emitKernel, "PositionsWRITE", positionsBuffer);
            computeShader.SetBuffer(emitKernel, "VelocitiesWRITE", velocitiesBuffer);
            computeShader.SetBuffer(emitKernel, "Lifetimes", lifetimesBuffer);
            computeShader.Dispatch(emitKernel, Groups(uploads.count), 1, 1);
        }

        public void DispatchDestroyTerrain()
        {

        }

        private void DispatchEmit()
        {
            if (emitList.Count > 0) {
                int emitKernel = computeShader.FindKernel("Emit");
                ComputeBuffer.CopyCount(deadBuffer, counter, 0);
                emitBuffer.SetData(emitList);
                computeShader.SetInt("CounterOffset", 0);
                computeShader.SetInt("Width", emitList.Count);
                computeShader.SetFloat("Lifetime", 1000);
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

        private void DispatchPredictPositions()
        {
            int predictPositionsKernel = computeShader.FindKernel("PredictPositions");
            computeShader.SetInt("Width", positionsBuffer.count);
            computeShader.SetBuffer(predictPositionsKernel, "PositionsREAD", positionsBuffer);
            computeShader.SetBuffer(predictPositionsKernel, "VelocitiesREAD", velocitiesBuffer);
            computeShader.SetBuffer(predictPositionsKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(predictPositionsKernel, "PredictedWRITE", predictedBuffers[WRITE]);
            computeShader.Dispatch(predictPositionsKernel, Groups(positionsBuffer.count), 1, 1);
            Swap(predictedBuffers);
        }

        private void DispatchSolveConstraints()
        {
            int solveConstraintsKernel = computeShader.FindKernel("SolveConstraints");
            computeShader.SetBuffer(solveConstraintsKernel, "IndexMap", hash.IndexMap);
            computeShader.SetBuffer(solveConstraintsKernel, "Table", hash.Table);
            computeShader.SetTexture(solveConstraintsKernel, "_TerrainDistanceField", terrainSystem.terrainDistanceField);
            computeShader.SetBuffer(solveConstraintsKernel, "PredictedREAD", predictedBuffers[READ]);
            computeShader.SetBuffer(solveConstraintsKernel, "PositionsREAD", positionsBuffer);
            computeShader.SetBuffer(solveConstraintsKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(solveConstraintsKernel, "PredictedWRITE", predictedBuffers[WRITE]);
            computeShader.Dispatch(solveConstraintsKernel, Groups(positionsBuffer.count), 1, 1);
            Swap(predictedBuffers);
        }

        private void DispatchUpdate()
        {
            int updateKernel = computeShader.FindKernel("Update");
    		aliveBuffer.SetCounterValue(0);
            computeShader.SetInt("Width", positionsBuffer.count);
            computeShader.SetBuffer(updateKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(updateKernel, "Dead", deadBuffer);
            computeShader.SetBuffer(updateKernel, "Alive", aliveBuffer);
            computeShader.SetBuffer(updateKernel, "PredictedREAD", predictedBuffers[READ]);
            computeShader.SetBuffer(updateKernel, "PositionsWRITE", positionsBuffer);
            computeShader.SetBuffer(updateKernel, "VelocitiesWRITE", velocitiesBuffer);
            computeShader.Dispatch(updateKernel, Groups(lifetimesBuffer.count), 1, 1);
        }

        private void Render()
        {
            ComputeBuffer.CopyCount(aliveBuffer, argsBuffer, Marshal.SizeOf(typeof(uint)));
		    material.SetBuffer("_Positions", positionsBuffer);
		    material.SetBuffer("_Alive", aliveBuffer);
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer, 0);
        }

        public void EmitExplosion(Vector2 position, float radius)
        {
            
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
            ComputeUtilities.Release(ref emitCounter);
            ComputeUtilities.Release(ref argsBuffer);
            mesh = null;
            hash.Dispose();
        }

        private static void Swap(ComputeBuffer[] buffers)
        {
            ComputeBuffer tmp = buffers[0];
            buffers[0] = buffers[1];
            buffers[1] = tmp;
        }
    }
}
