using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace PTRCraft
{
	/// <summary>
	/// PTR's people <-> Minecraft combat. Every person in the level is published to Minecraft as an
	/// invisible, hittable proxy (SkyCraft's actor table); a Minecraft hit on one comes back as a hit
	/// event and lands through PTR's own Enemy.Damage (hurt sounds, impact animation, knockdown,
	/// death, and the whole bar joining the fight). Anything that hurts PTR's player becomes
	/// Minecraft damage instead, so Minecraft's hearts are the player's real health.
	/// </summary>
	internal static class Combat
	{
		private static readonly List<Enemy> people = new();
		private static readonly Dictionary<uint, Enemy> byId = new();
		private static float nextScan;
		public static int HitsIn, HitsOut, Burns;
		public static string LastHit = "";

		public static void Reset()
		{
			people.Clear();
			byId.Clear();
			nextScan = 0;
		}

		private static uint IdOf(Enemy e) => (uint)e.GetInstanceID() | 0x80000000u;

		private static void Scan()
		{
			people.Clear();
			byId.Clear();
			foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Enemy>()))
			{
				var e = o.TryCast<Enemy>();
				if (e == null || !e.gameObject.activeInHierarchy) continue;
				people.Add(e);
				byId[IdOf(e)] = e;
			}
		}

		/// <summary>Writes the actor table (call every frame while Minecraft plays).</summary>
		public static void Publish(Link link)
		{
			if (Time.time >= nextScan)
			{
				Scan();
				nextScan = Time.time + 2f;
			}
			var player = Puppet.Player != null ? Puppet.Player.transform.position : Vector3.zero;
			link.BeginActors();
			int n = 0;
			foreach (var e in people)
			{
				if (n >= Proto.MaxActors) break;
				if (e == null || e.WasCollected) continue;
				var hips = e.hipsTransform != null ? e.hipsTransform.position : e.transform.position;
				if ((hips - player).sqrMagnitude > CombatRules.proxy_range * CombatRules.proxy_range) continue;
				float feetY = hips.y - 0.95f;
				if (e.leftFootTransform != null && e.rightFootTransform != null)
					feetY = Math.Min(e.leftFootTransform.position.y, e.rightFootTransform.position.y) - 0.08f;
				bool down = e.isDead || e.IsKnockedDown;
				float height = down ? CombatRules.proxy_height_down : Math.Max(1.2f, (e.headTransform != null ? e.headTransform.position.y + 0.3f : hips.y + 0.9f) - feetY);
				Mapping.ToMc(new Vector3(hips.x, feetY, hips.z), out double x, out double y, out double z);
				uint flags = Proto.ActorHostile;
				if (e.isDead) flags |= Proto.ActorDead;
				if (e.inCombat) flags |= Proto.ActorInCombat;
				float hp = e.originalHealth > 0 ? Mathf.Clamp01(e.health / e.originalHealth) : 1f;
				link.WriteActor(n++, IdOf(e), flags, (float)x, (float)y, (float)z, Mapping.McYaw(e.transform.eulerAngles.y), down ? CombatRules.proxy_width_down : CombatRules.proxy_width, height, hp, 10, e.name);
			}
			link.EndActors(n);
		}

		/// <summary>Minecraft's events: hits on PTR's people, the Minecraft player's death.</summary>
		public static void HandleEvents(Link link)
		{
			Span<Link.McEvent> evs = stackalloc Link.McEvent[64];
			int n = link.DrainEvents(evs);
			for (int i = 0; i < n; i++)
			{
				var ev = evs[i];
				if (ev.Type == Proto.EvHitActor) Hit(ev);
				else if (ev.Type == Proto.EvPlayerDied) PlayerDied();
			}
		}

		private static void Hit(in Link.McEvent ev)
		{
			if (!byId.TryGetValue(ev.FormId, out var e) || e == null || e.WasCollected || e.isDead) return;
			HitsIn++;
			if ((ev.Flags & Proto.HitFire) != 0 && Settings.MinecraftFireBurnsPtr.Value)
			{
				// Minecraft fire (a fire block, lava, a campfire, magma, Fire Aspect, Flame arrows): PTR's own
				// burning, as the arena's fire does. Its flames do the damage, not Minecraft's fire ticks.
				if (!e.onFire)
				{
					if (!e.inCombat) e.StartCombat(0.3f, true, CombatRules.alert_range);
					e.SetOnFire();
					Burns++;
				}
				LastHit = $"{e.name}: Minecraft fire -> {(e.onFire ? "burning" : "can't burn")}";
				return;
			}
			// Minecraft damage -> PTR damage: a bare fist is a PTR punch (about 4 to drop a biker),
			// a diamond sword about one.
			float mcDamage = ev.A;
			float ptr = Settings.DamageToPtrBase.Value + Settings.DamageToPtr.Value * mcDamage;
			if ((ev.Flags & 1) != 0) ptr *= CombatRules.crit_bonus; // critical
			var player = Puppet.Player;
			var hips = e.hipsTransform != null ? e.hipsTransform.position : e.transform.position;
			var from = player != null ? player.transform.position + Vector3.up * 0.6f : hips;
			var dirMc = new Vector3(ev.B, 0, ev.C);
			var dir = new Vector3(dirMc.x, 0.2f, -dirMc.z);
			if (dir.sqrMagnitude < 1e-4f) dir = (hips - from).normalized;
			dir.Normalize();
			var hitPoint = hips + Vector3.up * 0.3f;
			var local = e.transform.InverseTransformDirection(dir);
			if (!e.inCombat) e.StartCombat(0.3f, true, CombatRules.alert_range);
			IEnemyTarget hitter = player != null ? player.Cast<IEnemyTarget>() : null;
			// PTR's punches make people flinch where they stand, they don't shove them: an ordinary Minecraft
			// hit doesn't push either; only real knockback (sprinting, the Knockback enchantment) does, about
			// as hard as PTR's own pushes. Beneath's enemies are pushed further by the same force: their own scale.
			float knock = GameManager.IsBeneath ? Settings.KnockbackBeneath.Value : Settings.Knockback.Value;
			e.Damage(ptr, hitPoint, local, dir * (CombatRules.hit_force * (0.5f + ev.D) * knock), hitter, true, false, mcDamage >= CombatRules.strong_hit);
			e.AnimateImpact(true, true, 1f, from, ptr, 0);
			float push = CombatRules.knock_force * Math.Max(0f, ev.D - CombatRules.knock_free) * knock;
			if (push > 0.01f) e.ApplyImpactForce(dir, push);
			LastHit = $"{e.name}: Minecraft {mcDamage:F1} -> PTR {ptr:F2}, health {e.health:F2}{(e.isDead ? " (down)" : "")}";
		}

		/// <summary>True while the plugin itself kills PTR's player (the SetHealth patch lets it through).</summary>
		internal static bool KillingPlayer;

		private static void PlayerDied()
		{
			Plugin.L.LogInfo("PTRCraft: the Minecraft player died");
			// PTR's rules: dying ends the level (or the Beneath run) with PTR's own death and restart.
			var p = Puppet.Player;
			if (!Settings.MinecraftDeathIsPtrDeath.Value || p == null || p.IsDead()) return;
			KillingPlayer = true;
			try
			{
				p.SetHealth(0f, true);
			}
			finally
			{
				KillingPlayer = false;
			}
		}

		/// <summary>Turns a PTR hit on the player into Minecraft damage.</summary>
		internal static void PlayerHurt(float ptrDamage, Enemy attacker)
		{
			var link = Runner.LinkInstance;
			if (link == null) return;
			float mc = ptrDamage * Settings.DamageToMinecraft.Value;
			if (mc <= 0) return;
			HitsOut++;
			uint id = attacker != null ? IdOf(attacker) : 0;
			// SkyCraft's Minecraft side divides by 5 (Skyrim scale) and takes hundredths
			link.PushInput(Proto.InHurt, Proto.HurtMelee, (int)Math.Round(mc * CombatRules.mc_damage_scale * 100f), (int)id, 0);
		}

		internal static Enemy lastAttacker;
	}

	/// <summary>While Minecraft plays, PTR's player never loses PTR health: the hit goes to Minecraft.</summary>
	[HarmonyPatch(typeof(PTTRPlayer), nameof(PTTRPlayer.SetHealth))]
	internal static class SetHealthPatch
	{
		private static bool Prefix(PTTRPlayer __instance, float setHealthTo, bool loweringHealth)
		{
			if (Combat.KillingPlayer || !Puppet.Active || __instance == null || Puppet.Player == null || __instance.Pointer != Puppet.Player.Pointer) return true;
			float now = __instance.health;
			if (!loweringHealth || setHealthTo >= now) return true;
			Combat.PlayerHurt(now - setHealthTo, Combat.lastAttacker);
			Combat.lastAttacker = null;
			return false;
		}
	}

	/// <summary>Remembers who punched the player, so Minecraft knows where the hit came from.</summary>
	[HarmonyPatch(typeof(PTTRPlayer), nameof(PTTRPlayer.GetPunched))]
	internal static class GetPunchedPatch
	{
		private static void Prefix(Enemy hittingEnemy)
		{
			if (Puppet.Active) Combat.lastAttacker = hittingEnemy;
		}
	}

	/// <summary>Falls are Minecraft's to judge (its fall damage), not PTR's.</summary>
	[HarmonyPatch(typeof(PTTRPlayer), nameof(PTTRPlayer.FallDamage))]
	internal static class FallDamagePatch
	{
		private static bool Prefix() => !Puppet.Active;
	}
}
