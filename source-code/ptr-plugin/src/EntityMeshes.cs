using System;
using System.Collections.Generic;
using UnityEngine;

namespace PTRCraft
{
	/// <summary>
	/// Minecraft's entity renderer output, drawn in PTR: the player's own model (skin, armour, held
	/// items, cape; posed and animated by Minecraft) for the F5 cameras, and every other Minecraft
	/// entity and particle. Positions come relative to the player's feet (avatar) or to an origin
	/// (scene); textures arrive once as RenTexture (id 0 = the block/item atlas).
	/// </summary>
	internal static unsafe class EntityMeshes
	{
		private sealed class Drawn
		{
			public GameObject Go;
			public Mesh Mesh;
			public MeshRenderer Renderer;
			public int VertexCount;
		}

		private static readonly Dictionary<uint, Texture2D> textures = new();
		private static readonly Dictionary<(uint tex, bool translucent), Material> materials = new();
		private static Drawn avatar, scene;
		private static double sceneX, sceneY, sceneZ;
		public static int AvatarVertices => avatar?.VertexCount ?? 0;
		public static int SceneVertices => scene?.VertexCount ?? 0;

		public static void OnTexture(byte* b, int len)
		{
			uint id = *(uint*)b, w = *(uint*)(b + 4), h = *(uint*)(b + 8);
			if (w == 0 || h == 0 || w > 4096 || h > 4096 || 16 + (long)w * h * 4 > len) return;
			if (!textures.TryGetValue(id, out var t) || t == null || t.width != w || t.height != h)
			{
				t = new Texture2D((int)w, (int)h, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
				UnityEngine.Object.DontDestroyOnLoad(t);
				textures[id] = t;
				foreach (var key in new List<(uint, bool)>(materials.Keys))
					if (key.Item1 == id) materials[key].mainTexture = t;
			}
			// rows top-first: loaded as-is, meshes flip v
			t.LoadRawTextureData((IntPtr)(b + 16), (int)(w * h * 4));
			t.Apply(false);
		}

		private static Material MaterialFor(uint tex, bool translucent)
		{
			if (materials.TryGetValue((tex, translucent), out var m) && m != null) return m;
			var baseMat = translucent ? WorldRender.Translucent : WorldRender.Cutout;
			if (baseMat == null) return null;
			m = new Material(baseMat);
			m.mainTexture = tex == 0 ? WorldRender.Atlas : textures.TryGetValue(tex, out var t) ? t : null;
			UnityEngine.Object.DontDestroyOnLoad(m);
			materials[(tex, translucent)] = m;
			return m;
		}

		/// <summary>RenAvatar (player model, feet-relative) or RenScene (origin + everything else).</summary>
		public static void OnMesh(byte* b, int len, bool isScene)
		{
			int head = isScene ? 32 : 8;
			if (len < head) return;
			uint batchCount, vertexCount;
			if (isScene)
			{
				sceneX = *(double*)b;
				sceneY = *(double*)(b + 8);
				sceneZ = *(double*)(b + 16);
				batchCount = *(uint*)(b + 24);
				vertexCount = *(uint*)(b + 28);
			}
			else
			{
				batchCount = *(uint*)b;
				vertexCount = *(uint*)(b + 4);
			}
			ref Drawn d = ref isScene ? ref scene : ref avatar;
			if (d == null)
			{
				d = new Drawn();
				d.Go = new GameObject(isScene ? "PTRCraftMinecraftEntities" : "PTRCraftPlayerModel");
				UnityEngine.Object.DontDestroyOnLoad(d.Go);
				d.Mesh = new Mesh();
				d.Mesh.MarkDynamic();
				d.Go.AddComponent<MeshFilter>().sharedMesh = d.Mesh;
				d.Renderer = d.Go.AddComponent<MeshRenderer>();
			}
			long need = head + (long)batchCount * 16 + (long)vertexCount * 32;
			if (batchCount == 0 || vertexCount == 0 || len < need || !WorldRender.Ready)
			{
				d.VertexCount = 0;
				d.Go.SetActive(false);
				return;
			}
			byte* batches = b + head;
			byte* v = batches + batchCount * 16;
			int n = (int)vertexCount;
			var pos = new Vector3[n];
			var uv = new Vector2[n];
			var col = new Color32[n];
			for (int i = 0; i < n; i++)
			{
				byte* e = v + i * 32;
				pos[i] = new Vector3(*(float*)e, *(float*)(e + 4), -*(float*)(e + 8));
				uv[i] = new Vector2(*(float*)(e + 12), 1f - *(float*)(e + 16));
				col[i] = new Color32(e[20], e[21], e[22], e[23]);
			}
			var mats = new List<Material>();
			var tris = new List<int[]>();
			for (int k = 0; k < batchCount; k++)
			{
				byte* bt = batches + k * 16;
				uint tex = *(uint*)bt, first = *(uint*)(bt + 4), count = *(uint*)(bt + 8), flags = *(uint*)(bt + 12);
				if (first + count > vertexCount) continue;
				var m = MaterialFor(tex, (flags & 1) != 0);
				if (m == null) continue;
				var t = new int[count - count % 3];
				for (int i = 0; i + 2 < t.Length; i += 3)
				{
					// the z mirror flips the winding
					t[i] = (int)first + i;
					t[i + 1] = (int)first + i + 2;
					t[i + 2] = (int)first + i + 1;
				}
				mats.Add(m);
				tris.Add(t);
			}
			var mesh = d.Mesh;
			mesh.Clear();
			mesh.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
			mesh.vertices = pos;
			mesh.uv = uv;
			mesh.colors32 = col;
			mesh.subMeshCount = tris.Count;
			for (int k = 0; k < tris.Count; k++) mesh.SetTriangles(tris[k], k);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			d.Renderer.sharedMaterials = mats.ToArray();
			d.VertexCount = n;
			d.Go.SetActive(true);
			if (isScene) d.Go.transform.position = Mapping.ToUnity(sceneX, sceneY, sceneZ);
		}

		/// <summary>Puts the player model at Minecraft's feet (call late in the frame).</summary>
		public static void PlaceAvatar(double x, double y, double z, bool show)
		{
			if (avatar == null) return;
			bool on = show && avatar.VertexCount > 0;
			if (avatar.Go.activeSelf != on) avatar.Go.SetActive(on);
			if (on) avatar.Go.transform.position = Mapping.ToUnity(x, y, z);
		}

		public static void Hide()
		{
			if (avatar != null) avatar.Go.SetActive(false);
			if (scene != null) scene.Go.SetActive(false);
		}
	}
}
