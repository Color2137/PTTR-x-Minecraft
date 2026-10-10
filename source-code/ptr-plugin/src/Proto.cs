namespace PTRCraft
{
	/// <summary>
	/// The shared-memory layout, mirrored from SkyCraft's protocol (research/skycraft_protocol.h,
	/// v11) and the Java side (minecraft/.../link/Proto.java). Little-endian; MC coordinates.
	/// </summary>
	internal static class Proto
	{
		public const uint Magic = 0x43525450; // "PTRC"
		public const uint Version = 11;
		public const string MappingName = "Local\\PTRCraft_v1";

		public const long OffHeader = 0x0;
		public const long OffSky = 0x100;
		public const long OffMc = 0x200;
		public const long OffOverlayCtl = 0x300;
		public const long OffOverlaySlotHdr = 0x340;
		public const long OffWaterGrid = 0x400;
		public const long OffInputRing = 0x1000;
		public const long OffActorTable = 0x12000;
		public const long OffEventRing = 0x17000;
		public const long OffWorldEntities = 0x1C000;
		public const long OffCollisionRing = 0x20000;
		public const long CollisionRingBytes = 32L << 20;
		public const long OffOverlayPixels = OffCollisionRing + CollisionRingBytes;
		public const int MaxOverlayW = 3840, MaxOverlayH = 2160;
		public const long OverlaySlotBytes = (long)MaxOverlayW * MaxOverlayH * 4;
		public const int OverlaySlots = 3;
		public const long OffRenderRing = OffOverlayPixels + OverlaySlotBytes * OverlaySlots;
		public const long RenderRingBytes = 64L << 20;
		public const long MappingBytes = OffRenderRing + RenderRingBytes;

		// header
		public const long HMagic = 0x00, HVersion = 0x04, HHostPid = 0x08, HMcPid = 0x0C, HHostBeat = 0x10, HMcBeat = 0x18;

		// SkyState (host -> MC)
		public const uint SkyInGame = 1, SkyMenuOpen = 2, SkyLoading = 4;
		// PTR draws Minecraft's blocks and entities itself (MinecraftDrawsWorld off): Minecraft renders no level.
		public const uint SkyHostDrawsWorld = 8;
		// The level PTR is in (each has its own Minecraft world), NUL-padded UTF-8, relative to OffSky.
		public const long SsLevelKey = 0x40, SsLevelTitle = 0x70;
		public const int SsLevelKeyBytes = 48, SsLevelTitleBytes = 64;

		// McState (MC -> host)
		public const long MsSeq = 0x00, MsFlags = 0x04, MsX = 0x08, MsY = 0x10, MsZ = 0x18, MsYaw = 0x20, MsPitch = 0x24,
			MsEyeHeight = 0x28, MsSensitivity = 0x2C, MsTeleportAck = 0x30, MsGuiScale = 0x34, MsFrame = 0x38, MsFov = 0x40,
			MsBobPhase = 0x44, MsBobAmount = 0x48, MsEyeX = 0x50, MsEyeY = 0x58, MsEyeZ = 0x60, MsTickQpc = 0x68,
			MsPrevX = 0x70, MsCurX = 0x88, MsEyeO = 0xA0, MsEye = 0xA4, MsWalkO = 0xA8, MsWalk = 0xAC, MsBobO = 0xB0, MsBob = 0xB4,
			MsTickMs = 0xB8, MsCameraMode = 0xC0, MsCameraDistance = 0xC4;
		public const uint McInWorld = 1, McScreenOpen = 2, McOnGround = 4, McSneaking = 8, McSprinting = 16, McDead = 32, McSwimming = 64, McFlying = 128;

		// overlay
		public const uint OverlayDirty = 1u << 2;

		// input ring
		public const int InputRingEntries = 4096;
		public const ushort InKey = 1, InMouseButton = 2, InScroll = 3, InCursor = 4, InText = 5, InReleaseAll = 6, InHurt = 7, InOpenMenu = 8;
		public const ushort HurtMelee = 0, HurtProjectile = 1, HurtMagic = 2, HurtOther = 3;

		// actors
		public const int MaxActors = 256;
		public const uint ActorHostile = 1, ActorDead = 2, ActorEssential = 4, ActorInCombat = 8;

		// events (MC -> host)
		public const int EventRingEntries = 512;
		public const uint EvHitActor = 1, EvPlayerDied = 2, EvExplosion = 3, EvArrowStuck = 4, EvSkillUse = 5;
		// EvHitActor flags (Proto.HIT_* on the Minecraft side)
		public const uint HitCritical = 1, HitProjectile = 2, HitSweep = 4, HitFire = 8;

		// world entities
		public const int MaxWorldEntities = 160;

		// collision ring
		public const uint ColPad = 0, ColClear = 1, ColRegion = 2, ColTris = 3;

		// render ring
		public const uint RenPad = 0, RenAtlas = 1, RenSection = 2, RenClearAll = 3, RenTexture = 4, RenAvatar = 5, RenScene = 6,
			RenAtlasRegion = 7, RenLights = 8, RenRagdoll = 9, RenSolids = 10, RenDug = 11;
	}
}
