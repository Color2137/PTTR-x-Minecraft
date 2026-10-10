using System;
using System.Collections.Generic;
using UnityEngine;

namespace PTRCraft
{
	/// <summary>
	/// Minecraft things that aren't blocks, drawn in PTR each frame from Minecraft's world-entity table:
	/// arrows and tridents in flight, dropped items and blocks, block-breaking cracks. Geometry follows
	/// SkyCraft's Skyrim renderer (WorldRender.cpp), at Minecraft's own size (PTR is 1 m = 1 block).
	/// </summary>
	internal static unsafe class WorldEntities
	{
		private const int Stride = 96;
		private static GameObject go;
		private static Mesh mesh;
		private static readonly List<Vector3> pos = new();
		private static readonly List<Vector2> uv = new();
		private static readonly List<Color32> col = new();
		private static readonly List<int> solid = new(), cracks = new();
		private static double ox, oy, oz; // Minecraft origin of this frame's mesh
		public static int Count;

		private static void Ensure()
		{
			if (go != null) return;
			go = new GameObject("PTRCraftEntities");
			UnityEngine.Object.DontDestroyOnLoad(go);
			mesh = new Mesh();
			mesh.MarkDynamic();
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>();
		}

		public static void Update(Link link)
		{
			if (!WorldRender.Ready) return;
			long o = Proto.OffWorldEntities;
			byte* b = link.Base;
			uint seq = *(uint*)(b + o);
			if ((seq & 1) != 0) return;
			int count = Math.Min((int)*(uint*)(b + o + 4), Proto.MaxWorldEntities);
			// copy first, then check the seqlock didn't move
			var buf = new byte[count * Stride];
			fixed (byte* d = buf) Buffer.MemoryCopy(b + o + 0x40, d, buf.Length, buf.Length);
			if (*(uint*)(b + o) != seq) return;
			Ensure();
			pos.Clear();
			uv.Clear();
			col.Clear();
			solid.Clear();
			cracks.Clear();
			var cam = Puppet.Cam != null ? Puppet.Cam.transform.position : Vector3.zero;
			Mapping.ToMc(cam, out ox, out oy, out oz);
			ox = Math.Floor(ox);
			oy = Math.Floor(oy);
			oz = Math.Floor(oz);
			int drawn = 0;
			fixed (byte* p0 = buf)
			{
				for (int i = 0; i < count; i++)
				{
					byte* e = p0 + i * Stride;
					uint kind = *(uint*)e;
					float* f = (float*)(e + 8);
					float px = (float)(f[0] - ox), py = (float)(f[1] - oy), pz = (float)(f[2] - oz);
					float yaw = f[3], pitch = f[4], scale = f[5];
					float* ext = f + 6;
					float* uv0 = f + 9, uv1 = f + 13, uv2 = f + 17;
					uint tint = *(uint*)(e + 92);
					switch (kind)
					{
						case 1: // arrow
						case 3: // trident
						{
							float yr = yaw * Mathf.Deg2Rad, pr = pitch * Mathf.Deg2Rad;
							var d = new Vector3(Mathf.Sin(yr) * Mathf.Cos(pr), Mathf.Sin(pr), Mathf.Cos(yr) * Mathf.Cos(pr));
							Arrow(new Vector3(px, py, pz), d, uv0, uv1, kind == 3);
							drawn++;
							break;
						}
						case 2: // dropped item: a flat sprite turning about the vertical
						{
							float spin = yaw * Mathf.Deg2Rad, half = scale * 0.5f;
							float rx = Mathf.Cos(spin) * half, rz = Mathf.Sin(spin) * half;
							Quad(new Vector3(px - rx, py + half, pz - rz), new Vector3(px + rx, py + half, pz + rz),
								new Vector3(px + rx, py - half, pz + rz), new Vector3(px - rx, py - half, pz - rz), uv0, 0xFFFFFFFF, solid);
							drawn++;
							break;
						}
						case 4: // dropped block: a small spinning cube
						{
							float s = scale;
							Box(new Vector3(px - s * 0.5f, py - s * 0.5f, pz - s * 0.5f), new Vector3(s, s, s), yaw * Mathf.Deg2Rad, uv0, uv1, uv2, tint, solid);
							drawn++;
							break;
						}
						case 5: // block cracks
						{
							Box(new Vector3(px, py, pz), new Vector3(ext[0], ext[1], ext[2]), 0, uv0, uv0, uv0, 0, cracks);
							drawn++;
							break;
						}
					}
				}
			}
			Count = drawn;
			mesh.Clear();
			if (drawn == 0) return;
			// Minecraft -> Unity: positions are relative to the origin; the object sits at it.
			go.transform.position = Mapping.ToUnity(ox, oy, oz);
			var verts = new Vector3[pos.Count];
			for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(pos[i].x, pos[i].y, -pos[i].z);
			mesh.vertices = verts;
			mesh.uv = uv.ToArray();
			mesh.colors32 = col.ToArray();
			mesh.subMeshCount = 2;
			mesh.SetTriangles(solid.ToArray(), 0);
			mesh.SetTriangles(cracks.ToArray(), 1);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			go.GetComponent<MeshRenderer>().sharedMaterials = new Material[] { WorldRender.Cutout, WorldRender.Translucent };
		}

		/// <summary>A quad TL, TR, BR, BL (seen from the front) with the atlas rect {u0, v0, u1, v1}.</summary>
		private static void Quad(Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl, float* r, uint rgba, List<int> into)
		{
			int i = pos.Count;
			pos.Add(tl);
			pos.Add(tr);
			pos.Add(br);
			pos.Add(bl);
			uv.Add(new Vector2(r[0], 1f - r[1]));
			uv.Add(new Vector2(r[2], 1f - r[1]));
			uv.Add(new Vector2(r[2], 1f - r[3]));
			uv.Add(new Vector2(r[0], 1f - r[3]));
			var c = new Color32((byte)(rgba & 0xFF), (byte)(rgba >> 8), (byte)(rgba >> 16), (byte)(rgba >> 24));
			for (int k = 0; k < 4; k++) col.Add(c);
			// both sides (sprites and fins are seen from either side); the z mirror flips winding
			into.Add(i); into.Add(i + 2); into.Add(i + 1);
			into.Add(i); into.Add(i + 3); into.Add(i + 2);
			into.Add(i); into.Add(i + 1); into.Add(i + 2);
			into.Add(i); into.Add(i + 2); into.Add(i + 3);
		}

		private static void Box(Vector3 min, Vector3 size, float yaw, float* side, float* top, float* bottom, uint topTint, List<int> into)
		{
			float cx = min.x + size.x * 0.5f, cz = min.z + size.z * 0.5f, c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
			Vector3 Corner(int i)
			{
				float lx = ((i & 1) != 0 ? 0.5f : -0.5f) * size.x, lz = ((i & 4) != 0 ? 0.5f : -0.5f) * size.z;
				return new Vector3(cx + lx * c - lz * s, min.y + ((i & 2) != 0 ? size.y : 0f), cz + lx * s + lz * c);
			}
			int[,] faces = { { 6, 7, 5, 4 }, { 3, 2, 0, 1 }, { 7, 3, 1, 5 }, { 2, 6, 4, 0 }, { 2, 3, 7, 6 }, { 4, 5, 1, 0 } };
			for (int fc = 0; fc < 6; fc++)
			{
				float* r = fc == 4 ? top : fc == 5 ? bottom : side;
				uint tint = fc == 4 && topTint != 0 ? topTint : 0xFFFFFFFF;
				Quad(Corner(faces[fc, 0]), Corner(faces[fc, 1]), Corner(faces[fc, 2]), Corner(faces[fc, 3]), r, tint, into);
			}
		}

		/// <summary>Minecraft's arrow model (two crossed fins and a back plate), or a trident's icon.</summary>
		private static void Arrow(Vector3 p, Vector3 d, float* side, float* back, bool trident)
		{
			var s = new Vector3(d.z, 0, -d.x);
			if (s.sqrMagnitude < 1e-6f) s = new Vector3(1, 0, 0);
			s.Normalize();
			var u = Vector3.Cross(s, d);
			const float r = 0.70710678f;
			var fins = new[] { (u + s) * r, (u - s) * r };
			Vector3 At(float along, Vector3 q, float side_, Vector3 q2, float side2) => p + d * along + q * side_ + q2 * side2;
			if (!trident)
			{
				const float k = 0.9f / 16f;
				foreach (var q in fins)
					Quad(At(-12 * k, q, -2 * k, Vector3.zero, 0), At(4 * k, q, -2 * k, Vector3.zero, 0), At(4 * k, q, 2 * k, Vector3.zero, 0), At(-12 * k, q, 2 * k, Vector3.zero, 0), side, 0xFFFFFFFF, solid);
				Quad(At(-11 * k, fins[0], -2 * k, fins[1], -2 * k), At(-11 * k, fins[0], 2 * k, fins[1], -2 * k), At(-11 * k, fins[0], 2 * k, fins[1], 2 * k), At(-11 * k, fins[0], -2 * k, fins[1], 2 * k), back, 0xFFFFFFFF, solid);
			}
			else
			{
				const float h = 0.9f;
				foreach (var q in fins)
					Quad(At(0, q, h, Vector3.zero, 0), At(h, q, 0, Vector3.zero, 0), At(0, q, -h, Vector3.zero, 0), At(-h, q, 0, Vector3.zero, 0), side, 0xFFFFFFFF, solid);
			}
		}

		public static void Hide()
		{
			if (mesh != null) mesh.Clear();
		}
	}
}
