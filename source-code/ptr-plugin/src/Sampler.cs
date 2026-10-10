using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using NV = System.Numerics.Vector3;

namespace PTRCraft
{
	/// <summary>
	/// Measures colliders whose shape PTR can't hand out (meshes that aren't readable at runtime,
	/// terrain, anything that isn't a box, sphere, capsule or readable mesh) with PTR's own physics:
	/// rays at the collider alone (Collider.Raycast) on a 25 cm grid from above, below and all four
	/// sides, several surfaces deep. Each surface hit becomes a strip of triangles along its grid row,
	/// on the plane PTR's physics reported, so Minecraft stands and bumps where PTR's player would.
	///
	/// Main thread only (Unity physics), a few milliseconds per frame, nearest collider first; results
	/// go to the collision worker (Collision.Work) and are kept per collider until it moves.
	/// </summary>
	internal static class Sampler
	{
		internal sealed class Batch
		{
			public uint Epoch;
			public List<Collision.Tri> Tris;
		}

		private sealed class Job
		{
			public Collider C;
			public IntPtr Key;
			public Vector3 Pos, Scale;
			public Quaternion Rot;
			public NV Min, Max, Size;
			public int Phase = -1, Row, Rows, Cols;
			public float S;
			public readonly List<Collision.Tri> Tris = new();
		}

		private struct Hit
		{
			public NV P, N;
		}

		private const float Spacing = 0.25f;
		private const int MaxCells = 40000, MaxCols = 400, MaxLayers = 8;

		public static readonly ConcurrentQueue<Batch> Results = new();
		public static int Measured, Rays;
		public static bool Idle => current == null && queue.Count == 0;

		private static readonly Queue<Collider> queue = new();
		private static Job current;
		private static uint epoch;
		private static readonly Dictionary<IntPtr, (Vector3 pos, Quaternion rot, Vector3 scale, List<Collision.Tri> tris)> cache = new();
		private static readonly List<Hit>[] columns = new List<Hit>[MaxCols];

		/// <summary>A new level: nothing measured carries over.</summary>
		public static void Reset()
		{
			queue.Clear();
			current = null;
			cache.Clear();
			while (Results.TryDequeue(out _)) { }
		}

		/// <summary>What was measured for this collider, if it hasn't moved since.</summary>
		public static List<Collision.Tri> Cached(Collider c)
		{
			if (!cache.TryGetValue(c.Pointer, out var e)) return null;
			var t = c.transform;
			return t.position == e.pos && t.rotation == e.rot && t.lossyScale == e.scale ? e.tris : null;
		}

		/// <summary>Measures these colliders, the nearest to {@code player} (Unity space) first.</summary>
		public static void Start(List<Collider> colliders, Vector3 player)
		{
			epoch = Collision.Epoch;
			queue.Clear();
			current = null;
			colliders.Sort((a, b) => (a.bounds.ClosestPoint(player) - player).sqrMagnitude.CompareTo((b.bounds.ClosestPoint(player) - player).sqrMagnitude));
			foreach (var c in colliders) queue.Enqueue(c);
		}

		public static void Step(double budgetMs)
		{
			if (Idle) return;
			var sw = Stopwatch.StartNew();
			while (sw.Elapsed.TotalMilliseconds < budgetMs)
			{
				if (current == null)
				{
					if (queue.Count == 0) return;
					var c = queue.Dequeue();
					if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
					var t = c.transform;
					var b = c.bounds;
					current = new Job { C = c, Key = c.Pointer, Pos = t.position, Rot = t.rotation, Scale = t.lossyScale, Min = V(b.min), Max = V(b.max), Size = V(b.size) };
					if (!NextPhase(current)) current = null; // flat in every direction: nothing to stand on
				}
				if (current.C == null)
				{
					current = null; // destroyed meanwhile
					continue;
				}
				Row(current);
				if (++current.Row >= current.Rows && !NextPhase(current))
				{
					Results.Enqueue(new Batch { Epoch = epoch, Tris = current.Tris });
					cache[current.Key] = (current.Pos, current.Rot, current.Scale, current.Tris);
					Measured++;
					current = null;
				}
			}
		}

		// Phases: rays down (floors), up (ceilings), +x, -x, +z, -z (walls).
		private static readonly int[] RayAxes = { 1, 1, 0, 0, 2, 2 };
		private static readonly float[] RayDirs = { -1, 1, 1, -1, 1, -1 };
		private static int RayAxis(int phase) => RayAxes[phase];
		private static float RayDir(int phase) => RayDirs[phase];
		private static int UAxis(int d) => d == 1 ? 0 : d == 0 ? 2 : 0; // along a row
		private static int VAxis(int d) => d == 1 ? 2 : 1; // row to row

		private static bool NextPhase(Job j)
		{
			while (++j.Phase < 6)
			{
				int d = RayAxis(j.Phase), u = UAxis(d), v = VAxis(d);
				float su = A(j.Size, u), sv = A(j.Size, v);
				float s = Math.Max(Spacing, Math.Max((float)Math.Sqrt(su * sv / MaxCells), su / MaxCols));
				j.S = s;
				j.Cols = Math.Max(1, Math.Min(MaxCols, (int)Math.Ceiling(su / s)));
				j.Rows = Math.Max(1, (int)Math.Ceiling(sv / s));
				j.Row = 0;
				if (su > 0.01f && sv > 0.01f) return true;
			}
			return false;
		}

		/// <summary>One grid row of rays for the current phase, merged into strips.</summary>
		private static void Row(Job j)
		{
			int d = RayAxis(j.Phase), u = UAxis(d), v = VAxis(d);
			float dir = RayDir(j.Phase), s = j.S;
			var dirVec = With(NV.Zero, d, dir);
			var dirU = U(dirVec);
			float vc = A(j.Min, v) + s * (j.Row + 0.5f);
			float depth = A(j.Size, d) + 0.2f;
			for (int col = 0; col < j.Cols; col++)
			{
				var list = columns[col] ??= new List<Hit>();
				list.Clear();
				var origin = With(With(With(NV.Zero, u, A(j.Min, u) + s * (col + 0.5f)), v, vc), d, dir < 0 ? A(j.Max, d) + 0.1f : A(j.Min, d) - 0.1f);
				float left = depth;
				for (int layer = 0; layer < MaxLayers && left > 0; layer++)
				{
					Rays++;
					if (!j.C.Raycast(new Ray(U(origin), dirU), out RaycastHit hit, left)) break;
					var n = V(hit.normal);
					// Each surface is taken by the rays along its own main axis, facing them.
					float nd = A(n, d);
					if (Math.Abs(nd) >= Math.Abs(A(n, u)) && Math.Abs(nd) >= Math.Abs(A(n, v)) && nd * dir < 0) list.Add(new Hit { P = V(hit.point), N = n });
					float step = hit.distance + 0.01f;
					origin += dirVec * step;
					left -= step;
				}
			}
			// Merge neighbouring columns on the same plane (layer by layer) into strips.
			for (int layer = 0; layer < MaxLayers; layer++)
			{
				int start = -1;
				Hit first = default;
				for (int col = 0; col <= j.Cols; col++)
				{
					bool has = col < j.Cols && columns[col].Count > layer;
					var h = has ? columns[col][layer] : default;
					bool same = has && start >= 0 && NV.Dot(h.N, first.N) > 0.999f && Math.Abs(NV.Dot(first.N, h.P - first.P)) < 0.02f;
					if (start >= 0 && !same)
					{
						Strip(j, first, start, col - 1, vc, d, u, v);
						start = -1;
					}
					if (has && start < 0)
					{
						start = col;
						first = h;
					}
				}
			}
		}

		// Plain .NET maths in the loop: Unity's own vector operators are calls into the game through the interop.
		private static float A(NV p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;

		private static NV With(NV p, int axis, float value)
		{
			if (axis == 0) p.X = value;
			else if (axis == 1) p.Y = value;
			else p.Z = value;
			return p;
		}

		private static NV V(Vector3 v) => new(v.x, v.y, v.z);

		private static Vector3 U(NV p)
		{
			Vector3 v = default;
			v.x = p.X;
			v.y = p.Y;
			v.z = p.Z;
			return v;
		}

		/// <summary>The plane through {@code h} over columns c0..c1 of this row, as two triangles in Minecraft space.</summary>
		private static void Strip(Job j, Hit h, int c0, int c1, float vc, int d, int u, int v)
		{
			float s = j.S;
			float ua = A(j.Min, u) + s * c0, ub = A(j.Min, u) + s * (c1 + 1);
			float va = vc - s * 0.5f, vb = vc + s * 0.5f;
			Vector3 Corner(float cu, float cv)
			{
				float pd = A(h.P, d) - (A(h.N, u) * (cu - A(h.P, u)) + A(h.N, v) * (cv - A(h.P, v))) / A(h.N, d);
				return U(With(With(With(NV.Zero, u, cu), v, cv), d, pd));
			}
			var a = Collision.ToMc(Corner(ua, va));
			var b = Collision.ToMc(Corner(ub, va));
			var c = Collision.ToMc(Corner(ub, vb));
			var e = Collision.ToMc(Corner(ua, vb));
			Collision.AddTri(j.Tris, a, b, c);
			Collision.AddTri(j.Tris, a, c, e);
		}
	}
}
