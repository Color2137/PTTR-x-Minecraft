// <generated> by tools/sheets.mjs from sheets/settings.json. Edit the sheet, not this file.
using BepInEx.Configuration;

namespace PTRCraft
{
	/// <summary>BepInEx/config/dev.ptrcraft.cfg (sheets/settings.json).</summary>
	internal static class Settings
	{
		/// <summary>Replace PTR's player with the Minecraft player while Minecraft is linked</summary>
		public static ConfigEntry<bool> Enabled;
		/// <summary>Start the PTRCraft Minecraft (hidden) when PTR starts, unless one is already running</summary>
		public static ConfigEntry<bool> StartMinecraft;
		/// <summary>Prism Launcher exe; empty = %LOCALAPPDATA%\Programs\PrismLauncher\prismlauncher.exe</summary>
		public static ConfigEntry<string> Launcher;
		/// <summary>Arguments that start the PTRCraft instance</summary>
		public static ConfigEntry<string> LauncherArgs;
		/// <summary>Local debug console on 127.0.0.1</summary>
		public static ConfigEntry<bool> DevBridge;
		/// <summary>Debug console port</summary>
		public static ConfigEntry<int> DevPort;
		/// <summary>PTR damage per Minecraft damage point (a biker has about 1.0 health)</summary>
		public static ConfigEntry<float> DamageToPtr;
		/// <summary>PTR damage added to every Minecraft hit (a bare-fist punch = 0.23, about 4 to drop a biker)</summary>
		public static ConfigEntry<float> DamageToPtrBase;
		/// <summary>Minecraft damage per PTR damage dealt to the player (full PTR health = 10 hearts)</summary>
		public static ConfigEntry<float> DamageToMinecraft;
		/// <summary>Dying in Minecraft kills PTR's player too: PTR's own death screen and restart (or the end of a Beneath run)</summary>
		public static ConfigEntry<bool> MinecraftDeathIsPtrDeath;
		/// <summary>How far Minecraft hits push PTR's people (scenarios, arena, custom levels)</summary>
		public static ConfigEntry<float> Knockback;
		/// <summary>The same in Beneath, whose enemies PTR itself pushes about ten times less (they flew off the map at 1.0)</summary>
		public static ConfigEntry<float> KnockbackBeneath;
		/// <summary>Minecraft fire (fire, lava, campfires, magma, Fire Aspect, Flame arrows) sets PTR's people burning with PTR's own fire, like the arena's fire traps</summary>
		public static ConfigEntry<bool> MinecraftFireBurnsPtr;
		/// <summary>Minecraft draws its hand and HUD at most this tall; PTR scales it up</summary>
		public static ConfigEntry<int> OverlayMaxHeight;
		/// <summary>How often Minecraft renders its hand and HUD (PTR's camera never waits)</summary>
		public static ConfigEntry<int> MinecraftMaxFps;
		/// <summary>Hide PTR's health bar, power icons and key prompts while Minecraft's HUD is shown</summary>
		public static ConfigEntry<bool> HidePtrHud;
		/// <summary>Minecraft draws its own blocks, mobs, players, arrows, items, particles and your F5 body into the overlay, hidden behind PTR's walls and people; off: PTR draws Minecraft's blocks and entities itself with PTR's lights</summary>
		public static ConfigEntry<bool> MinecraftDrawsWorld;

		public static void Load(ConfigFile c)
		{
			Enabled = c.Bind("General", "Enabled", true, "Replace PTR's player with the Minecraft player while Minecraft is linked");
			StartMinecraft = c.Bind("Minecraft", "StartWithPTR", true, "Start the PTRCraft Minecraft (hidden) when PTR starts, unless one is already running");
			Launcher = c.Bind("Minecraft", "Launcher", "", "Prism Launcher exe; empty = %LOCALAPPDATA%\\Programs\\PrismLauncher\\prismlauncher.exe");
			LauncherArgs = c.Bind("Minecraft", "Arguments", "--launch PTRCraft", "Arguments that start the PTRCraft instance");
			DevBridge = c.Bind("Debug", "DevBridge", true, "Local debug console on 127.0.0.1");
			DevPort = c.Bind("Debug", "DevPort", 47347, "Debug console port");
			DamageToPtr = c.Bind("Combat", "MinecraftToPtrDamage", 0.11f, "PTR damage per Minecraft damage point (a biker has about 1.0 health)");
			DamageToPtrBase = c.Bind("Combat", "MinecraftToPtrDamageBase", 0.12f, "PTR damage added to every Minecraft hit (a bare-fist punch = 0.23, about 4 to drop a biker)");
			DamageToMinecraft = c.Bind("Combat", "PtrToMinecraftDamage", 20f, "Minecraft damage per PTR damage dealt to the player (full PTR health = 10 hearts)");
			MinecraftDeathIsPtrDeath = c.Bind("Combat", "MinecraftDeathEndsPtrLevel", true, "Dying in Minecraft kills PTR's player too: PTR's own death screen and restart (or the end of a Beneath run)");
			Knockback = c.Bind("Combat", "Knockback", 1f, "How far Minecraft hits push PTR's people (scenarios, arena, custom levels)");
			KnockbackBeneath = c.Bind("Combat", "KnockbackBeneath", 0.1f, "The same in Beneath, whose enemies PTR itself pushes about ten times less (they flew off the map at 1.0)");
			MinecraftFireBurnsPtr = c.Bind("Combat", "MinecraftFireBurnsPtr", true, "Minecraft fire (fire, lava, campfires, magma, Fire Aspect, Flame arrows) sets PTR's people burning with PTR's own fire, like the arena's fire traps");
			OverlayMaxHeight = c.Bind("Display", "OverlayMaxHeight", 1080, "Minecraft draws its hand and HUD at most this tall; PTR scales it up");
			MinecraftMaxFps = c.Bind("Display", "MinecraftMaxFps", 75, "How often Minecraft renders its hand and HUD (PTR's camera never waits)");
			HidePtrHud = c.Bind("Display", "HidePtrHud", true, "Hide PTR's health bar, power icons and key prompts while Minecraft's HUD is shown");
			MinecraftDrawsWorld = c.Bind("Display", "MinecraftDrawsWorld", true, "Minecraft draws its own blocks, mobs, players, arrows, items, particles and your F5 body into the overlay, hidden behind PTR's walls and people; off: PTR draws Minecraft's blocks and entities itself with PTR's lights");
		}
	}
}
// </generated>
