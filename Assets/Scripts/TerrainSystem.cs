﻿using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using CaveRoyale;

[ExecuteInEditMode()]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainSystem : MonoBehaviour {

	//group size
	private const int THREADS = 128;
	public int width = 1920;
	public int height = 1080;
	public RenderTexture terrain { get; private set; }
	public RenderTexture terrainDistanceField { get; private set; }
	public float terrainDistanceFieldMultiplier { get; private set; }
	public Vector4 terrainDistanceFieldScale { get; private set; }
	public Material terrainMaterial;
	public Material debrisMaterial;
	private Material material;
	private Material voronoiMaterial;
	private DebrisSystem debrisSystem;

    private void Awake()
    {
        GenerateMesh();
    }

#if UNITY_EDITOR
    [ContextMenu("Generate mesh")]
#endif
    private void GenerateMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
		Mesh mesh = meshFilter.sharedMesh;
		if (mesh == null) {
			meshFilter.mesh = new Mesh ();
			mesh = meshFilter.sharedMesh;
		}

        var vertices = new Vector3[4];
		float halfWidth = width / 2;
		float halfHeight = height / 2;

        vertices[0] = new Vector3(-halfWidth, -halfHeight, 0);
        vertices[1] = new Vector3(halfWidth, -halfHeight, 0);
        vertices[2] = new Vector3(-halfWidth, halfHeight, 0);
        vertices[3] = new Vector3(halfWidth, halfHeight, 0);

        mesh.vertices = vertices;

        var tri = new int[6];

        tri[0] = 0;
        tri[1] = 2;
        tri[2] = 1;

        tri[3] = 2;
        tri[4] = 3;
        tri[5] = 1;

        mesh.triangles = tri;

        var uv = new Vector2[4];

        uv[0] = new Vector2(0, 0);
        uv[1] = new Vector2(1, 0);
        uv[2] = new Vector2(0, 1);
        uv[3] = new Vector2(1, 1);

        mesh.uv = uv;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
    }
	
	private void OnEnable() {
		terrainDistanceField = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);
		terrainDistanceFieldMultiplier = 4;
		terrainDistanceFieldScale = new Vector4(1f / width, 1f / height, width, height);
		terrainDistanceField.antiAliasing = 1;
		terrainDistanceField.filterMode = FilterMode.Bilinear;
		terrainDistanceField.hideFlags = HideFlags.DontSave;
		Shader.SetGlobalFloat("_TerrainDistanceFieldMultiplier", terrainDistanceFieldMultiplier);
		Shader.SetGlobalVector("_TerrainDistanceFieldScale", terrainDistanceFieldScale);
		Shader.SetGlobalTexture("_TerrainDistanceField", terrainDistanceField);
		material = new Material(Shader.Find("CaveRoyale/TerrainDebug"));
		MeshRenderer renderer = GetComponent<MeshRenderer>();
		renderer.material = material;
		voronoiMaterial = new Material(Shader.Find("CaveRoyale/Voronoi"));
	}

	private void Start() {
		if(terrain) {
			DestroyImmediate(terrain);
		}
		if (debrisSystem != null) {
			debrisSystem.Dispose();
			debrisSystem = null;
		}
	}

	private void Update() {
		if (!terrain) {
			Debug.Log("Create terrain");
			terrain = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			terrain.filterMode = FilterMode.Point;
			terrain.enableRandomWrite = true;
			terrain.hideFlags = HideFlags.DontSave;
			terrain.Create();
			Graphics.Blit(terrain, terrain, terrainMaterial);
			Shader.SetGlobalTexture("_Terrain", terrain);
		}

		if (debrisSystem == null) {
			debrisSystem = new LeapfrogDebrisSystem(65536, 1/120f, 3, debrisMaterial, new Bounds(Vector3.zero, new Vector3(width, height, 100)), this);
			//debrisSystem = new VerletDebrisSystem(65536, 1/120f, 3, debrisMaterial, new Bounds(Vector3.zero, new Vector3(width, height, 100)), this);
		}

		debrisSystem.DispatchDestroyTerrain();

		UpdateTerrainDistanceField();

		debrisSystem.Update();
	} 

	private void UpdateTerrainDistanceField() {
		RenderTexture voronoi1 = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB64, RenderTextureReadWrite.Linear, 1);
		voronoi1.filterMode = FilterMode.Point;

 		Graphics.Blit(terrain, voronoi1, voronoiMaterial, 0);

		RenderTexture voronoi2 = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB64, RenderTextureReadWrite.Linear, 1);
		voronoi2.filterMode = FilterMode.Point;

		voronoiMaterial.SetInt("_Offset", 4);
		Graphics.Blit(voronoi1, voronoi2, voronoiMaterial, 1);
		voronoiMaterial.SetInt("_Offset", 2);
		Graphics.Blit(voronoi2, voronoi1, voronoiMaterial, 1);
		voronoiMaterial.SetInt("_Offset", 1);
		Graphics.Blit(voronoi1, voronoi2, voronoiMaterial, 1);

		voronoiMaterial.SetVector("_DistanceScale", terrainDistanceFieldScale);
		voronoiMaterial.SetFloat("_DistanceMultiplier", terrainDistanceFieldMultiplier);

		RenderTexture distanceField = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.R16, RenderTextureReadWrite.Linear, 1);
		distanceField.filterMode = FilterMode.Bilinear;
		Graphics.Blit(voronoi2, distanceField, voronoiMaterial, 2);

		RenderTexture.ReleaseTemporary(voronoi1);
		RenderTexture.ReleaseTemporary(voronoi2);

		Graphics.Blit(distanceField, terrainDistanceField, voronoiMaterial, 4);

		RenderTexture.ReleaseTemporary(distanceField);
	}

	private void OnDisable() {
		DestroyImmediate(terrain);
		DestroyImmediate(terrainDistanceField);
		DestroyImmediate(material);
		DestroyImmediate(voronoiMaterial);
		if (debrisSystem != null) {
			debrisSystem.Dispose();
			debrisSystem = null;
		}
	}

	public void EmitExplosion(Vector2 position, float radius) {
		if (debrisSystem != null) {
			debrisSystem.EmitExplosion(position, radius);
		}
	}

	public void EmitDebris(Vector2 position, Vector2 velocity)
	{
		if (debrisSystem != null) {
			debrisSystem.Emit(position, Vector2.zero);
		}
	}
}
