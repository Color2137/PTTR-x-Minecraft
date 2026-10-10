using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Il2CppInterop.Runtime;
using NV = System.Numerics.Vector3;
using NM = System.Numerics.Matrix4x4;

namespace PTRCraft
{
	/// <summary>
	/// PTR's physical world (its own colliders) sent to Minecraft as collision: exact triangles for the
	/// player and 1/8-block voxels for everything else, in 8-block regions (SkyCraft's ColTris/ColRegion).
	///
	/// - Boxes and readable meshes are exact; spheres and capsules are tessellated. Colliders whose mesh
	///   PTR can't hand out (unreadable) and any other kind are measured with PTR's own physics: rays
	///   from every side (Collider.Raycast) and the surfaces they hit, a little each frame (Sampler).
	/// - Collision streams outward from the player, nearest region first, so the floor under you is
	///   there in a moment even in Beneath's hub (590k triangles).
	/// - A level's first build waits until its colliders stop changing (Beneath builds its floors after
	///   the scene has loaded); later changes are picked up every 3 s and replace regions in place.
	///
	/// Unity is only touched on the main thread; voxelizing and encoding run on one worker, and the
	/// main thread just copies finished messages into the ring.
	/// </summary>
	internal static class Collision
	{
		private const int Region = 8;
		private const float BoxFillMaxVolume = 4096f; // m^3; bigger boxes are hollow (their faces are triangles anyway)

		internal struct Tri
		{
			public NV A, B, C, Min, Max;
		}

		private struct Box
		{
			public NM McToBox; // MC coords -> box unit cube (|x|,|y|,|z| <= 1)
			public NV Min, Max;
		}

		private const float StreamRadius = 160f; // blocks around the player; farther regions follow as the player gets near

		private static readonly ConcurrentQueue<(uint type, byte[] payload)> ready = new();
		private static (uint type, byte[] payload)? head;
		private static volatile bool working;
		private static volatile bool farOnly; // the worker has sent everything near the player and waits for it to move
		private static volatile int buildId;
		public static string Report = "";
		public static int RegionsSent, RegionsTotal, Refreshes;
		public static uint Epoch;
		public static readonly List<string> Unreadable = new();
		private static HashSet<long> sentRegions = new();
		// What each region held when last sent (this level): a refresh only resends the regions that changed.
		private static ConcurrentDictionary<long, ulong> regionHashes = new();
		private static long fingerprint;

		// The player in Minecraft coordinates, for nearest-first streaming (written on the main thread).
		private static float playerX, playerY, playerZ;

		// Settling: a level's first build waits for its colliders to stop changing.
		private static bool settling;
		private static float settleStart, nextSettleCheck, nextChangeCheck;
		private static long settlePrint = long.MinValue;
		private static int settleStable;

		// ---------------------------------------------------------------- main thread

		/// <summary>A new level (or a new floor): collision starts once PTR has finished building it.</summary>
		public static void Begin(uint epoch)
		{
			Epoch = epoch; // a worker still busy with the last level stops
			settling = true;
			settleStart = nextSettleCheck = UnityEngine.Time.realtimeSinceStartup;
			settleStable = 0;
			settlePrint = long.MinValue;
			sentRegions = new HashSet<long>();
			regionHashes = new ConcurrentDictionary<long, ulong>();
			while (ready.TryDequeue(out _)) { }
			head = null;
			RegionsSent = RegionsTotal = 0;
			Sampler.Reset();
			Report = "waiting for the level to finish building";
		}

		/// <summary>Look for changed geometry soon (PTR just moved the player somewhere new).</summary>
		public static void CheckSoon() => nextChangeCheck = 0;

		/// <summary>Every frame while in a level with Minecraft linked.</summary>
		public static void Tick(Link link, bool checkChanges)
		{
			float now = UnityEngine.Time.realtimeSinceStartup;
			if (Puppet.Player != null)
			{
				Mapping.ToMc(Puppet.Player.transform.position, out double x, out double y, out double z);
				Volatile.Write(ref playerX, (float)x);
				Volatile.Write(ref playerY, (float)y);
				Volatile.Write(ref playerZ, (float)z);
			}
			if (settling)
			{
				if (now >= nextSettleCheck)
				{
					nextSettleCheck = now + 0.5f;
					long f = Fingerprint(out int count);
					settleStable = f == settlePrint && count >= 8 && !Runner.LevelBusy() ? settleStable + 1 : 0;
					settlePrint = f;
					// Stable for a second (Beneath's floors and custom levels are built after the scene has
					// loaded), or 20 s at most.
					if (settleStable >= 2 || now - settleStart > 20f)
					{
						settling = false;
						fingerprint = f;
						Build(Epoch, refresh: false);
						nextChangeCheck = now + 3f;
					}
				}
			}
			else if (checkChanges && now >= nextChangeCheck)
			{
				nextChangeCheck = now + 3f;
				if (Changed()) Build(Epoch, refresh: true);
			}
			Sampler.Step(3.0);
			Pump(link);
		}

		/// <summary>
		/// A cheap fingerprint of PTR's static level geometry (colliders without a rigidbody): new
		/// Beneath floors, opened arena gates. Thrown bottles, chairs and ragdolls don't count.
		/// </summary>
		private static long Fingerprint(out int count)
		{
			long h = 17;
			int n = 0;
			// Not the player (it moves wherever Minecraft goes) nor PTR's people: only the level itself.
			var player = Puppet.Player != null ? Puppet.Player.transform : null;
			var people = new HashSet<IntPtr>();
			foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Enemy>()))
			{
				var e = o.TryCast<Enemy>();
				if (e != null) people.Add(e.transform.Pointer);
			}
			foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<UnityEngine.Collider>()))
			{
				var c = o.Cast<UnityEngine.Collider>();
				if (!c.enabled || c.isTrigger || c.attachedRigidbody != null) continue;
				if (c.TryCast<UnityEngine.CharacterController>() != null) continue;
				var t = c.transform;
				if (player != null && (t == player || t.IsChildOf(player))) continue;
				bool person = false;
				for (var q = t; q != null && !person; q = q.parent) person = people.Contains(q.Pointer);
				if (person) continue;
				var p = t.position;
				h = h * 31 + (long)(p.x * 4) * 7 + (long)(p.y * 4) * 13 + (long)(p.z * 4);
				n++;
			}
			count = n;
			return h ^ ((long)n << 40);
		}

		/// <summary>True when PTR's static geometry changed since the last build.</summary>
		public static bool Changed()
		{
			if ((working && !farOnly) || settling) return false;
			long f = Fingerprint(out _);
			if (f == fingerprint) return false;
			fingerprint = f;
			return true;
		}

		private static NV N(UnityEngine.Vector3 v) => new(v.x, v.y, v.z);

		internal static NV ToMc(UnityEngine.Vector3 u) => new((float)(u.x + Mapping.LevelX), u.y, -u.z);

		private static bool Skip(UnityEngine.Collider c, HashSet<IntPtr> people, UnityEngine.Transform player)
		{
			if (c == null || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) return true;
			if (c.TryCast<UnityEngine.CharacterController>() != null) return true; // someone walking about
			var t = c.transform;
			if (player != null && (t == player || t.IsChildOf(player))) return true;
			for (var p = t; p != null; p = p.parent) if (people.Contains(p.Pointer)) return true; // people's ragdoll limbs
			var rb = c.attachedRigidbody;
			if (rb != null && !rb.isKinematic)
			{
				var e = c.bounds.extents;
				if (e.x * e.y * e.z * 8 < 0.03f) return true; // bottles, glasses, balls: don't trip over them
			}
			if (c.GetComponent<UnityEngine.HingeJoint>() != null) return true; // doors swing; walk through them
			if (c.GetIl2CppType().Name == "WheelCollider") return true;
			return false;
		}

		/// <summary>
		/// Reads every collider of the loaded level and starts streaming it: once per level, and again
		/// (refresh) when the geometry changed. A refresh replaces regions in place and empties the ones
		/// that have nothing left, so Minecraft never loses the ground under the player.
		/// </summary>
		private static void Build(uint epoch, bool refresh)
		{
			if (refresh && working && !farOnly) return;
			var t0 = DateTime.UtcNow;
			if (refresh) Refreshes++;
			var previous = new HashSet<long>(sentRegions);
			while (ready.TryDequeue(out _)) { }
			head = null;
			Unreadable.Clear();
			var tris = new List<Tri>();
			var boxes = new List<Box>();
			var people = new HashSet<IntPtr>();
			foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Enemy>()))
			{
				var e = o.TryCast<Enemy>();
				if (e != null) people.Add(e.transform.Pointer);
			}
			var player = Puppet.Player != null ? Puppet.Player.transform : null;
			var mcFromUnity = NM.CreateScale(1, 1, -1) * NM.CreateTranslation((float)Mapping.LevelX, 0, 0);
			var toSample = new List<UnityEngine.Collider>();
			int nBox = 0, nMesh = 0, nRound = 0, nSkipped = 0, nCached = 0;
			var biggest = new List<(float size, string what)>(); // the three largest, for the log
			foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<UnityEngine.Collider>()))
			{
				var c = o.TryCast<UnityEngine.Collider>();
				if (Skip(c, people, player))
				{
					nSkipped++;
					continue;
				}
				var bsz = c.bounds.size;
				float span = Math.Max(bsz.x, Math.Max(bsz.y, bsz.z));
				if (biggest.Count < 3 || span > biggest[^1].size)
				{
					biggest.Add((span, $"{DevBridge.PathOf(c.transform)} ({c.GetIl2CppType().Name} {bsz.x:F0}x{bsz.y:F0}x{bsz.z:F0})"));
					biggest.Sort((a, b) => b.size.CompareTo(a.size));
					if (biggest.Count > 3) biggest.RemoveAt(3);
				}
				var box = c.TryCast<UnityEngine.BoxCollider>();
				if (box != null)
				{
					AddBox(tris, boxes, Mat(box.transform.localToWorldMatrix) * mcFromUnity, N(box.center), N(box.size));
					nBox++;
					continue;
				}
				var sphere = c.TryCast<UnityEngine.SphereCollider>();
				if (sphere != null)
				{
					AddCapsule(tris, Mat(sphere.transform.localToWorldMatrix) * mcFromUnity, N(sphere.center), sphere.radius, 0f, 1);
					nRound++;
					continue;
				}
				var capsule = c.TryCast<UnityEngine.CapsuleCollider>();
				if (capsule != null)
				{
					AddCapsule(tris, Mat(capsule.transform.localToWorldMatrix) * mcFromUnity, N(capsule.center), capsule.radius, capsule.height, capsule.direction);
					nRound++;
					continue;
				}
				var mc = c.TryCast<UnityEngine.MeshCollider>();
				if (mc != null && mc.sharedMesh != null && mc.sharedMesh.isReadable)
				{
					var mesh = mc.sharedMesh;
					var l2w = Mat(mc.transform.localToWorldMatrix) * mcFromUnity;
					var v = mesh.vertices;
					var idx = mesh.triangles;
					var w = new NV[v.Length];
					for (int i = 0; i < v.Length; i++) w[i] = NV.Transform(N(v[i]), l2w);
					for (int i = 0; i + 2 < idx.Length; i += 3) AddTri(tris, w[idx[i]], w[idx[i + 1]], w[idx[i + 2]]);
					nMesh++;
					continue;
				}
				if (mc != null && mc.sharedMesh == null) continue;
				// Unreadable meshes, terrain and anything else: measured with rays (cached per pose).
				if (mc != null) Unreadable.Add($"{DevBridge.PathOf(c.transform)} mesh={mc.sharedMesh.name} convex={mc.convex} size={c.bounds.size}");
				var cached = Sampler.Cached(c);
				if (cached != null)
				{
					tris.AddRange(cached);
					nCached++;
				}
				else
				{
					toSample.Add(c);
				}
			}
			Report = $"colliders: {nBox} boxes, {nMesh} meshes, {nRound} spheres/capsules ({tris.Count} triangles), {toSample.Count + nCached} measured with rays ({nCached} known), {nSkipped} skipped; read in {(DateTime.UtcNow - t0).TotalMilliseconds:F0} ms";
			Plugin.L.LogInfo("PTRCraft: " + Report);
			Plugin.L.LogInfo("PTRCraft: largest colliders: " + string.Join("; ", biggest.Select(b => b.what)));
			RegionsSent = RegionsTotal = 0;
			Sampler.Start(toSample, new UnityEngine.Vector3(playerX - (float)Mapping.LevelX, playerY, -playerZ));
			working = true;
			farOnly = false;
			int id = ++buildId; // a worker still waiting on far-away regions stops
			Task.Run(() =>
			{
				try
				{
					Work(tris, boxes, epoch, previous, id);
				}
				catch (Exception e)
				{
					Plugin.L.LogError("PTRCraft: collision worker: " + e);
				}
				finally
				{
					if (id == buildId) working = false;
				}
			});
		}

		// Unity matrices are column-major (m[row,col]); System.Numerics uses row vectors (v * M).
		private static NM Mat(UnityEngine.Matrix4x4 m) => new(
			m.m00, m.m10, m.m20, m.m30,
			m.m01, m.m11, m.m21, m.m31,
			m.m02, m.m12, m.m22, m.m32,
			m.m03, m.m13, m.m23, m.m33);

		/// <param name="boxToMc">box local space -> MC</param>
		private static void AddBox(List<Tri> tris, List<Box> boxes, NM boxToMc, NV center, NV size)
		{
			var h = size * 0.5f;
			var cs = new NV[8];
			var min = new NV(float.MaxValue);
			var max = new NV(float.MinValue);
			for (int i = 0; i < 8; i++)
			{
				var lp = center + new NV((i & 1) != 0 ? h.X : -h.X, (i & 2) != 0 ? h.Y : -h.Y, (i & 4) != 0 ? h.Z : -h.Z);
				cs[i] = NV.Transform(lp, boxToMc);
				min = NV.Min(min, cs[i]);
				max = NV.Max(max, cs[i]);
			}
			var unitToMc = NM.CreateScale(Math.Max(h.X, 1e-3f), Math.Max(h.Y, 1e-3f), Math.Max(h.Z, 1e-3f)) * NM.CreateTranslation(center) * boxToMc;
			var sz = max - min;
			if (sz.X * sz.Y * sz.Z <= BoxFillMaxVolume && NM.Invert(unitToMc, out var inv)) boxes.Add(new Box { McToBox = inv, Min = min, Max = max });
			int[] f = { 0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3 };
			for (int i = 0; i < f.Length; i += 3) AddTri(tris, cs[f[i]], cs[f[i + 1]], cs[f[i + 2]]);
		}

		/// <summary>A sphere (height 0) or capsule along local axis {@code dir} (0 x, 1 y, 2 z), tessellated.</summary>
		private static void AddCapsule(List<Tri> tris, NM toMc, NV center, float radius, float height, int dir)
		{
			const int rings = 8, segs = 12;
			float half = Math.Max(0f, height * 0.5f - radius);
			NV Axis(float a, float b, float c) => dir == 0 ? new NV(c, a, b) : dir == 1 ? new NV(a, c, b) : new NV(a, b, c);
			var pts = new NV[(rings + 2) * (segs + 1)];
			int row = 0;
			for (int r = 0; r <= rings + 1; r++)
			{
				// rings / 2 rows for each cap; the two middle rows are the same latitude, offset by the cylinder
				int lat = r <= rings / 2 ? r : r - 1;
				float phi = (float)Math.PI * lat / rings - (float)Math.PI / 2;
				float off = r <= rings / 2 ? -half : half;
				for (int s = 0; s <= segs; s++)
				{
					float th = 2 * (float)Math.PI * s / segs;
					var local = center + Axis(radius * (float)Math.Cos(phi) * (float)Math.Cos(th), radius * (float)Math.Cos(phi) * (float)Math.Sin(th), radius * (float)Math.Sin(phi) + off);
					pts[row * (segs + 1) + s] = NV.Transform(local, toMc);
				}
				row++;
			}
			for (int r = 0; r < rings + 1; r++)
			for (int s = 0; s < segs; s++)
			{
				int a = r * (segs + 1) + s, b = a + 1, c = a + segs + 1, d = c + 1;
				AddTri(tris, pts[a], pts[b], pts[d]);
				AddTri(tris, pts[a], pts[d], pts[c]);
			}
		}

		internal static void AddTri(List<Tri> tris, NV a, NV b, NV c)
		{
			if (NV.Cross(b - a, c - a).LengthSquared() < 1e-10f) return;
			tris.Add(new Tri { A = a, B = b, C = c, Min = NV.Min(a, NV.Min(b, c)), Max = NV.Max(a, NV.Max(b, c)) });
		}

		// ---------------------------------------------------------------- worker

		private static int Fd(float v, int d) => (int)Math.Floor(v / d);

		private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

		private static (int, int, int) Unkey(long k) => ((int)(k << 1 >> 43), (int)(k << 22 >> 43), (int)(k << 43 >> 43));

		/// <summary>
		/// Worker: indexes triangles and boxes by region, then encodes the regions nearest the player
		/// first (one ColTris + ColRegion pair each), taking in what the Sampler measures as it comes.
		/// </summary>
		private static void Work(List<Tri> tris, List<Box> boxes, uint epoch, HashSet<long> previous, int id)
		{
			var t0 = DateTime.UtcNow;
			var regTris = new Dictionary<long, List<int>>();
			var regBoxes = new Dictionary<long, List<int>>();
			var known = new HashSet<long>(); // regions with geometry and their neighbours (empty air near the level is "known")
			var pending = new HashSet<long>();
			var done = new HashSet<long>();
			int dropped = 0;

			void Touch(long rk, int rx, int ry, int rz, bool first)
			{
				if (first)
				{
					for (int dx = -1; dx <= 1; dx++)
					for (int dy = -1; dy <= 1; dy++)
					for (int dz = -1; dz <= 1; dz++)
					{
						long n = Key(rx + dx, ry + dy, rz + dz);
						if (known.Add(n)) pending.Add(n);
					}
				}
				if (done.Remove(rk)) pending.Add(rk); // already sent: send again with the new geometry
			}

			void IndexTri(int i)
			{
				var t = tris[i];
				int rx0 = Fd(t.Min.X, Region), rx1 = Fd(t.Max.X, Region);
				int ry0 = Fd(t.Min.Y, Region), ry1 = Fd(t.Max.Y, Region);
				int rz0 = Fd(t.Min.Z, Region), rz1 = Fd(t.Max.Z, Region);
				if ((long)(rx1 - rx0 + 1) * (ry1 - ry0 + 1) * (rz1 - rz0 + 1) > 200_000)
				{
					dropped++; // kilometres across: not part of anywhere you can walk
					return;
				}
				var n = NV.Cross(t.B - t.A, t.C - t.A);
				float reach = (Math.Abs(n.X) + Math.Abs(n.Y) + Math.Abs(n.Z)) * Region * 0.5f;
				for (int rx = rx0; rx <= rx1; rx++)
				for (int ry = ry0; ry <= ry1; ry++)
				for (int rz = rz0; rz <= rz1; rz++)
				{
					// big triangles: only the regions their plane actually passes through
					var c = new NV((rx + 0.5f) * Region, (ry + 0.5f) * Region, (rz + 0.5f) * Region);
					if (Math.Abs(NV.Dot(n, c - t.A)) > reach) continue;
					long rk = Key(rx, ry, rz);
					bool first = !regTris.TryGetValue(rk, out var l);
					if (first) regTris[rk] = l = new List<int>();
					l.Add(i);
					Touch(rk, rx, ry, rz, first);
				}
			}

			for (int i = 0; i < tris.Count; i++) IndexTri(i);
			for (int i = 0; i < boxes.Count; i++)
			{
				var b = boxes[i];
				for (int rx = Fd(b.Min.X, Region); rx <= Fd(b.Max.X, Region); rx++)
				for (int ry = Fd(b.Min.Y, Region); ry <= Fd(b.Max.Y, Region); ry++)
				for (int rz = Fd(b.Min.Z, Region); rz <= Fd(b.Max.Z, Region); rz++)
				{
					long rk = Key(rx, ry, rz);
					if (!regBoxes.TryGetValue(rk, out var l)) regBoxes[rk] = l = new List<int>();
					l.Add(i);
				}
			}

			// regions that had geometry before and have none now (a Beneath floor that went away)
			int emptied = 0;
			foreach (long rk in previous)
			{
				if (known.Contains(rk)) continue;
				var (rx, ry, rz) = Unkey(rk);
				foreach (uint type in new[] { Proto.ColTris, Proto.ColRegion })
				{
					var m = new MemoryStream();
					var w = new BinaryWriter(m);
					Header(w, rx * Region, ry * Region, rz * Region, epoch, 0);
					ready.Enqueue((type, m.ToArray()));
				}
				emptied++;
			}
			if (emptied > 0) Plugin.L.LogInfo($"PTRCraft: collision: {emptied} regions emptied");
			if (dropped > 0) Plugin.L.LogInfo($"PTRCraft: collision: {dropped} kilometre-wide triangles left out");

			var bits = new ulong[512 * 8]; // one region: 8x8x8 blocks, 8 words of sub-voxel bits each
			var used = new bool[512];
			int blocks = 0, unchanged = 0, encoded = 0;
			var lastHashes = regionHashes;
			var hashes = new ConcurrentDictionary<long, ulong>();
			sentRegions = known;
			regionHashes = hashes;
			bool reported = false;
			while (true)
			{
				if (epoch != Epoch || id != buildId) return; // a newer level or build replaced us
				while (Sampler.Results.TryDequeue(out var batch))
				{
					if (batch.Epoch != epoch) continue;
					int first = tris.Count;
					tris.AddRange(batch.Tris);
					for (int i = first; i < tris.Count; i++) IndexTri(i);
				}
				RegionsTotal = known.Count;
				// The pending regions near where the player is now, nearest first.
				var p = new NV(Volatile.Read(ref playerX), Volatile.Read(ref playerY), Volatile.Read(ref playerZ));
				var near = new List<long>();
				var nearDist = new List<float>();
				foreach (long rk in pending)
				{
					var (rx, ry, rz) = Unkey(rk);
					float d = NV.DistanceSquared(p, new NV((rx + 0.5f) * Region, (ry + 0.5f) * Region, (rz + 0.5f) * Region));
					if (d > StreamRadius * StreamRadius) continue;
					near.Add(rk);
					nearDist.Add(d);
				}
				if (near.Count == 0)
				{
					if (!reported && Sampler.Idle && Sampler.Results.IsEmpty)
					{
						reported = true;
						Plugin.L.LogInfo($"PTRCraft: collision streamed: {tris.Count} triangles, {blocks} blocks in {encoded} regions near the player, {encoded - unchanged} sent ({unchanged} unchanged), {pending.Count} farther than {StreamRadius:F0} blocks for later ({(DateTime.UtcNow - t0).TotalMilliseconds:F0} ms)");
					}
					if (pending.Count == 0 && Sampler.Idle && Sampler.Results.IsEmpty) break;
					farOnly = Sampler.Idle && Sampler.Results.IsEmpty;
					Thread.Sleep(pending.Count > 0 ? 100 : 20);
					continue;
				}
				farOnly = false;
				var order = near.ToArray();
				var dist = nearDist.ToArray();
				Array.Sort(dist, order);
				int take = Math.Min(order.Length, Math.Max(256, order.Length / 16));
				for (int i = 0; i < take; i++)
				{
					if (epoch != Epoch || id != buildId) return;
					long rk = order[i];
					pending.Remove(rk);
					done.Add(rk);
					encoded++;
					blocks += EncodeRegion(rk, tris, boxes, regTris, regBoxes, epoch, bits, used, lastHashes, hashes, ref unchanged);
				}
			}
			farOnly = false;
		}

		/// <summary>Voxelizes one region and queues its two messages. Returns its number of blocks.</summary>
		private static int EncodeRegion(long rk, List<Tri> tris, List<Box> boxes, Dictionary<long, List<int>> regTris, Dictionary<long, List<int>> regBoxes,
			uint epoch, ulong[] bits, bool[] used, ConcurrentDictionary<long, ulong> lastHashes, ConcurrentDictionary<long, ulong> hashes, ref int unchanged)
		{
			var (rx, ry, rz) = Unkey(rk);
			Array.Clear(bits, 0, bits.Length);
			Array.Clear(used, 0, used.Length);
			int ox = rx * Region * 8, oy = ry * Region * 8, oz = rz * Region * 8; // region origin in sub-voxels
			void Set(int sx, int sy, int sz)
			{
				int lx = sx - ox, ly = sy - oy, lz = sz - oz;
				if ((uint)lx >= 64 || (uint)ly >= 64 || (uint)lz >= 64) return;
				int b = (lx >> 3) + 8 * ((lz >> 3) + 8 * (ly >> 3));
				used[b] = true;
				bits[b * 8 + (ly & 7)] |= 1UL << ((lz & 7) * 8 + (lx & 7));
			}
			regTris.TryGetValue(rk, out var tl);
			if (tl != null) foreach (int i in tl) Rasterize(tris[i], ox, oy, oz, Set);
			if (regBoxes.TryGetValue(rk, out var bl))
			{
				foreach (int i in bl)
				{
					var b = boxes[i];
					int sx0 = Math.Max((int)Math.Floor(b.Min.X * 8), ox), sx1 = Math.Min((int)Math.Ceiling(b.Max.X * 8), ox + 64);
					int sy0 = Math.Max((int)Math.Floor(b.Min.Y * 8), oy), sy1 = Math.Min((int)Math.Ceiling(b.Max.Y * 8), oy + 64);
					int sz0 = Math.Max((int)Math.Floor(b.Min.Z * 8), oz), sz1 = Math.Min((int)Math.Ceiling(b.Max.Z * 8), oz + 64);
					for (int sx = sx0; sx < sx1; sx++)
					for (int sy = sy0; sy < sy1; sy++)
					for (int sz = sz0; sz < sz1; sz++)
					{
						var q = NV.Transform(new NV((sx + 0.5f) / 8f, (sy + 0.5f) / 8f, (sz + 0.5f) / 8f), b.McToBox);
						if (Math.Abs(q.X) <= 1 && Math.Abs(q.Y) <= 1 && Math.Abs(q.Z) <= 1) Set(sx, sy, sz);
					}
				}
			}
			int count = 0;
			for (int b = 0; b < 512; b++) if (used[b]) count++;
			// Same as what Minecraft already has for this region: nothing to send.
			ulong hash = 14695981039346656037UL;
			if (tl != null)
			{
				foreach (int i in tl)
				{
					var t = tris[i];
					Mix(ref hash, t.A); Mix(ref hash, t.B); Mix(ref hash, t.C);
				}
			}
			for (int b = 0; b < 512; b++)
			{
				if (!used[b]) continue;
				hash = (hash ^ (ulong)b) * 1099511628211UL;
				for (int k = 0; k < 8; k++) hash = (hash ^ bits[b * 8 + k]) * 1099511628211UL;
			}
			hashes[rk] = hash;
			if (lastHashes.TryGetValue(rk, out var before) && before == hash)
			{
				unchanged++;
				return count;
			}
			int bx = rx * Region, by = ry * Region, bz = rz * Region;
			var tm = new MemoryStream();
			var tw = new BinaryWriter(tm);
			Header(tw, bx, by, bz, epoch, tl?.Count ?? 0);
			if (tl != null)
			{
				foreach (int i in tl)
				{
					var t = tris[i];
					tw.Write(t.A.X); tw.Write(t.A.Y); tw.Write(t.A.Z);
					tw.Write(t.B.X); tw.Write(t.B.Y); tw.Write(t.B.Z);
					tw.Write(t.C.X); tw.Write(t.C.Y); tw.Write(t.C.Z);
					tw.Write(0u);
				}
			}
			var vm = new MemoryStream();
			var vw = new BinaryWriter(vm);
			Header(vw, bx, by, bz, epoch, count);
			for (int b = 0; b < 512; b++)
			{
				if (!used[b]) continue;
				int lx = b & 7, lz = (b >> 3) & 7, ly = b >> 6;
				vw.Write(bx + lx); vw.Write(by + ly); vw.Write(bz + lz); vw.Write(0u);
				for (int k = 0; k < 8; k++) vw.Write(bits[b * 8 + k]);
			}
			ready.Enqueue((Proto.ColTris, tm.ToArray()));
			ready.Enqueue((Proto.ColRegion, vm.ToArray()));
			return count;
		}

		private static void Mix(ref ulong h, NV v)
		{
			h = (h ^ (uint)BitConverter.SingleToInt32Bits(v.X)) * 1099511628211UL;
			h = (h ^ (uint)BitConverter.SingleToInt32Bits(v.Y)) * 1099511628211UL;
			h = (h ^ (uint)BitConverter.SingleToInt32Bits(v.Z)) * 1099511628211UL;
		}

		private static float Ax(NV p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;

		/// <summary>
		/// The sub-voxels (1/8 block) a triangle passes through within one region: walks the cells of
		/// its dominant projection and fills the span of the plane over each, so the work is the
		/// triangle's area, not its bounding box.
		/// </summary>
		private static void Rasterize(Tri t, int ox, int oy, int oz, Action<int, int, int> set)
		{
			NV a = t.A * 8, b = t.B * 8, c = t.C * 8;
			var n = NV.Cross(b - a, c - a);
			float nx = Math.Abs(n.X), ny = Math.Abs(n.Y), nz = Math.Abs(n.Z);
			int d = nx >= ny && nx >= nz ? 0 : ny >= nz ? 1 : 2;
			int u = (d + 1) % 3, v = (d + 2) % 3;
			int[] o = { ox, oy, oz };
			float au = Ax(a, u), av = Ax(a, v), bu = Ax(b, u), bv = Ax(b, v), cu = Ax(c, u), cv = Ax(c, v);
			float nd = Ax(n, d), nu = Ax(n, u), nv = Ax(n, v), ad = Ax(a, d);
			float dMin = Math.Min(ad, Math.Min(Ax(b, d), Ax(c, d))), dMax = Math.Max(ad, Math.Max(Ax(b, d), Ax(c, d)));
			int u0 = Math.Max((int)Math.Floor(Math.Min(au, Math.Min(bu, cu))), o[u]), u1 = Math.Min((int)Math.Floor(Math.Max(au, Math.Max(bu, cu))), o[u] + 63);
			int v0 = Math.Max((int)Math.Floor(Math.Min(av, Math.Min(bv, cv))), o[v]), v1 = Math.Min((int)Math.Floor(Math.Max(av, Math.Max(bv, cv))), o[v] + 63);
			int dLo = Math.Max((int)Math.Floor(dMin), o[d]), dHi = Math.Min((int)Math.Floor(dMax), o[d] + 63);
			if (u0 > u1 || v0 > v1 || dLo > dHi || Math.Abs(nd) < 1e-12f) return;
			// edge functions of the projected triangle, positive inside
			float s = (bu - au) * (cv - av) - (bv - av) * (cu - au) >= 0 ? 1 : -1;
			float E(float pu, float pv, float qu, float qv, float x, float y) => s * ((qu - pu) * (y - pv) - (qv - pv) * (x - pu));
			float r0 = 0.5f * (Math.Abs(bu - au) + Math.Abs(bv - av)), r1 = 0.5f * (Math.Abs(cu - bu) + Math.Abs(cv - bv)), r2 = 0.5f * (Math.Abs(au - cu) + Math.Abs(av - cv));
			var cell = new int[3];
			for (int iu = u0; iu <= u1; iu++)
			for (int iv = v0; iv <= v1; iv++)
			{
				float x = iu + 0.5f, y = iv + 0.5f;
				if (E(au, av, bu, bv, x, y) + r0 < 0 || E(bu, bv, cu, cv, x, y) + r1 < 0 || E(cu, cv, au, av, x, y) + r2 < 0) continue;
				// the plane's span along d over this cell (its four corners), within the triangle's own span
				float lo = float.MaxValue, hi = float.MinValue;
				for (int k = 0; k < 4; k++)
				{
					float pu = iu + (k & 1), pv = iv + (k >> 1);
					float pd = ad - (nu * (pu - au) + nv * (pv - av)) / nd;
					lo = Math.Min(lo, pd);
					hi = Math.Max(hi, pd);
				}
				int c0 = Math.Max((int)Math.Floor(Math.Max(lo, dMin)), dLo), c1 = Math.Min((int)Math.Floor(Math.Min(hi, dMax)), dHi);
				cell[u] = iu;
				cell[v] = iv;
				for (int id = c0; id <= c1; id++)
				{
					cell[d] = id;
					set(cell[0], cell[1], cell[2]);
				}
			}
		}

		private static void Header(BinaryWriter w, int bx, int by, int bz, uint epoch, int count)
		{
			w.Write(bx); w.Write(by); w.Write(bz);
			w.Write(bx + Region - 1); w.Write(by + Region - 1); w.Write(bz + Region - 1);
			w.Write(epoch); w.Write((uint)count);
		}

		/// <summary>Copies finished messages into the ring while it has room.</summary>
		public static void Pump(Link link)
		{
			for (int n = 0; n < 4096; n++)
			{
				if (head == null)
				{
					if (!ready.TryDequeue(out var m)) return;
					head = m;
				}
				if (!link.WriteCollision(head.Value.type, head.Value.payload)) return;
				if (head.Value.type == Proto.ColRegion) RegionsSent++;
				head = null;
			}
		}

		public static bool Done => !working && !settling && head == null && ready.IsEmpty && Sampler.Idle;

		public static void SendClear(Link link, uint epoch)
		{
			link.WriteCollision(Proto.ColClear, BitConverter.GetBytes(epoch));
		}
	}
}
