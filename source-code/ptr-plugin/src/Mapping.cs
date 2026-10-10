using UnityEngine;

namespace PTRCraft
{
	/// <summary>
	/// PTR (Unity: left-handed, Y up, metres) <-> Minecraft (right-handed, Y up, blocks).
	/// 1 metre = 1 block. MC = (ux + levelX, uy, -uz).
	/// Yaw: Minecraft 0 faces +Z (south) = Unity -Z, so Unity yaw = MC yaw + 180. Pitch is the same.
	///
	/// Every PTR level plays in its own Minecraft world (PTRCraft-&lt;key&gt;): each scenario, Pirate
	/// Cove by day and by night, each arena challenge and the endless arena, Beneath's hub and each of
	/// its floors and Trickster's rooms, the sandbox and each custom level. Each level
	/// also keeps its own stretch of X (0.1.x kept every level in one world that way, and the Biker
	/// Bar's builds from then stay where they were).
	/// </summary>
	internal static class Mapping
	{
		public static double LevelX;
		/// <summary>The current level's key: letters, digits and '-' (it names the Minecraft world's folder).</summary>
		public static string Key = "";
		/// <summary>The current level's name for people ("Pirate Cove (Night)").</summary>
		public static string Title = "";

		public static Vector3 ToUnity(double x, double y, double z) => new((float)(x - LevelX), (float)y, (float)-z);

		public static void ToMc(Vector3 u, out double x, out double y, out double z)
		{
			x = u.x + LevelX;
			y = u.y;
			z = -u.z;
		}

		public static float UnityYaw(float mcYaw) => mcYaw + 180f;
		public static float McYaw(float unityYaw) => Mathf.Repeat(unityYaw - 180f + 180f, 360f) - 180f;

		/// <summary>Picks the level's key, name and stretch of X when PTR has started a level.</summary>
		public static void SetLevel(string scene)
		{
			Key = KeyFor(scene, out Title);
			// Append only: an index is where a level's builds are in its world.
			string[] known = { "BikerBar", "Saloon", "Disco", "PirateCove", "Prison", "Arena", "EndlessArena", "Sandbox", "BeneathHub", "BeneathLevel", "LevelEditorPlay", "PirateCoveNight" };
			int index = System.Array.IndexOf(known, Key);
			if (index < 0) index = 32 + (int)(WorldId(Key) % 64);
			LevelX = 4096.0 * (index + 1);
		}

		/// <summary>The key (and name) of the level PTR is playing in this scene right now.</summary>
		public static string KeyFor(string scene, out string title)
		{
			LevelKey(scene, out string key, out title);
			return key;
		}

		private static void LevelKey(string scene, out string key, out string title)
		{
			if (scene == "PirateCove" && PaintTheTownRed.PirateCoveNight)
			{
				key = "PirateCoveNight";
				title = "Pirate Cove (Night)";
				return;
			}
			if (scene == "EndlessArena" || (scene == "Arena" && GameManager.IsEndlessArena))
			{
				key = "EndlessArena";
				title = "Endless Arena";
				return;
			}
			if (scene == "Arena")
			{
				// The arena's challenges (1st to 5th) share the scene: each gets its own world.
				int challenge = Arena.CurrentChallengeIndex;
				if (challenge >= 0 && challenge < 64)
				{
					key = "Arena" + (challenge + 1);
					title = "Arena challenge " + (challenge + 1);
					return;
				}
			}
			if (scene == "BeneathLevel" || scene == "BeneathTesting")
			{
				BeneathKey(out key, out title);
				return;
			}
			if (scene == "LevelEditorPlay")
			{
				// A custom level (your own or from the Workshop): one world per level file.
				string file = null, editing = null;
				try
				{
					file = LevelEditor.LevelFileToPlay;
					// Testing from the editor plays a shared temp file: name the world after the level being edited.
					if (LevelEditor.TestingLevel) editing = LevelEditor.CurrentLevelSimpleName;
				}
				catch { }
				if (!string.IsNullOrEmpty(editing))
				{
					key = "Custom-" + Clean(editing, 24) + "-" + (WorldId(editing) & 0xFFFFFF).ToString("x6");
					title = "Custom level " + (editing.Length > 40 ? editing.Substring(0, 40) : editing) + " (testing)";
					return;
				}
				if (!string.IsNullOrEmpty(file))
				{
					string name = System.IO.Path.GetFileNameWithoutExtension(file);
					key = "Custom-" + Clean(name, 24) + "-" + (WorldId(file) & 0xFFFFFF).ToString("x6");
					title = "Custom level " + (name.Length > 40 ? name.Substring(0, 40) : name);
					return;
				}
				key = "Custom";
				title = "Custom level";
				return;
			}
			key = Clean(scene, 40);
			title = scene switch
			{
				"BikerBar" => "Biker Bar",
				"PirateCove" => "Pirate Cove (Day)",
				"EndlessArena" => "Endless Arena",
				"BeneathHub" => "Beneath (hub)",
				"BeneathLevel" => "Beneath",
				_ => scene,
			};
		}

		// Beneath's areas (Beneath.AreaIndex_*), and the scenarios the Trickster's rooms copy (TricksterRoom_*).
		private static readonly string[] BeneathAreas = { "CAV", "CRY", "RUI", "ISL", "TRI", "FIR" };
		private static readonly string[] TricksterRooms = { "BikerBar", "Disco", "Prison", "PirateCove" };

		/// <summary>
		/// Beneath: one world per area and floor (Beneath-CAV-1, Beneath-CRY-2, ...), and one per
		/// Trickster's room. Floors are generated anew every run, so a floor's world outlives its layout.
		/// </summary>
		private static void BeneathKey(out string key, out string title)
		{
			int area = Beneath.CurrentArea, floor = Beneath.CurrentLevel;
			string tag = area >= 0 && area < BeneathAreas.Length ? BeneathAreas[area] : "Area" + area;
			string name = tag;
			try
			{
				var names = Beneath.AreaNames;
				if (names != null && area >= 0 && area < names.Length && !string.IsNullOrEmpty(names[area]))
					name = System.Text.RegularExpressions.Regex.Replace(names[area], "<[^>]*>", "").Trim(); // no rich-text tags
			}
			catch { }
			int room = Beneath.CurrentTricksterRoom;
			if (tag == "TRI" && room >= 0 && room < TricksterRooms.Length)
			{
				key = "Beneath-TRI-" + TricksterRooms[room];
				title = "Beneath: " + name + " (" + TricksterRooms[room] + ")";
				return;
			}
			key = "Beneath-" + tag + "-" + (floor + 1);
			title = "Beneath: " + name + ", floor " + (floor + 1);
		}

		private static string Clean(string s, int max)
		{
			var sb = new System.Text.StringBuilder();
			foreach (char c in s)
			{
				if (sb.Length >= max) break;
				if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
				else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
			}
			string r = sb.ToString().Trim('-');
			return r.Length == 0 ? "Level" : r;
		}

		public static uint WorldId(string key)
		{
			uint h = 2166136261;
			foreach (char c in key) h = (h ^ c) * 16777619;
			return h == 0 ? 1 : h;
		}
	}
}
