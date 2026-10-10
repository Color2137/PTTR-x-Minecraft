using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PTRCraft
{
	/// <summary>
	/// The PTR end of the shared memory. PTR creates the mapping (like Skyrim does in SkyCraft); the
	/// Minecraft mod opens it, and each side keeps a heartbeat.
	/// </summary>
	internal unsafe sealed class Link : IDisposable
	{
		public struct McState
		{
			public uint Flags;
			public double X, Y, Z;
			public float Yaw, Pitch, EyeHeight, Sensitivity;
			public uint TeleportAck, GuiScale;
			public ulong Frame;
			public float Fov, BobPhase, BobAmount;
			public double EyeX, EyeY, EyeZ;
			public long TickQpc;
			public double PrevX, PrevY, PrevZ, CurX, CurY, CurZ;
			public float EyeO, Eye, WalkO, Walk, BobO, Bob, TickMs;
			public uint CameraMode;
			public float CameraDistance;
		}

		private readonly MemoryMappedFile mmf;
		private readonly MemoryMappedViewAccessor view;
		private readonly byte* b;
		private uint skySeq;
		private int overlayFront = 2;
		private ulong colHead, inputHead;

		public Link()
		{
			mmf = MemoryMappedFile.CreateOrOpen(Proto.MappingName, Proto.MappingBytes, MemoryMappedFileAccess.ReadWrite);
			view = mmf.CreateViewAccessor(0, Proto.MappingBytes, MemoryMappedFileAccess.ReadWrite);
			byte* p = null;
			view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
			b = p;
			// Fresh state for this PTR run (Minecraft restarts its side when our pid changes).
			Zero(0, 0x100);
			Zero(Proto.OffSky, 0x100);
			Zero(Proto.OffMc, 0x100);
			Zero(Proto.OffOverlayCtl, 0x100);
			Zero(Proto.OffWaterGrid, 0xC00);
			Zero(Proto.OffInputRing, 0x80);
			Zero(Proto.OffActorTable, 0x40);
			Zero(Proto.OffEventRing, 0x80);
			Zero(Proto.OffWorldEntities, 0x40);
			Zero(Proto.OffCollisionRing, 0x80);
			Zero(Proto.OffRenderRing, 0x80);
			// no water anywhere
			for (int i = 0; i < 256; i++) F32(Proto.OffWaterGrid + 0x10 + 4 * i) = -1.0e30f;
			U32(Proto.HMagic) = Proto.Magic;
			U32(Proto.HVersion) = Proto.Version;
			U32(Proto.HHostPid) = (uint)Environment.ProcessId;
			// Triple buffer: we (reader) hold slot 2, the middle starts as slot 0, Minecraft writes slot 1.
		}

		public byte* Base => b;

		private void Zero(long off, long n)
		{
			new Span<byte>(b + off, (int)n).Clear();
		}

		public ref uint U32(long off) => ref *(uint*)(b + off);
		public ref int I32(long off) => ref *(int*)(b + off);
		public ref ulong U64(long off) => ref *(ulong*)(b + off);
		public ref float F32(long off) => ref *(float*)(b + off);
		public ref double F64(long off) => ref *(double*)(b + off);

		// ---- heartbeat -------------------------------------------------------------------------

		public void Heartbeat()
		{
			Volatile.Write(ref U64(Proto.HHostBeat), Native.GetTickCount64());
		}

		public bool McAlive()
		{
			ulong beat = Volatile.Read(ref U64(Proto.HMcBeat));
			return beat != 0 && Native.GetTickCount64() - beat < 4000;
		}

		public uint McPid => Volatile.Read(ref U32(Proto.HMcPid));

		// ---- host state (seqlock) ---------------------------------------------------------------

		private byte[] levelKey = new byte[0], levelTitle = new byte[0];

		/// <summary>The level PTR is in (its key names the Minecraft world), sent with every WriteSky.</summary>
		public void SetLevel(string key, string title)
		{
			levelKey = Utf8(key, Proto.SsLevelKeyBytes - 1);
			levelTitle = Utf8(title, Proto.SsLevelTitleBytes - 1);
		}

		private static byte[] Utf8(string s, int max)
		{
			var bytes = Encoding.UTF8.GetBytes(s ?? "");
			if (bytes.Length <= max) return bytes;
			int n = max;
			while (n > 0 && (bytes[n] & 0xC0) == 0x80) n--; // don't cut a character in half
			return bytes[..n];
		}

		public void WriteSky(uint flags, uint worldId, uint epoch, double x, double y, double z, float yaw, float pitch, uint teleportSeq, uint vw, uint vh, float hour)
		{
			skySeq++;
			long o = Proto.OffSky;
			Volatile.Write(ref U32(o), skySeq * 2 - 1);
			Thread.MemoryBarrier();
			U32(o + 0x04) = flags;
			U32(o + 0x08) = worldId;
			U32(o + 0x0C) = epoch;
			F64(o + 0x10) = x;
			F64(o + 0x18) = y;
			F64(o + 0x20) = z;
			F32(o + 0x28) = yaw;
			F32(o + 0x2C) = pitch;
			U32(o + 0x30) = teleportSeq;
			U32(o + 0x34) = vw;
			U32(o + 0x38) = vh;
			F32(o + 0x3C) = hour;
			for (int i = 0; i < Proto.SsLevelKeyBytes; i++) b[o + Proto.SsLevelKey + i] = i < levelKey.Length ? levelKey[i] : (byte)0;
			for (int i = 0; i < Proto.SsLevelTitleBytes; i++) b[o + Proto.SsLevelTitle + i] = i < levelTitle.Length ? levelTitle[i] : (byte)0;
			Thread.MemoryBarrier();
			Volatile.Write(ref U32(o), skySeq * 2);
		}

		public bool ReadMc(ref McState s)
		{
			long o = Proto.OffMc;
			for (int attempt = 0; attempt < 8; attempt++)
			{
				uint s1 = Volatile.Read(ref U32(o));
				if ((s1 & 1) != 0) continue;
				Thread.MemoryBarrier();
				var t = new McState
				{
					Flags = U32(o + Proto.MsFlags), X = F64(o + Proto.MsX), Y = F64(o + Proto.MsY), Z = F64(o + Proto.MsZ),
					Yaw = F32(o + Proto.MsYaw), Pitch = F32(o + Proto.MsPitch), EyeHeight = F32(o + Proto.MsEyeHeight),
					Sensitivity = F32(o + Proto.MsSensitivity), TeleportAck = U32(o + Proto.MsTeleportAck), GuiScale = U32(o + Proto.MsGuiScale),
					Frame = U64(o + Proto.MsFrame), Fov = F32(o + Proto.MsFov), BobPhase = F32(o + Proto.MsBobPhase), BobAmount = F32(o + Proto.MsBobAmount),
					EyeX = F64(o + Proto.MsEyeX), EyeY = F64(o + Proto.MsEyeY), EyeZ = F64(o + Proto.MsEyeZ), TickQpc = *(long*)(b + o + Proto.MsTickQpc),
					PrevX = F64(o + Proto.MsPrevX), PrevY = F64(o + Proto.MsPrevX + 8), PrevZ = F64(o + Proto.MsPrevX + 16),
					CurX = F64(o + Proto.MsCurX), CurY = F64(o + Proto.MsCurX + 8), CurZ = F64(o + Proto.MsCurX + 16),
					EyeO = F32(o + Proto.MsEyeO), Eye = F32(o + Proto.MsEye), WalkO = F32(o + Proto.MsWalkO), Walk = F32(o + Proto.MsWalk),
					BobO = F32(o + Proto.MsBobO), Bob = F32(o + Proto.MsBob), TickMs = F32(o + Proto.MsTickMs),
					CameraMode = U32(o + Proto.MsCameraMode), CameraDistance = F32(o + Proto.MsCameraDistance),
				};
				Thread.MemoryBarrier();
				if (Volatile.Read(ref U32(o)) != s1) continue;
				s = t;
				return true;
			}
			return false;
		}

		// ---- input ring (host -> MC) ------------------------------------------------------------

		public void PushInput(ushort type, ushort code, int a = 0, int bb = 0, int c = 0)
		{
			long ring = Proto.OffInputRing;
			ulong head = inputHead;
			ulong tail = Volatile.Read(ref U64(ring + 0x40));
			if (head - tail >= Proto.InputRingEntries) return; // Minecraft isn't reading: drop
			long e = ring + 0x80 + (long)(head % Proto.InputRingEntries) * 16;
			*(ushort*)(b + e) = type;
			*(ushort*)(b + e + 2) = code;
			I32(e + 4) = a;
			I32(e + 8) = bb;
			I32(e + 12) = c;
			inputHead = head + 1;
			Volatile.Write(ref U64(ring), inputHead);
		}

		// ---- collision ring (host -> MC) --------------------------------------------------------

		private const long ColData = Proto.CollisionRingBytes - 0x80;

		/// <summary>Space left in the collision ring (Minecraft consumes it every frame).</summary>
		public long CollisionFree => ColData - (long)(colHead - Volatile.Read(ref U64(Proto.OffCollisionRing + 0x40)));

		public bool WriteCollision(uint type, ReadOnlySpan<byte> payload)
		{
			long msg = (8 + payload.Length + 7) & ~7L;
			if (msg + 64 > CollisionFree) return false;
			long pos = (long)(colHead % (ulong)ColData);
			if (pos + msg > ColData)
			{
				long pb = Proto.OffCollisionRing + 0x80 + pos;
				U32(pb) = Proto.ColPad;
				U32(pb + 4) = 0;
				colHead += (ulong)(ColData - pos);
				pos = 0;
				if (msg + 64 > CollisionFree) return false;
			}
			long bpos = Proto.OffCollisionRing + 0x80 + pos;
			U32(bpos) = type;
			U32(bpos + 4) = (uint)payload.Length;
			payload.CopyTo(new Span<byte>(b + bpos + 8, payload.Length));
			colHead += (ulong)msg;
			Volatile.Write(ref U64(Proto.OffCollisionRing), colHead);
			return true;
		}

		// ---- overlay triple buffer (MC -> host) ------------------------------------------------

		/// <summary>Takes the newest overlay frame if there is one. Returns the slot index, or -1.</summary>
		public int AcquireOverlay(out int w, out int h, out bool bottomUp)
		{
			w = h = 0;
			bottomUp = false;
			long st = Proto.OffOverlayCtl;
			uint state = Volatile.Read(ref U32(st));
			if ((state & Proto.OverlayDirty) == 0) return -1;
			uint prev = (uint)Interlocked.Exchange(ref I32(st), overlayFront);
			overlayFront = (int)(prev & 3);
			long hdr = Proto.OffOverlaySlotHdr + overlayFront * 0x40;
			w = (int)U32(hdr);
			h = (int)U32(hdr + 4);
			bottomUp = (U32(hdr + 8) & 1) != 0;
			return overlayFront;
		}

		public IntPtr OverlayPixels(int slot) => (IntPtr)(b + Proto.OffOverlayPixels + slot * Proto.OverlaySlotBytes);

		// ---- actor table (host -> MC, seqlock) --------------------------------------------------

		private uint actorSeq;

		public void BeginActors()
		{
			actorSeq++;
			Volatile.Write(ref U32(Proto.OffActorTable), actorSeq * 2 - 1);
			Thread.MemoryBarrier();
		}

		public void WriteActor(int i, uint formId, uint flags, float x, float y, float z, float yaw, float width, float height, float healthFrac, ushort level, string name)
		{
			long r = Proto.OffActorTable + 0x40 + i * 64L;
			U32(r) = formId;
			U32(r + 4) = flags;
			F32(r + 8) = x;
			F32(r + 12) = y;
			F32(r + 16) = z;
			F32(r + 20) = yaw;
			F32(r + 24) = width;
			F32(r + 28) = height;
			F32(r + 32) = healthFrac;
			*(ushort*)(b + r + 36) = level;
			*(ushort*)(b + r + 38) = 0;
			var bytes = Encoding.UTF8.GetBytes(name ?? "");
			int n = Math.Min(bytes.Length, 23);
			var dst = new Span<byte>(b + r + 40, 24);
			dst.Clear();
			bytes.AsSpan(0, n).CopyTo(dst);
		}

		public void EndActors(int count)
		{
			U32(Proto.OffActorTable + 4) = (uint)count;
			Thread.MemoryBarrier();
			Volatile.Write(ref U32(Proto.OffActorTable), actorSeq * 2);
		}

		// ---- event ring (MC -> host) ------------------------------------------------------------

		public struct McEvent
		{
			public uint Type, FormId, Flags, Weapon;
			public float A, B, C, D;
		}

		public int DrainEvents(Span<McEvent> into)
		{
			long ring = Proto.OffEventRing;
			ulong head = Volatile.Read(ref U64(ring));
			ulong tail = U64(ring + 0x40);
			int n = 0;
			while (tail < head && n < into.Length)
			{
				long e = ring + 0x80 + (long)(tail % Proto.EventRingEntries) * 32;
				into[n++] = new McEvent { Type = U32(e), FormId = U32(e + 4), A = F32(e + 8), B = F32(e + 12), C = F32(e + 16), D = F32(e + 20), Flags = U32(e + 24), Weapon = U32(e + 28) };
				tail++;
			}
			Volatile.Write(ref U64(ring + 0x40), tail);
			return n;
		}

		// ---- render ring (MC -> host) -----------------------------------------------------------

		private const long RenData = Proto.RenderRingBytes - 0x80;

		/// <summary>Calls handler(type, payload pointer, payload bytes) for every queued message.</summary>
		public int DrainRender(Action<uint, IntPtr, int> handler, int maxMessages)
		{
			long ring = Proto.OffRenderRing;
			ulong head = Volatile.Read(ref U64(ring));
			ulong tail = U64(ring + 0x40);
			int n = 0;
			while (tail < head && n < maxMessages)
			{
				long pos = (long)(tail % (ulong)RenData);
				long m = ring + 0x80 + pos;
				uint type = U32(m), len = U32(m + 4);
				if (type == Proto.RenPad)
				{
					tail += (ulong)(RenData - pos);
					continue;
				}
				handler(type, (IntPtr)(b + m + 8), (int)len);
				tail += (ulong)((8 + len + 7) & ~7L);
				n++;
			}
			Volatile.Write(ref U64(ring + 0x40), tail);
			return n;
		}

		public void Dispose()
		{
			view.SafeMemoryMappedViewHandle.ReleasePointer();
			view.Dispose();
			mmf.Dispose();
		}
	}
}
