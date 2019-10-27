using UnityEngine;

[RequireComponent(typeof(Camera))]
public class TerrainTest : MonoBehaviour {
	public TerrainSystem terrainSystem;
	private Camera _camera;
	void Start () {
		_camera = GetComponent<Camera>();
	}
	private void Update() {
		if (Input.GetMouseButton(0)) {
			Vector3 mousePos = new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0f);
			Vector3 viewPos = _camera.ScreenToViewportPoint(mousePos);
			if (viewPos.x > 0 && viewPos.x < 1 && viewPos.y > 0 && viewPos.y < 1) {
				Vector3 wordPos = _camera.ScreenToWorldPoint(mousePos);
				terrainSystem.EmitExplosion(wordPos, 3);
				if (Input.GetMouseButtonDown(0)) {
					terrainSystem.EmitDebris(wordPos, Vector2.zero);
				}
			}
		}
	}
}
