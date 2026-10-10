using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace PTRCraft
{
	/// <summary>
	/// PTR's player becomes a puppet of the Minecraft player: PTR's movement, mouse look and fists are
	/// switched off, its whole body is hidden (Minecraft draws the hand in first person and its own
	/// player model in third person), and the player object and camera follow Minecraft every frame.
	/// The PTTRPlayer stays in the level, so PTR's people still see, chase and punch "the player".
	/// When PTR moves its player itself (a new Beneath floor, a portal, a reset), Minecraft follows.
	/// </summary>
	internal static class Puppet
	{
		public static PTTRPlayer Player;
		public static Camera Cam;
		public static bool Active;
		private static Transform headJoint;
		private static PTTRFirstPersonController controller;
		private static CharacterController cc;

		/// <summary>PTR's player's own collider (switched off while Minecraft plays; Triggers stands in for it).</summary>
		public static CharacterController Body => cc;
		private static WeaponAttack attack;
		private static readonly List<Behaviour> disabled = new();
		private static readonly List<Renderer> hidden = new();
		private static Vector3 feetOffset;
		private static Vector3 lastApplied;
		private static bool applied;
		private static float zoom;
		private static uint zoomMode;
		/// <summary>Set when PTR moved its player: Minecraft has to be teleported there.</summary>
		public static bool TeleportWanted;
		public static Vector3 TeleportFeet;
		public static int Teleports;

		public static bool Find()
		{
			if (Player != null && Player.WasCollected == false && Player.gameObject != null) return true;
			Player = null;
			Active = false;
			applied = false;
			disabled.Clear();
			hidden.Clear();
			foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PTTRPlayer>()))
			{
				var p = o.TryCast<PTTRPlayer>();
				if (p != null && p.IsLocalPlayer())
				{
					Player = p;
					break;
				}
			}
			if (Player == null) return false;
			controller = Player.GetComponent<PTTRFirstPersonController>();
			cc = Player.GetComponent<CharacterController>();
			attack = Player.GetComponent<WeaponAttack>();
			var cam = Player.transform.Find("Head Joint/First Person Camera");
			headJoint = Player.transform.Find("Head Joint");
			Cam = cam != null ? cam.GetComponent<Camera>() : Camera.main;
			return true;
		}

		/// <summary>The PTR player's feet in Minecraft coordinates (where Minecraft should put Steve).</summary>
		public static void FeetMc(out double x, out double y, out double z, out float yaw)
		{
			Mapping.ToMc(Player.GetFeetPosition(), out x, out y, out z);
			yaw = Mapping.McYaw(Player.transform.eulerAngles.y);
		}

		public static WeaponAttack Attack => attack;

		public static void TakeOver()
		{
			if (Active || Player == null) return;
			Active = true;
			applied = false;
			feetOffset = Player.transform.position - Player.GetFeetPosition();
			Disable(controller);
			if (attack != null) Disable(attack);
			foreach (var r in Player.GetComponentsInChildren<SimpleMouseRotator>(true)) Disable(r);
			if (Cam != null)
			{
				var fov = Cam.GetComponent<CameraFOV>();
				if (fov != null) Disable(fov);
			}
			if (cc != null) cc.enabled = false;
			// PTR's whole player body (first-person arms and legs, third-person model, held weapon):
			// like in Minecraft, first person shows only Minecraft's hand, third person Minecraft's model.
			HideBody();
			Plugin.L.LogInfo($"PTRCraft: took over the PTR player (feet offset {feetOffset})");
		}

		private static void HideBody()
		{
			foreach (var r in Player.GetComponentsInChildren<Renderer>(true))
			{
				if (r == null || !r.enabled) continue;
				// camera-attached effects (screen overlays) are not the body
				if (r.TryCast<ParticleSystemRenderer>() != null) continue;
				r.enabled = false;
				hidden.Add(r);
			}
		}

		private static void Disable(Behaviour b)
		{
			if (b == null || !b.enabled) return;
			b.enabled = false;
			disabled.Add(b);
		}

		/// <summary>Gives PTR its player back (Minecraft went away).</summary>
		public static void Release()
		{
			if (!Active) return;
			Active = false;
			Triggers.Reset();
			foreach (var b in disabled) if (b != null) b.enabled = true;
			foreach (var r in hidden) if (r != null) r.enabled = true;
			disabled.Clear();
			hidden.Clear();
			if (cc != null) cc.enabled = true;
			EntityMeshes.Hide();
			Plugin.L.LogInfo("PTRCraft: gave the player back to PTR");
		}

		/// <summary>PTR moved its player since we last placed it: Minecraft must follow.</summary>
		public static void DetectPtrTeleport()
		{
			if (!Active || Player == null || !applied || TeleportWanted) return;
			var now = Player.transform.position;
			if ((now - lastApplied).sqrMagnitude < 1.5f * 1.5f) return;
			TeleportWanted = true;
			TeleportFeet = now - feetOffset;
			Teleports++;
			Plugin.L.LogInfo($"PTRCraft: PTR moved its player to {TeleportFeet}; Minecraft follows");
		}

		/// <summary>Moves PTR's player and camera to Minecraft's player (call late in the frame).</summary>
		public static void Apply(in Link.McState mc, float yaw, float pitch, bool waitingForTeleport, float dt)
		{
			if (!Active || Player == null) return;
			// Keep the body hidden: PTR re-enables renderers on some events (weapon pickups, respawns).
			for (int i = 0; i < hidden.Count; i++) if (hidden[i] != null && hidden[i].enabled) hidden[i].enabled = false;
			var t = Player.transform;
			Vector3 eye;
			if (waitingForTeleport)
			{
				// Minecraft is on its way to where PTR put the player: stay there meanwhile.
				eye = TeleportFeet + Vector3.up * 1.62f;
			}
			else
			{
				var feet = Mapping.ToUnity(mc.X, mc.Y, mc.Z);
				t.position = feet + feetOffset;
				lastApplied = t.position;
				applied = true;
				eye = Mapping.ToUnity(mc.EyeX, mc.EyeY, mc.EyeZ);
				EntityMeshes.PlaceAvatar(mc.X, mc.Y, mc.Z, mc.CameraMode != 0);
			}
			t.rotation = Quaternion.Euler(0f, Mapping.UnityYaw(yaw), 0f);
			if (headJoint != null) headJoint.localRotation = Quaternion.Euler(pitch, 0f, 0f);
			if (Cam == null) return;
			// Minecraft's F5: 1 = behind the player, 2 = in front looking back. The distance is where
			// Minecraft's own camera collision stopped it; it pulls in at once and eases back out.
			bool mirrored = mc.CameraMode == 2;
			bool detached = mc.CameraMode != 0 && mc.CameraDistance > 0f;
			if (!detached || zoomMode != mc.CameraMode || mc.CameraDistance < zoom) zoom = detached ? mc.CameraDistance : 0f;
			else zoom += (mc.CameraDistance - zoom) * (1f - Mathf.Exp(-Math.Max(dt, 0f) / 0.2f));
			zoomMode = mc.CameraMode;
			var rot = Quaternion.Euler(mirrored ? -pitch : pitch, Mapping.UnityYaw(mirrored ? yaw + 180f : yaw), 0f);
			Cam.transform.rotation = rot;
			Cam.transform.position = detached ? eye - rot * Vector3.forward * zoom : eye;
			if (mc.Fov > 1f) Cam.fieldOfView = mc.Fov;
		}
	}
}
