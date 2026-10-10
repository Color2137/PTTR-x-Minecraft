using System.Collections.Generic;
using UnityEngine;

namespace PTRCraft
{
	/// <summary>
	/// PTR's in-game HUD (health bar, power icons, key prompts) is hidden while Minecraft's HUD is the
	/// player's; win/lose, pause and level screens stay PTR's.
	/// </summary>
	internal static class PtrHud
	{
		private static readonly string[] Panels = { "GameplayUICanvas/PanelGameHUD", "GameplayUICanvas/Tooltip", "GameplayUICanvas/BottomRight", "GameplayUICanvas/Panel-GamepadHints" };
		private static readonly List<CanvasGroup> groups = new();
		private static bool found;

		public static void Hide()
		{
			if (!found)
			{
				found = true;
				groups.Clear();
				foreach (var p in Panels)
				{
					var go = GameObject.Find(p);
					if (go == null) continue;
					var g = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
					groups.Add(g);
				}
				// PTR's key prompts ("Q - KICK", "THROW", ...) describe PTR's controls, not Minecraft's
				foreach (var o in Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<ControlsHelp>()))
				{
					var help = o.TryCast<ControlsHelp>();
					if (help != null && help.canvasGroup != null) groups.Add(help.canvasGroup);
				}
			}
			// PTR animates these groups itself, so hold them down every frame
			foreach (var g in groups) if (g != null) g.alpha = 0f;
		}

		public static void Restore()
		{
			foreach (var g in groups) if (g != null) g.alpha = 1f;
			groups.Clear();
			found = false;
		}
	}
}
