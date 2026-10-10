using UnityEngine;

namespace PTRCraft
{
	/// <summary>The outline of the block Minecraft's crosshair is on, drawn in PTR's scene.</summary>
	internal static unsafe class Selection
	{
		private static GameObject box;
		private static Mesh mesh;

		private static void Ensure()
		{
			if (box != null) return;
			box = new GameObject("PTRCraftSelection");
			Object.DontDestroyOnLoad(box);
			mesh = new Mesh();
			var v = new Vector3[8];
			for (int i = 0; i < 8; i++) v[i] = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
			mesh.vertices = v;
			int[] e = { 0, 1, 1, 3, 3, 2, 2, 0, 4, 5, 5, 7, 7, 6, 6, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
			mesh.SetIndices(e, MeshTopology.Lines, 0);
			box.AddComponent<MeshFilter>().sharedMesh = mesh;
			var mr = box.AddComponent<MeshRenderer>();
			var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
			var mat = new Material(shader);
			mat.color = new Color(0f, 0f, 0f, 0.8f);
			Object.DontDestroyOnLoad(mat);
			mr.sharedMaterial = mat;
			mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		}

		public static void Update(Link link)
		{
			long o = Proto.OffWorldEntities;
			byte* b = link.Base;
			uint seq1 = *(uint*)(b + o);
			if ((seq1 & 1) != 0) return;
			uint has = *(uint*)(b + o + 8);
			float* mn = (float*)(b + o + 0x0C);
			float* mx = (float*)(b + o + 0x18);
			float x0 = mn[0], y0 = mn[1], z0 = mn[2], x1 = mx[0], y1 = mx[1], z1 = mx[2];
			if (*(uint*)(b + o) != seq1) return;
			if (has == 0)
			{
				if (box != null) box.SetActive(false);
				return;
			}
			Ensure();
			box.SetActive(true);
			// MC box -> Unity (z mirrored): min corner in Unity is (x0, y0, -z1)
			const float g = 0.002f;
			var a = Mapping.ToUnity(x0 - g, y0 - g, z1 + g);
			box.transform.position = a;
			box.transform.localScale = new Vector3(x1 - x0 + 2 * g, y1 - y0 + 2 * g, z1 - z0 + 2 * g);
		}

		public static void Hide()
		{
			if (box != null) box.SetActive(false);
		}
	}
}
