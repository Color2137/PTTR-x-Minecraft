// <generated> by tools/sheets.mjs from sheets/combat_rules.json. Edit the sheet, not this file.
namespace PTRCraft
{
	internal static class CombatRules
	{
		/// <summary>PTR people within this distance are hittable in Minecraft (metres)</summary>
		public const float proxy_range = 64f;
		/// <summary>Hitbox width of a standing person (blocks)</summary>
		public const float proxy_width = 0.7f;
		/// <summary>Hitbox width of a person lying down (knocked out or dead) (blocks)</summary>
		public const float proxy_width_down = 1.4f;
		/// <summary>Hitbox height of a person lying down (blocks)</summary>
		public const float proxy_height_down = 0.6f;
		/// <summary>A Minecraft critical hit (falling) hits harder in PTR (x)</summary>
		public const float crit_bonus = 1.25f;
		/// <summary>The first hit on a calm person starts PTR's brawl in this radius (metres)</summary>
		public const float alert_range = 15f;
		/// <summary>Minecraft damage from which PTR plays its strong hurt sound (Minecraft damage)</summary>
		public const float strong_hit = 6f;
		/// <summary>PTR impact force per point of Minecraft knockback above an ordinary hit's (PTR's own pushes are 5-30: TestKnockbackX10/X30/Away5, Beneath melee 5, charge 10) (PTR force)</summary>
		public const float knock_force = 10f;
		/// <summary>Minecraft knockback an ordinary hit has (0.4, a sprint hit 0.9): hits up to this don't push, they flinch in place as PTR's punches do (Minecraft knockback)</summary>
		public const float knock_free = 0.45f;
		/// <summary>Force handed to PTR's Damage per (0.5 + Minecraft knockback): how hard a knocked-out body falls (PTR force)</summary>
		public const float hit_force = 3f;
		/// <summary>SkyCraft's Minecraft side divides hurt values by this (Skyrim scale), in hundredths (x)</summary>
		public const float mc_damage_scale = 5f;
	}
}
// </generated>
