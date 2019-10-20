using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

[ExecuteInEditMode()]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainSystem : MonoBehaviour {

	public int width = 1920;
	public int height = 1080;
	public RenderTexture terrain;
	public RenderTexture terrainDistanceField;
	public Vector4 terrainDistanceFieldScale { get; private set; }
	public Material terrainMaterial;
	private Material material;
	private Material voronoiMaterial;
	private ComputeShader computeShader;
	private int destroyTerrainKernel;
	private uint destroyTerrainKernelX;
	private uint destroyTerrainKernelY;
	private ComputeBuffer explosionsBuffer;
	private List<Vector4> explosionsList = new List<Vector4>();

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
		terrainDistanceField = new RenderTexture(width / 2, height / 2, 0, RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);
		terrainDistanceFieldScale = new Vector4(1f / width, 1f / height, width, height);
		terrainDistanceField.antiAliasing = 1;
		terrainDistanceField.isPowerOfTwo = true;
		terrainDistanceField.filterMode = FilterMode.Trilinear;
		terrainDistanceField.hideFlags = HideFlags.DontSave;
		material = new Material(Shader.Find("CaveRoyale/DistanceFieldDebug"));
		material.SetTexture("_MainTex", terrainDistanceField);
		MeshRenderer renderer = GetComponent<MeshRenderer>();
		renderer.material = material;
		voronoiMaterial = new Material(Shader.Find("CaveRoyale/Voronoi"));

		computeShader = (ComputeShader)Resources.Load("UpdateTerrain");
		destroyTerrainKernel = computeShader.FindKernel("DestroyTerrain");
		uint z;
		computeShader.GetKernelThreadGroupSizes(destroyTerrainKernel, out destroyTerrainKernelX, out destroyTerrainKernelY, out z);
		explosionsBuffer = new ComputeBuffer(16, Marshal.SizeOf(typeof(Vector4)), ComputeBufferType.Default);
	}

	private void Start() {
		if(terrain) {
			DestroyImmediate(terrain);
		}
	}

	private void Update() {
		if (!terrain) {
			Debug.Log("Create terrain");
			terrain = new RenderTexture(width, height, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
			terrain.filterMode = FilterMode.Point;
			terrain.enableRandomWrite = true;
			Graphics.Blit(terrain, terrain, terrainMaterial);
		}

		if (explosionsList.Count > 0) {
			explosionsBuffer.SetData(explosionsList);
			computeShader.SetInt("_Count", explosionsList.Count);
			computeShader.SetTexture(destroyTerrainKernel, "terrain", terrain);
			computeShader.SetBuffer(destroyTerrainKernel, "explosions", explosionsBuffer);
			int x = terrain.width / (int)destroyTerrainKernelX;
			int y = terrain.height / (int)destroyTerrainKernelY;
			computeShader.Dispatch(destroyTerrainKernel, x, y, 1);
			explosionsList.Clear();
		}
	} 

	private void LateUpdate() {
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

		RenderTexture distanceField = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, 1);
		distanceField.filterMode = FilterMode.Bilinear;
		Graphics.Blit(voronoi2, distanceField, voronoiMaterial, 2);

		RenderTexture.ReleaseTemporary(voronoi1);
		RenderTexture.ReleaseTemporary(voronoi2);

		voronoiMaterial.SetFloat("_BoxOffset", 1);
		Graphics.Blit(distanceField, terrainDistanceField, voronoiMaterial, 3);

		RenderTexture.ReleaseTemporary(distanceField);
	}

	private void OnDisable() {
		DestroyImmediate(terrain);
		DestroyImmediate(terrainDistanceField);
		DestroyImmediate(material);
		DestroyImmediate(voronoiMaterial);
		explosionsBuffer.Release();
	}

	public void EmitExplosion(Vector2 position, float radius) {
		if (explosionsList.Count < explosionsBuffer.count) {
			Vector4 e = position;
			e.x += width / 2;
			e.y += height / 2;
			e.z = radius * radius;
			explosionsList.Add(e);
		}
	}
}
