using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace PTRCraft
{
	/// <summary>
	/// Minecraft's own block meshes (built by its block renderer, with tint and ambient occlusion)
	/// drawn inside PTR's scene with PTR's lights, plus colliders so PTR's people bump into your
	/// builds, and real PTR lights for torches and lava.
	/// </summary>
	internal static unsafe class WorldRender
	{
		private sealed class Section
		{
			public GameObject Go;
			public Mesh Mesh;
			public GameObject Solids;
			public readonly List<GameObject> Lights = new();
		}

		private static readonly Dictionary<(int, int, int), Section> sections = new();
		private static Texture2D atlas;
		private static Material opaque, cutout, translucent;
		private static GameObject root;
		public static string ShaderUsed = "";
		public static int SectionCount => sections.Count;
		public static int Messages;
		internal static bool Ready => cutout != null && translucent != null;
		internal static Material Cutout => cutout;
		internal static Material Translucent => translucent;
		internal static Texture2D Atlas => atlas;

		private static void EnsureRoot()
		{
			if (root != null) return;
			root = new GameObject("PTRCraftBlocks");
			UnityEngine.Object.DontDestroyOnLoad(root);
		}

		private static Shader FindShader(params string[] names)
		{
			// Shader.Find only sees shaders that are "always included"; the rest are loaded with
			// PTR's Addressables bundles, so look through every loaded shader too.
			Dictionary<string, Shader> loaded = null;
			foreach (var n in names)
			{
				var s = Shader.Find(n);
				if (s != null && s.isSupported) return s;
				if (loaded == null)
				{
					loaded = new Dictionary<string, Shader>();
					foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Shader>()))
					{
						var sh = o.Cast<Shader>();
						if (sh.isSupported) loaded[sh.name] = sh;
					}
				}
				if (loaded.TryGetValue(n, out s)) return s;
			}
			return null;
		}

		private static void EnsureMaterials()
		{
			if (opaque != null || atlas == null) return;
			var lit = FindShader("Custom/VertexColourStandardUVSpec", "Custom/PTTR_VC", "Particles/Standard Surface", "Legacy Shaders/Diffuse", "Standard");
			var cut = FindShader("Legacy Shaders/Transparent/Cutout/Diffuse", "Unlit/Transparent Cutout");
			var tr = FindShader("Legacy Shaders/Transparent/Diffuse", "Unlit/Transparent", "Sprites/Default");
			ShaderUsed = $"{lit?.name} / {cut?.name} / {tr?.name}";
			Plugin.L.LogInfo("PTRCraft: block shaders " + ShaderUsed);
			opaque = new Material(lit ?? cut);
			opaque.mainTexture = atlas;
			if (opaque.HasProperty("_Glossiness")) opaque.SetFloat("_Glossiness", 0f);
			if (opaque.HasProperty("_Metallic")) opaque.SetFloat("_Metallic", 0f);
			cutout = new Material(cut ?? lit);
			cutout.mainTexture = atlas;
			if (cutout.HasProperty("_Cutoff")) cutout.SetFloat("_Cutoff", 0.5f);
			translucent = new Material(tr ?? cut ?? lit);
			translucent.mainTexture = atlas;
			UnityEngine.Object.DontDestroyOnLoad(opaque);
			UnityEngine.Object.DontDestroyOnLoad(cutout);
			UnityEngine.Object.DontDestroyOnLoad(translucent);
		}

		public static void Pump(Link link)
		{
			link.DrainRender(Handle, 512);
		}

		private static void Handle(uint type, IntPtr p, int len)
		{
			Messages++;
			byte* b = (byte*)p;
			switch (type)
			{
				case Proto.RenAtlas:
				{
					int w = *(int*)b, h = *(int*)(b + 4);
					if (w <= 0 || h <= 0 || 8 + (long)w * h * 4 > len) return;
					if (atlas == null || atlas.width != w || atlas.height != h)
					{
						atlas = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
						UnityEngine.Object.DontDestroyOnLoad(atlas);
						if (opaque != null)
						{
							opaque.mainTexture = atlas;
							cutout.mainTexture = atlas;
							translucent.mainTexture = atlas;
						}
					}
					// rows arrive top-first; loading them as-is puts row 0 at Unity's v=0, so meshes use v' = 1 - v
					atlas.LoadRawTextureData((IntPtr)(b + 8), w * h * 4);
					atlas.Apply(false);
					EnsureMaterials();
					Plugin.L.LogInfo($"PTRCraft: block atlas {w}x{h}");
					break;
				}
				case Proto.RenClearAll:
					Clear();
					break;
				case Proto.RenSection:
				{
					int sx = *(int*)b, sy = *(int*)(b + 4), sz = *(int*)(b + 8), n = *(int*)(b + 12);
					if (16 + (long)n * 32 > len) return;
					BuildSection(sx, sy, sz, b + 16, n);
					break;
				}
				case Proto.RenSolids:
				{
					int sx = *(int*)b, sy = *(int*)(b + 4), sz = *(int*)(b + 8), n = *(int*)(b + 12);
					BuildSolids(sx, sy, sz, n > 0 && len >= 16 + 512 ? b + 16 : null);
					break;
				}
				case Proto.RenLights:
				{
					int sx = *(int*)b, sy = *(int*)(b + 4), sz = *(int*)(b + 8), n = *(int*)(b + 12);
					if (16 + (long)n * 8 > len) return;
					BuildLights(sx, sy, sz, b + 16, n);
					break;
				}
				case Proto.RenTexture:
					EntityMeshes.OnTexture(b, len);
					break;
				case Proto.RenAvatar:
					EntityMeshes.OnMesh(b, len, false);
					break;
				case Proto.RenScene:
					EntityMeshes.OnMesh(b, len, true);
					break;
				default:
					break; // animated atlas regions, ragdoll parts, dug bits: not drawn yet
			}
		}

		private static Section Get(int sx, int sy, int sz, bool create)
		{
			if (sections.TryGetValue((sx, sy, sz), out var s) || !create) return s;
			EnsureRoot();
			s = new Section();
			s.Go = new GameObject($"Section {sx},{sy},{sz}");
			s.Go.transform.SetParent(root.transform, false);
			s.Go.transform.position = Mapping.ToUnity(sx * 16, sy * 16, sz * 16);
			sections[(sx, sy, sz)] = s;
			return s;
		}

		private static void BuildSection(int sx, int sy, int sz, byte* v, int n)
		{
			EnsureMaterials();
			var s = Get(sx, sy, sz, n > 0);
			if (s == null) return;
			if (n == 0)
			{
				if (s.Mesh != null) s.Mesh.Clear();
				return;
			}
			var pos = new Vector3[n];
			var uv = new Vector2[n];
			var col = new Color32[n];
			var lists = new[] { new List<int>(), new List<int>(), new List<int>() };
			for (int i = 0; i < n; i++)
			{
				byte* e = v + i * 32;
				pos[i] = new Vector3(*(float*)e, *(float*)(e + 4), -*(float*)(e + 8));
				uv[i] = new Vector2(*(float*)(e + 12), 1f - *(float*)(e + 16));
				col[i] = new Color32(e[20], e[21], e[22], e[23]);
			}
			for (int i = 0; i + 2 < n; i += 3)
			{
				uint flags = *(uint*)(v + i * 32 + 28);
				int k = (flags & 2) != 0 ? 2 : (flags & 1) != 0 ? 1 : 0;
				// the Z mirror flips handedness: reverse the winding
				lists[k].Add(i);
				lists[k].Add(i + 2);
				lists[k].Add(i + 1);
			}
			if (s.Mesh == null)
			{
				s.Mesh = new Mesh();
				s.Go.AddComponent<MeshFilter>().sharedMesh = s.Mesh;
				var mr = s.Go.AddComponent<MeshRenderer>();
				mr.sharedMaterials = new Material[] { opaque, cutout, translucent };
				mr.shadowCastingMode = ShadowCastingMode.On;
				mr.receiveShadows = true;
			}
			var m = s.Mesh;
			m.Clear();
			m.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			m.vertices = pos;
			m.uv = uv;
			m.colors32 = col;
			m.subMeshCount = 3;
			for (int k = 0; k < 3; k++) m.SetTriangles(lists[k].ToArray(), k);
			m.RecalculateNormals();
			m.RecalculateBounds();
		}

		/// <summary>Box colliders for Minecraft's solid blocks, so PTR's people and props collide with builds.</summary>
		private static void BuildSolids(int sx, int sy, int sz, byte* bits)
		{
			var s = Get(sx, sy, sz, bits != null);
			if (s == null) return;
			if (s.Solids != null) UnityEngine.Object.Destroy(s.Solids);
			s.Solids = null;
			if (bits == null) return;
			s.Solids = new GameObject("Solids");
			s.Solids.transform.SetParent(s.Go.transform, false);
			s.Solids.layer = 0;
			// merge runs along x (bit x + 16z + 256y)
			for (int y = 0; y < 16; y++)
			for (int z = 0; z < 16; z++)
			{
				int x = 0;
				while (x < 16)
				{
					int i = x + 16 * z + 256 * y;
					if ((bits[i >> 3] & (1 << (i & 7))) == 0)
					{
						x++;
						continue;
					}
					int x0 = x;
					while (x < 16)
					{
						int j = x + 16 * z + 256 * y;
						if ((bits[j >> 3] & (1 << (j & 7))) == 0) break;
						x++;
					}
					var bc = s.Solids.AddComponent<BoxCollider>();
					// MC block (x..x+1, y..y+1, z..z+1) -> local Unity (x, y, -z)
					bc.center = new Vector3((x0 + x) * 0.5f, y + 0.5f, -(z + 0.5f));
					bc.size = new Vector3(x - x0, 1, 1);
				}
			}
		}

		private static void BuildLights(int sx, int sy, int sz, byte* l, int n)
		{
			var s = Get(sx, sy, sz, n > 0);
			if (s == null) return;
			foreach (var go in s.Lights) UnityEngine.Object.Destroy(go);
			s.Lights.Clear();
			for (int i = 0; i < Math.Min(n, 24); i++)
			{
				byte* e = l + i * 8;
				int x = e[0], y = e[1], z = e[2], level = e[3];
				uint c = *(uint*)(e + 4);
				var go = new GameObject("Light");
				go.transform.SetParent(s.Go.transform, false);
				go.transform.localPosition = new Vector3(x + 0.5f, y + 0.5f, -(z + 0.5f));
				var light = go.AddComponent<Light>();
				light.type = LightType.Point;
				light.color = new Color((c & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, ((c >> 16) & 0xFF) / 255f);
				light.range = level * 0.9f;
				light.intensity = 0.25f + level / 15f;
				light.shadows = LightShadows.None;
				s.Lights.Add(go);
			}
		}

		public static void Clear()
		{
			foreach (var s in sections.Values) if (s.Go != null) UnityEngine.Object.Destroy(s.Go);
			sections.Clear();
		}
	}
}
