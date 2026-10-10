using System;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PTRCraft
{
	/// <summary>The plugin's per-frame driver, living on BepInEx's manager object.</summary>
	public class Runner : MonoBehaviour
	{
		public Runner(IntPtr ptr) : base(ptr)
		{
		}

		private static Link link;
		private static Link.McState mc;
		private static bool haveMc;
		private static string level = "", begunLevel = "";
		private static float nextKeyCheck;
		private static uint epoch, teleportSeq = 1;
		private static double spawnX, spawnY, spawnZ;
		private static bool waitingTeleport; // Minecraft has to arrive before the takeover
		// Before the takeover PTR's player has to come to rest (Beneath drops you in from high up while it builds
		// the floor, elevators ride you down): Minecraft is sent there then, and again if PTR moves it after.
		private static UnityEngine.Vector3 lastFeet;
		private static float stillSince, levelBegan;
		private static bool spawnSent;
		private static double sentX, sentY, sentZ;
		private static bool ptrTeleportPending; // PTR moved its player; Minecraft is on its way
		private static string status = "starting";
		private static float lastSkyWrite;
		internal static Link LinkInstance => link;
		/// <summary>A Minecraft screen (inventory, chat, crafting...) is open: the cursor is Minecraft's.</summary>
		internal static bool McScreenOpen;
		/// <summary>PTR's own menus or win/lose screens own the input.</summary>
		internal static bool PtrMenu;

		private void Start()
		{
			try
			{
				link = new Link();
				Plugin.L.LogInfo("PTRCraft: shared memory " + Proto.MappingName + " ready");
			}
			catch (Exception e)
			{
				status = "shared memory failed: " + e.Message;
				Plugin.L.LogError("PTRCraft: " + status);
			}
		}

		private void Update()
		{
			DevBridge.Pump();
			if (link == null || !Settings.Enabled.Value) return;
			try
			{
				Tick();
			}
			catch (Exception e)
			{
				Plugin.L.LogError("PTRCraft: " + e);
			}
		}

		private static void HideAll()
		{
			Overlay.Show(false);
			PtrHud.Restore();
			Selection.Hide();
			WorldEntities.Hide();
			EntityMeshes.Hide();
		}

		private static void Tick()
		{
			link.Heartbeat();
			Launcher.EnsureRunning(link);
			haveMc = link.McAlive() && link.ReadMc(ref mc);

			// Level changes: a new stretch of the Minecraft world, new collision, a fresh teleport.
			string scene = SceneManager.GetActiveScene().name;
			if (scene != level)
			{
				level = scene;
				Puppet.Release();
				Puppet.Player = null;
				HideAll();
				WorldRender.Clear();
				Combat.Reset();
				begunLevel = "";
			}
			bool inLevel = Puppet.Find();
			if (inLevel && begunLevel != scene)
			{
				begunLevel = scene;
				nextKeyCheck = Time.realtimeSinceStartup + 1f;
				BeginLevel(scene);
			}
			else if (inLevel && Time.realtimeSinceStartup >= nextKeyCheck)
			{
				// The same scene can become another level (the arena's next challenge): its own world.
				nextKeyCheck = Time.realtimeSinceStartup + 1f;
				if (Mapping.KeyFor(scene, out _) != Mapping.Key)
				{
					Puppet.Release();
					HideAll();
					WorldRender.Clear();
					Combat.Reset();
					BeginLevel(scene);
				}
			}
			bool playerDead = inLevel && Puppet.Player.IsDead();
			PtrMenu = GameManager.Paused || GameManager.InMenu || playerDead;
			bool unfocused = !Application.isFocused || !Native.PtrHasFocus();
			bool linked = haveMc && (mc.Flags & Proto.McInWorld) != 0;
			McScreenOpen = linked && (mc.Flags & Proto.McScreenOpen) != 0;

			if (inLevel && linked && link.McAlive())
			{
				// PTR builds and removes level geometry as you play (Beneath floors, arena pieces): kept up with
				// while Minecraft plays (Collision checks every 3 s).
				Collision.Tick(link, Puppet.Active);
				WorldRender.Pump(link);
				if (Puppet.Active)
				{
					Combat.Publish(link);
					Combat.HandleEvents(link);
					if (Settings.HidePtrHud.Value) PtrHud.Hide();
				}
				if (waitingTeleport && spawnSent && Settled() && mc.TeleportAck == teleportSeq)
				{
					waitingTeleport = false;
					Puppet.TakeOver();
				}
				if (ptrTeleportPending && mc.TeleportAck == teleportSeq)
				{
					ptrTeleportPending = false;
					Puppet.TeleportWanted = false;
				}
				if (Puppet.Active && !PtrMenu && !unfocused)
				{
					if (!McScreenOpen && Input.GetKeyDown(KeyCode.G)) Interact.Use();
					InputCapture.Frame(link, mc.Sensitivity, McScreenOpen, Overlay.Width > 0 ? Overlay.Width : Screen.width, Overlay.Height > 0 ? Overlay.Height : Screen.height);
					ApplyCursor();
				}
				else
				{
					InputCapture.ReleaseAll(link);
				}
				status = Puppet.Active ? "playing as Minecraft" : "waiting for Minecraft to arrive";
			}
			else
			{
				// Minecraft gone, or out of its world (switching to another level's): PTR's player waits for it.
				if (Puppet.Active && (!link.McAlive() || (haveMc && (mc.Flags & Proto.McInWorld) == 0)))
				{
					Puppet.Release();
					HideAll();
					waitingTeleport = true; // when Minecraft is back, start over from PTR's player
					spawnSent = false;
				}
				status = !inLevel ? "not in a level" : !haveMc ? "waiting for Minecraft (" + Launcher.Status + ")" : "Minecraft is loading its world";
			}

			uint flags = 0;
			if (inLevel) flags |= Proto.SkyInGame;
			if (PtrMenu || unfocused) flags |= Proto.SkyMenuOpen;
			if (!Settings.MinecraftDrawsWorld.Value) flags |= Proto.SkyHostDrawsWorld;
			if (inLevel && !Puppet.Active)
			{
				// Until Minecraft has taken over, it waits at PTR's player, once PTR's player is at rest.
				var feet = Puppet.Player.GetFeetPosition();
				if ((feet - lastFeet).sqrMagnitude > 0.05f * 0.05f || LevelBusy()) stillSince = Time.realtimeSinceStartup;
				lastFeet = feet;
				Puppet.FeetMc(out double fx, out double fy, out double fz, out float yaw);
				InputCapture.Yaw = yaw;
				if (!spawnSent)
				{
					spawnX = fx;
					spawnY = fy;
					spawnZ = fz;
				}
				double moved = (fx - sentX) * (fx - sentX) + (fy - sentY) * (fy - sentY) + (fz - sentZ) * (fz - sentZ);
				if (waitingTeleport && Settled() && (!spawnSent || moved > 0.3 * 0.3))
				{
					spawnX = sentX = fx;
					spawnY = sentY = fy;
					spawnZ = sentZ = fz;
					spawnSent = true;
					teleportSeq++;
					Plugin.L.LogInfo($"PTRCraft: PTR's player is at rest at ({feet.x:F1}, {feet.y:F1}, {feet.z:F1}); Minecraft goes there");
				}
			}
			// Minecraft renders once per write (SkyClient.paceFrame): cap it, PTR's own camera never waits.
			float now = Time.realtimeSinceStartup;
			if (now - lastSkyWrite < 1f / Math.Max(10, Settings.MinecraftMaxFps.Value) - 0.0005f) return;
			lastSkyWrite = now;
			// Minecraft draws its hand and HUD at most OverlayMaxHeight tall (a 4K screen gets half size).
			int div = Math.Max(1, (int)Math.Ceiling(Screen.height / (float)Math.Max(360, Settings.OverlayMaxHeight.Value)));
			uint vw = (uint)Math.Min(Screen.width / div, Proto.MaxOverlayW), vh = (uint)Math.Min(Screen.height / div, Proto.MaxOverlayH);
			link.WriteSky(flags, Mapping.WorldId(Mapping.Key), epoch, spawnX, spawnY, spawnZ, InputCapture.Yaw, InputCapture.Pitch, teleportSeq, vw, vh, 12f);
		}

		private static bool Settled() => Time.realtimeSinceStartup - stillSince >= 0.75f;

		/// <summary>
		/// PTR is still putting the level together or moving its player itself: Beneath between floors
		/// (its elevator, a portal), a custom level still creating its rooms, props and people (at most
		/// 20 s, in case it never says it has started).
		/// </summary>
		internal static bool LevelBusy()
		{
			try
			{
				if (Beneath.InTransition) return true;
				if (level == "LevelEditorPlay" && Time.realtimeSinceStartup - levelBegan < 20f
					&& (LevelEditorPlay.CreatingObjects || !LevelEditorPlay.StartedPlaying)) return true;
			}
			catch
			{
			}
			return false;
		}

		/// <summary>Minecraft's screens get a free, visible cursor; gameplay keeps it locked.</summary>
		internal static void ApplyCursor()
		{
			if (McScreenOpen)
			{
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
			}
			else
			{
				Cursor.lockState = CursorLockMode.Locked;
				Cursor.visible = false;
			}
		}

		private static void BeginLevel(string scene)
		{
			epoch++;
			teleportSeq++;
			waitingTeleport = true;
			spawnSent = false;
			stillSince = levelBegan = Time.realtimeSinceStartup;
			ptrTeleportPending = false;
			Puppet.TeleportWanted = false;
			Mapping.SetLevel(scene);
			link.SetLevel(Mapping.Key, Mapping.Title); // Minecraft opens this level's own world
			InputCapture.Pitch = 0;
			WorldRender.Clear();
			Combat.Reset();
			Collision.SendClear(link, epoch);
			Collision.Begin(epoch); // builds once PTR has finished putting the level together
			Plugin.L.LogInfo($"PTRCraft: level {scene} ({Mapping.Title}) -> Minecraft world PTRCraft-{Mapping.Key}, x+{Mapping.LevelX}, epoch {epoch}");
		}

		private void LateUpdate()
		{
			if (link == null || !Settings.Enabled.Value) return;
			try
			{
				if (Puppet.Active && haveMc)
				{
					link.ReadMc(ref mc);
					if (Puppet.Player != null && Puppet.Player.IsDead())
					{
						// PTR's death camera and lose screen play out; Minecraft's respawn follows the restart.
						Overlay.Show(false);
						return;
					}
					// PTR moved its player (a new Beneath floor, a portal, a reset): send Minecraft there.
					Puppet.DetectPtrTeleport();
					if (Puppet.TeleportWanted && !ptrTeleportPending)
					{
						ptrTeleportPending = true;
						teleportSeq++;
						Mapping.ToMc(Puppet.TeleportFeet, out spawnX, out spawnY, out spawnZ);
						Collision.CheckSoon(); // new ground around the new place
					}
					Puppet.Apply(mc, InputCapture.Yaw, InputCapture.Pitch, ptrTeleportPending, Time.unscaledDeltaTime);
					// PTR's trigger zones (elevators, exits, portals, shops) see the player where Minecraft put it.
					if (!PtrMenu && !ptrTeleportPending) Triggers.Update(Puppet.Player, Puppet.Body);
					Overlay.Pump(link);
					Selection.Update(link);
					WorldEntities.Update(link);
					if (!PtrMenu) ApplyCursor();
				}
			}
			catch (Exception e)
			{
				Plugin.L.LogError("PTRCraft: " + e);
			}
		}

		internal static string Describe()
		{
			var sb = new StringBuilder();
			sb.AppendLine($"frame {Time.frameCount} time {Time.time:F1} screen {Screen.width}x{Screen.height} scene {level}");
			sb.AppendLine($"status: {status}; launcher: {Launcher.Status}; ptr menu {PtrMenu}; minecraft screen {McScreenOpen}; cursor {Cursor.lockState}/{Cursor.visible}");
			sb.AppendLine($"mc alive {link?.McAlive()} pid {link?.McPid} flags {mc.Flags:X} pos {mc.X:F2} {mc.Y:F2} {mc.Z:F2} eye {mc.EyeX:F2} {mc.EyeY:F2} {mc.EyeZ:F2} yaw {mc.Yaw:F1} pitch {mc.Pitch:F1} fov {mc.Fov:F1} camera {mc.CameraMode}/{mc.CameraDistance:F2} ack {mc.TeleportAck}/{teleportSeq} frame {mc.Frame}");
			sb.AppendLine($"puppet active {Puppet.Active} player {(Puppet.Player != null ? Puppet.Player.transform.position.ToString() : "none")} look {InputCapture.Yaw:F1}/{InputCapture.Pitch:F1} ptr teleports {Puppet.Teleports}");
			sb.AppendLine($"collision epoch {epoch} {Collision.RegionsSent}/{Collision.RegionsTotal} regions, {Collision.Refreshes} refreshes; {Collision.Report}");
			sb.AppendLine($"triggers entered {Triggers.Entered}, last {Triggers.Last}; measured {Sampler.Measured} colliders with {Sampler.Rays} rays");
			sb.AppendLine($"overlay {Overlay.Width}x{Overlay.Height} frames {Overlay.Frames}; level x+{Mapping.LevelX}");
			sb.AppendLine($"entities drawn: {WorldEntities.Count}; player model {EntityMeshes.AvatarVertices} vertices; other Minecraft entities {EntityMeshes.SceneVertices} vertices");
			sb.AppendLine($"blocks: {WorldRender.SectionCount} sections, {WorldRender.Messages} render messages, shaders {WorldRender.ShaderUsed}");
			sb.AppendLine($"combat: {Combat.HitsIn} hits on PTR people ({Combat.Burns} set burning by Minecraft fire), {Combat.HitsOut} hits on the player; last: {Combat.LastHit}");
			sb.AppendLine($"ptr use (G): {Interact.Uses} uses; last: {Interact.Last}");
			return sb.ToString();
		}

		internal static string DevCommand(string cmd, string arg)
		{
			switch (cmd)
			{
				case "takeover":
					Puppet.Find();
					Puppet.TakeOver();
					return "ok";
				case "release":
					Puppet.Release();
					return "ok";
				case "use":
					Interact.Use();
					return Interact.Last;
				case "shaders":
				{
					var sb = new StringBuilder();
					foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Shader>()))
					{
						var s = o.Cast<Shader>();
						sb.AppendLine($"{s.name} supported={s.isSupported}");
					}
					return sb.ToString();
				}
				case "text":
				{
					var sb = new StringBuilder();
					foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<TMPro.TMP_Text>()))
					{
						var t = o.Cast<TMPro.TMP_Text>();
						if (t.text != null && t.text.Contains(arg, StringComparison.OrdinalIgnoreCase))
							sb.AppendLine($"{DevBridge.PathOf(t.transform)} active={t.gameObject.activeInHierarchy} text={t.text.Replace('\n', ' ')}");
					}
					return sb.ToString();
				}
				default:
					return null;
			}
		}
	}

	/// <summary>
	/// PTR re-locks and hides the cursor every frame (GameManager.SetCursorAppropriate). While a
	/// Minecraft screen is open the cursor is Minecraft's: free and visible.
	/// </summary>
	[HarmonyLib.HarmonyPatch(typeof(GameManager), nameof(GameManager.SetCursorAppropriate))]
	internal static class CursorPatch
	{
		private static bool Prefix()
		{
			if (!Puppet.Active || Runner.PtrMenu) return true;
			Runner.ApplyCursor();
			return false;
		}
	}
}
