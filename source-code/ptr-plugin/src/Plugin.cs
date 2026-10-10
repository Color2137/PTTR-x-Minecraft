using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace PTRCraft
{
	/// <summary>
	/// Minecraft inside Paint the Town Red: PTR stays the game on screen; a hidden Minecraft (the
	/// ptrcraft Fabric mod, a fork of SkyCraft) is the player. This plugin is the PTR end of the
	/// shared-memory link (protocol: research/skycraft_protocol.h, mapping Local\PTRCraft_v1).
	/// </summary>
	[BepInPlugin(Id, "PTRCraft", Version)]
	public class Plugin : BasePlugin
	{
		public const string Id = "dev.ptrcraft";
		public const string Version = "0.2.0";

		internal static ManualLogSource L;
		internal static Plugin Instance;

		public override void Load()
		{
			Instance = this;
			L = Log;
			Settings.Load(Config);
			// One patch class at a time: a PTR update that renames a method costs that patch, not the plugin.
			var harmony = new Harmony(Id);
			foreach (var t in typeof(Plugin).Assembly.GetTypes())
			{
				if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
				try
				{
					harmony.CreateClassProcessor(t).Patch();
				}
				catch (System.Exception e)
				{
					L.LogWarning($"PTRCraft: patch {t.Name} failed: {e.Message}");
				}
			}
			CheckHooks();
			AddComponent<Runner>();
			if (Settings.DevBridge.Value) DevBridge.Start(Settings.DevPort.Value);
			L.LogInfo($"PTRCraft {Version} loaded");
		}

		/// <summary>Every PTR member the plugin touches (sheets/ptr_hooks.json) must exist in this PTR version.</summary>
		private static void CheckHooks()
		{
			const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
			int missing = 0;
			foreach (var (id, cls, member, _) in PtrHooks.All)
			{
				// PTR's own classes live in Assembly-CSharp; the rest are Unity's (physics, core). Looking them
				// up directly avoids scanning every assembly (Unity's UIElements types don't load under interop).
				var type = typeof(PTTRPlayer).Assembly.GetType(cls)
					?? typeof(UnityEngine.Collider).Assembly.GetType("UnityEngine." + cls)
					?? typeof(UnityEngine.Behaviour).Assembly.GetType("UnityEngine." + cls);
				bool ok = false;
				for (var t = type; t != null && !ok; t = t.BaseType)
					ok = t.GetMember(member, all).Length > 0 || t.GetMember("get_" + member, all).Length > 0;
				if (ok) continue;
				missing++;
				L.LogWarning($"PTRCraft: this Paint the Town Red has no {cls}.{member} (hook '{id}'); that part won't work until the plugin is updated");
			}
			L.LogInfo($"PTRCraft: {PtrHooks.All.Length - missing}/{PtrHooks.All.Length} PTR hooks found");
		}
	}
}
