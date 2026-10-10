using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace PTRCraft
{
	/// <summary>
	/// PTR's trigger zones (Beneath's hub elevator and level exits, portals, shortcut portals, boss and
	/// challenge rooms, shop zones, pickups, kill zones; 139 OnTriggerEnter/Stay handlers) react to
	/// PTR's player's CharacterController. While Minecraft plays, that controller is switched off (the
	/// player is moved where Minecraft is), so PhysX never reports the player in a trigger. This does
	/// PhysX's part instead: every frame it finds the trigger colliders the player's capsule overlaps
	/// and sends OnTriggerEnter / OnTriggerStay / OnTriggerExit as Unity would, to the trigger's
	/// object, its rigidbody's object and the player. PTR's build has no Physics.GetIgnoreLayerCollision
	/// (stripped), so instead of PTR's layer matrix it leaves out the zones PTR's people carry (their
	/// own hit and sight zones); PTR's handlers check what touched them themselves.
	/// </summary>
	internal static class Triggers
	{
		private static readonly Il2CppReferenceArray<Collider> buffer = new(128);
		private static HashSet<IntPtr> inside = new(), seen = new();
		private static readonly Dictionary<IntPtr, Collider> byPtr = new();
		private static readonly HashSet<IntPtr> ignored = new();
		private static float nextStay;
		private static int errors;
		public static int Entered;
		public static string Last = "";

		/// <summary>The player left the level or PTR has it back: forget what it was in.</summary>
		public static void Reset()
		{
			inside.Clear();
			byPtr.Clear();
			ignored.Clear();
		}

		public static void Update(PTTRPlayer player, CharacterController body)
		{
			if (player == null || body == null) return;
			var t = body.transform;
			var scale = t.lossyScale;
			float r = body.radius * Math.Max(Math.Abs(scale.x), Math.Abs(scale.z));
			float h = Math.Max(body.height * Math.Abs(scale.y), 2 * r);
			var c = t.TransformPoint(body.center);
			var up = new Vector3(0, h * 0.5f - r, 0);
			int n = Physics.OverlapCapsuleNonAlloc(c - up, c + up, r, buffer, ~0, QueryTriggerInteraction.Collide);
			var root = player.transform;
			seen.Clear();
			for (int i = 0; i < n && i < buffer.Length; i++)
			{
				var col = buffer[i];
				if (col == null || !col.isTrigger || !col.enabled) continue;
				var key = col.Pointer;
				if (ignored.Contains(key)) continue;
				if (inside.Contains(key))
				{
					seen.Add(key);
					continue;
				}
				var ct = col.transform;
				if (ct == root || ct.IsChildOf(root) || OnPerson(ct))
				{
					ignored.Add(key); // the player's own, or one of PTR's people's
					continue;
				}
				seen.Add(key);
				byPtr[key] = col;
				Entered++;
				Last = DevBridge.PathOf(ct);
				Send(col, body, "OnTriggerEnter");
			}
			// Stays at the physics rate, like Unity.
			bool stay = Time.time >= nextStay;
			if (stay) nextStay = Time.time + Time.fixedDeltaTime;
			foreach (var key in inside)
			{
				if (!byPtr.TryGetValue(key, out var col)) continue;
				if (!seen.Contains(key))
				{
					if (col != null) Send(col, body, "OnTriggerExit");
					byPtr.Remove(key);
				}
				else if (stay && col != null)
				{
					Send(col, body, "OnTriggerStay");
				}
			}
			(inside, seen) = (seen, inside);
		}

		// GetComponentInParent is stripped from PTR's build: walk up by hand.
		private static bool OnPerson(Transform t)
		{
			for (var q = t; q != null; q = q.parent) if (q.GetComponent<Enemy>() != null) return true;
			return false;
		}

		private static void Send(Collider trigger, Collider playerCollider, string message)
		{
			try
			{
				var go = trigger.gameObject;
				go.SendMessage(message, playerCollider, SendMessageOptions.DontRequireReceiver);
				var rb = trigger.attachedRigidbody;
				if (rb != null && rb.gameObject != go) rb.gameObject.SendMessage(message, playerCollider, SendMessageOptions.DontRequireReceiver);
				if (trigger != null) playerCollider.gameObject.SendMessage(message, trigger, SendMessageOptions.DontRequireReceiver);
			}
			catch (Exception e)
			{
				if (errors++ < 5) Plugin.L.LogWarning($"PTRCraft: {message} on {trigger?.name}: {e.Message}");
			}
		}
	}
}
