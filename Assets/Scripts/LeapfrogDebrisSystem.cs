using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CaveRoyale {
    public class LeapfrogDebrisSystem : DebrisSystem
    {
        //group size
        private const int THREADS = 128;
        private const int THREADS_TERRAIN = 8;
        //Macros
        private const int READ = 0;
        private const int WRITE = 1;
        private float timestep;
        private int maxIterations;
        private Material material;
        private Bounds bounds;
        private TerrainSystem terrainSystem;
        private float nextFrameTime = 0;
	    private List<Vector4> explosionsList = new List<Vector4>();
        private ComputeBuffer explosionsBuffer;
        // Particle data
        private ComputeBuffer[] positionsBuffers;
        private ComputeBuffer[] velocitiesBuffers;
        private ComputeBuffer lifetimesBuffer;
        private ComputeBuffer motionsBuffer;
        private ComputeBuffer colorsBuffer;

        // Particle pool
        private ComputeBuffer deadBuffer;

        // Particles to render
        private ComputeBuffer aliveBuffer;

        // Particles which should be added to terrain
        private ComputeBuffer addTerrainBuffer;

        // Size of the pool buffer
        private ComputeBuffer counter;

        // Size of the emit buffer
        private ComputeBuffer emitCounter;

        // Rendering arguments
        private ComputeBuffer argsBuffer;

        private Mesh mesh;
        private ComputeShader computeShader;
        private GridHash hash;
        private List<Vector4> emitList = new List<Vector4>();

        public LeapfrogDebrisSystem(int maxNumParticles, float timestep, int maxIterations, Material material, Bounds bounds, TerrainSystem terrainSystem)
        {
            this.timestep = timestep;
            this.maxIterations = maxIterations;
            this.material = material;
            this.bounds = bounds;
            this.terrainSystem = terrainSystem;

    		explosionsBuffer = new ComputeBuffer(16, Marshal.SizeOf(typeof(Vector4)), ComputeBufferType.Default);

            positionsBuffers = new ComputeBuffer[2];
            positionsBuffers[0] = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)));
            positionsBuffers[1] = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)));
            velocitiesBuffers = new ComputeBuffer[2];
            velocitiesBuffers[0] = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)));
            velocitiesBuffers[1] = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)));
            lifetimesBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)));
            motionsBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(float)));
            colorsBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector4)));

            deadBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Append);
            deadBuffer.SetCounterValue(0);
            aliveBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Append);
            aliveBuffer.SetCounterValue(0);
            addTerrainBuffer = new ComputeBuffer(maxNumParticles, Marshal.SizeOf(typeof(Vector2)), ComputeBufferType.Append);
            addTerrainBuffer.SetCounterValue(0);

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

            computeShader = (ComputeShader)Resources.Load("LeapfrogDebrisSystem");
            computeShader.SetFloat("DT", timestep);
            computeShader.SetVector("Gravity", new Vector2(0, -98.1f));
            computeShader.SetFloat("Damping", 0.99f);
    		computeShader.SetVector("_TerrainDistanceFieldScale", terrainSystem.terrainDistanceFieldScale);
            computeShader.SetFloat("_TerrainDistanceFieldMultiplier", terrainSystem.terrainDistanceFieldMultiplier);
            computeShader.SetVector("_TerrainSize", new Vector2(terrainSystem.width, terrainSystem.height));

            int initKernel = computeShader.FindKernel("Init");
            computeShader.SetInt("Width", maxNumParticles);
            computeShader.SetBuffer(initKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(initKernel, "Dead", deadBuffer);
            computeShader.Dispatch(initKernel, Groups(maxNumParticles), 1, 1);

            hash = new GridHash(bounds, maxNumParticles, 1.1283791671f);
            computeShader.SetFloat("HashScale", hash.InvCellSize);
            computeShader.SetVector("HashSize", hash.Bounds.size);
            computeShader.SetVector("HashTranslate", hash.Bounds.min);
        }

        public void Update()
        {
            nextFrameTime += Time.deltaTime;
            float lifetimeTimestep = 0;
            for (int j = 0; j < maxIterations && nextFrameTime > timestep; j++) {
                nextFrameTime -= timestep;
                lifetimeTimestep += timestep;
                DispatchUpdatePositionAndVelocity();
                hash.Process(positionsBuffers[READ], lifetimesBuffer);
                for (int i = 0; i < 4; i++) {
                    DispatchSolveCollisions();
                }
            }

            DispatchUpdate(lifetimeTimestep);

            DispatchAddTerrain();

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
            computeShader.SetBuffer(emitKernel, "PositionsWRITE", positionsBuffers[READ]);
            computeShader.SetBuffer(emitKernel, "VelocitiesWRITE", velocitiesBuffers[READ]);
            computeShader.SetBuffer(emitKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(emitKernel, "Motions", motionsBuffer);
            computeShader.Dispatch(emitKernel, Groups(uploads.count), 1, 1);
        }

        public void EmitExplosion(Vector2 position, float radius) {
            if (explosionsList.Count < explosionsBuffer.count) {
                Vector4 e = position;
                e.z = radius * radius;
                e.w = radius;
                explosionsList.Add(e);
            }
        }

        public void DispatchDestroyTerrain()
        {
            if (explosionsList.Count > 0) {
                int destroyTerrainKernel = computeShader.FindKernel("DestroyTerrain");
                int width = terrainSystem.terrain.width;
                int height = terrainSystem.terrain.height;
                explosionsBuffer.SetData(explosionsList);
                ComputeBuffer.CopyCount(deadBuffer, counter, 0);
                computeShader.SetInt("Count", explosionsList.Count);
                computeShader.SetInt("Width", width);
                computeShader.SetInt("Height", height);
                computeShader.SetInt("CounterOffset", 0);
                computeShader.SetFloat("Lifetime", 30);
                computeShader.SetTexture(destroyTerrainKernel, "_Terrain", terrainSystem.terrain);
                computeShader.SetBuffer(destroyTerrainKernel, "Explosions", explosionsBuffer);
                computeShader.SetBuffer(destroyTerrainKernel, "Counter", counter);
                computeShader.SetBuffer(destroyTerrainKernel, "Pool", deadBuffer);
                computeShader.SetBuffer(destroyTerrainKernel, "PositionsWRITE", positionsBuffers[READ]);
                computeShader.SetBuffer(destroyTerrainKernel, "VelocitiesWRITE", velocitiesBuffers[READ]);
                computeShader.SetBuffer(destroyTerrainKernel, "Lifetimes", lifetimesBuffer);
                computeShader.SetBuffer(destroyTerrainKernel, "Motions", motionsBuffer);
                computeShader.SetBuffer(destroyTerrainKernel, "Colors", colorsBuffer);

                computeShader.Dispatch(destroyTerrainKernel, GroupsTerrain(width), GroupsTerrain(height), 1);
                explosionsList.Clear();
            }
        }

        private void DispatchUpdatePositionAndVelocity()
        {
            int updateKernel = computeShader.FindKernel("UpdatePositionAndVelocity");
            computeShader.SetInt("Width", lifetimesBuffer.count);
            computeShader.SetBuffer(updateKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(updateKernel, "Motions", motionsBuffer);
            computeShader.SetBuffer(updateKernel, "PositionsREAD", positionsBuffers[READ]);
            computeShader.SetBuffer(updateKernel, "VelocitiesREAD", velocitiesBuffers[READ]);
            computeShader.SetBuffer(updateKernel, "PositionsWRITE", positionsBuffers[WRITE]);
            computeShader.SetBuffer(updateKernel, "VelocitiesWRITE", velocitiesBuffers[WRITE]);
            computeShader.Dispatch(updateKernel, Groups(lifetimesBuffer.count), 1, 1);
            ComputeUtilities.Swap(positionsBuffers);
            ComputeUtilities.Swap(velocitiesBuffers);
        }

        private void DispatchSolveCollisions()
        {
            int solveConstraintsKernel = computeShader.FindKernel("SolveCollisions");
            computeShader.SetBuffer(solveConstraintsKernel, "IndexMap", hash.IndexMap);
            computeShader.SetBuffer(solveConstraintsKernel, "Table", hash.Table);
            computeShader.SetTexture(solveConstraintsKernel, "_TerrainDistanceField", terrainSystem.terrainDistanceField);
            computeShader.SetBuffer(solveConstraintsKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(solveConstraintsKernel, "PositionsREAD", positionsBuffers[READ]);
            computeShader.SetBuffer(solveConstraintsKernel, "VelocitiesREAD", velocitiesBuffers[READ]);
            computeShader.SetBuffer(solveConstraintsKernel, "PositionsWRITE", positionsBuffers[WRITE]);
            computeShader.SetBuffer(solveConstraintsKernel, "VelocitiesWRITE", velocitiesBuffers[WRITE]);
            computeShader.Dispatch(solveConstraintsKernel, Groups(lifetimesBuffer.count), 1, 1);
            ComputeUtilities.Swap(positionsBuffers);
            ComputeUtilities.Swap(velocitiesBuffers);
        }

        private void DispatchUpdate(float lifetimeTimestep)
        {
            int updateKernel = computeShader.FindKernel("Update");
    		aliveBuffer.SetCounterValue(0);
            addTerrainBuffer.SetCounterValue(0);
            computeShader.SetFloat("LifetimeDT", lifetimeTimestep);
            computeShader.SetInt("Width", lifetimesBuffer.count);
            computeShader.SetBuffer(updateKernel, "Lifetimes", lifetimesBuffer);
            computeShader.SetBuffer(updateKernel, "Motions", motionsBuffer);
            computeShader.SetBuffer(updateKernel, "Dead", deadBuffer);
            computeShader.SetBuffer(updateKernel, "Alive", aliveBuffer);
            computeShader.SetBuffer(updateKernel, "AddTerrainAPPEND", addTerrainBuffer);
            computeShader.Dispatch(updateKernel, Groups(lifetimesBuffer.count), 1, 1);
        }

        private void DispatchAddTerrain()
        {
            int addTerrainKernel = computeShader.FindKernel("AddTerrain");
            ComputeBuffer.CopyCount(addTerrainBuffer, counter, 0);
            computeShader.SetInt("CounterOffset", 0);
            computeShader.SetTexture(addTerrainKernel, "_Terrain", terrainSystem.terrain);
            computeShader.SetBuffer(addTerrainKernel, "Counter", counter);
            computeShader.SetBuffer(addTerrainKernel, "AddTerrainREAD", addTerrainBuffer);
		    computeShader.SetBuffer(addTerrainKernel, "PositionsREAD", positionsBuffers[READ]);
		    computeShader.SetBuffer(addTerrainKernel, "Colors", colorsBuffer);
            computeShader.Dispatch(addTerrainKernel, Groups(addTerrainBuffer.count), 1, 1);
        }

        private void Render()
        {
            ComputeBuffer.CopyCount(aliveBuffer, argsBuffer, Marshal.SizeOf(typeof(uint)));
		    material.SetBuffer("_Positions", positionsBuffers[READ]);
		    material.SetBuffer("_Alive", aliveBuffer);
            material.SetBuffer("_Motions", motionsBuffer);
            material.SetBuffer("_Colors", colorsBuffer);
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer, 0);
        }

        public void Emit(Vector2 position, Vector2 velocity) {
        }

        private int Groups(int count)
        {
            int groups = count / THREADS;
            if (count % THREADS != 0) groups++;
            return groups;
        }

        private int GroupsTerrain(int count)
        {
            int groups = count / THREADS_TERRAIN;
            if (count % THREADS_TERRAIN != 0) groups++;
            return groups;
        }

        public void Dispose()
        {
            ComputeUtilities.Release(ref explosionsBuffer);
            ComputeUtilities.Release(positionsBuffers);
            ComputeUtilities.Release(velocitiesBuffers);
            ComputeUtilities.Release(ref lifetimesBuffer);
            ComputeUtilities.Release(ref motionsBuffer);
            ComputeUtilities.Release(ref colorsBuffer);
            ComputeUtilities.Release(ref deadBuffer);
            ComputeUtilities.Release(ref aliveBuffer);
            ComputeUtilities.Release(ref counter);
            ComputeUtilities.Release(ref emitCounter);
            ComputeUtilities.Release(ref argsBuffer);
            mesh = null;
            hash.Dispose();
        }
    }
}
