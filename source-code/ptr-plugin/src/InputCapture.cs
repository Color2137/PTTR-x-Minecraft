using System;
using UnityEngine;

namespace PTRCraft
{
	/// <summary>
	/// Reads the keyboard and mouse in PTR's window (Unity's own input) and forwards them to the hidden
	/// Minecraft as SDL scancodes, like SkyCraft's InputBridge expects. Mouse look stays here: PTR's
	/// camera turns with zero latency and Minecraft follows the yaw/pitch we publish.
	/// </summary>
	internal static class InputCapture
	{
		private static readonly (KeyCode key, ushort sdl)[] Map = BuildMap();
		private static readonly bool[] Down = new bool[Map.Length];
		private static readonly bool[] Buttons = new bool[5];
		private static readonly ushort[] SdlButton = { 1, 3, 2, 4, 5 }; // Unity L R M X1 X2 -> SDL L R M
		private static bool released = true;
		private static bool axisBroken;

		public static float Yaw, Pitch; // Minecraft degrees
		public static float MouseAxisScale = 0.1f; // Unity's default "Mouse X" sensitivity
		public static string LastMouse = "";

		private static (KeyCode, ushort)[] BuildMap()
		{
			var m = new System.Collections.Generic.List<(KeyCode, ushort)>();
			for (int i = 0; i < 26; i++) m.Add((KeyCode.A + i, (ushort)(4 + i)));
			for (int i = 1; i <= 9; i++) m.Add((KeyCode.Alpha0 + i, (ushort)(29 + i)));
			m.Add((KeyCode.Alpha0, 39));
			m.Add((KeyCode.Return, 40));
			m.Add((KeyCode.Backspace, 42));
			m.Add((KeyCode.Tab, 43));
			m.Add((KeyCode.Space, 44));
			m.Add((KeyCode.Minus, 45));
			m.Add((KeyCode.Equals, 46));
			m.Add((KeyCode.LeftBracket, 47));
			m.Add((KeyCode.RightBracket, 48));
			m.Add((KeyCode.Backslash, 49));
			m.Add((KeyCode.Semicolon, 51));
			m.Add((KeyCode.Quote, 52));
			m.Add((KeyCode.BackQuote, 53));
			m.Add((KeyCode.Comma, 54));
			m.Add((KeyCode.Period, 55));
			m.Add((KeyCode.Slash, 56));
			m.Add((KeyCode.CapsLock, 57));
			for (int i = 0; i < 12; i++) m.Add((KeyCode.F1 + i, (ushort)(58 + i)));
			m.Add((KeyCode.Print, 70));
			m.Add((KeyCode.ScrollLock, 71));
			m.Add((KeyCode.Pause, 72));
			m.Add((KeyCode.Insert, 73));
			m.Add((KeyCode.Home, 74));
			m.Add((KeyCode.PageUp, 75));
			m.Add((KeyCode.Delete, 76));
			m.Add((KeyCode.End, 77));
			m.Add((KeyCode.PageDown, 78));
			m.Add((KeyCode.RightArrow, 79));
			m.Add((KeyCode.LeftArrow, 80));
			m.Add((KeyCode.DownArrow, 81));
			m.Add((KeyCode.UpArrow, 82));
			m.Add((KeyCode.Numlock, 83));
			m.Add((KeyCode.KeypadDivide, 84));
			m.Add((KeyCode.KeypadMultiply, 85));
			m.Add((KeyCode.KeypadMinus, 86));
			m.Add((KeyCode.KeypadPlus, 87));
			m.Add((KeyCode.KeypadEnter, 88));
			for (int i = 1; i <= 9; i++) m.Add((KeyCode.Keypad0 + i, (ushort)(88 + i)));
			m.Add((KeyCode.Keypad0, 98));
			m.Add((KeyCode.KeypadPeriod, 99));
			m.Add((KeyCode.LeftControl, 224));
			m.Add((KeyCode.LeftShift, 225));
			m.Add((KeyCode.LeftAlt, 226));
			m.Add((KeyCode.RightControl, 228));
			m.Add((KeyCode.RightShift, 229));
			m.Add((KeyCode.RightAlt, 230));
			return m.ToArray();
		}

		/// <summary>Releases everything Minecraft thinks is held.</summary>
		public static void ReleaseAll(Link link)
		{
			if (released) return;
			released = true;
			Array.Clear(Down);
			Array.Clear(Buttons);
			link.PushInput(Proto.InReleaseAll, 0);
		}

		/// <param name="mcScreenOpen">a Minecraft screen (inventory, chat...) is open: send the cursor and typed text</param>
		public static void Frame(Link link, float sensitivity, bool mcScreenOpen, int overlayW, int overlayH)
		{
			released = false;
			for (int i = 0; i < Map.Length; i++)
			{
				var (key, sdl) = Map[i];
				bool d = Input.GetKey(key);
				if (d == Down[i]) continue;
				Down[i] = d;
				// O opens Minecraft's own pause/options menu (Esc is PTR's pause menu), like SkyCraft.
				// G is PTR's use key (buttons, Beneath items, shops): Runner handles it, Minecraft never sees it.
				if (key == KeyCode.G && !mcScreenOpen) continue;
				if (key == KeyCode.O && !mcScreenOpen)
				{
					if (d) link.PushInput(Proto.InOpenMenu, 0);
					continue;
				}
				link.PushInput(Proto.InKey, sdl, d ? 1 : 0);
			}
			if (Input.GetKeyDown(KeyCode.Escape) && mcScreenOpen)
			{
				link.PushInput(Proto.InKey, 41, 1);
				link.PushInput(Proto.InKey, 41, 0);
			}
			for (int bi = 0; bi < 5; bi++)
			{
				bool d = Input.GetMouseButton(bi);
				if (d == Buttons[bi]) continue;
				Buttons[bi] = d;
				link.PushInput(Proto.InMouseButton, SdlButton[bi], d ? 1 : 0);
			}
			float wheel = Input.mouseScrollDelta.y;
			if (wheel != 0) link.PushInput(Proto.InScroll, 0, (int)Math.Round(wheel * 120));

			if (mcScreenOpen)
			{
				var mp = Input.mousePosition;
				int cx = (int)(mp.x * overlayW / Math.Max(1, Screen.width));
				int cy = (int)((Screen.height - mp.y) * overlayH / Math.Max(1, Screen.height));
				link.PushInput(Proto.InCursor, 0, cx, cy);
				foreach (char ch in Input.inputString)
				{
					if (ch >= 32 && ch != 127) link.PushInput(Proto.InText, 0, ch);
				}
				return;
			}

			// Mouse look, exactly as Minecraft turns the player (MouseHandler.turnPlayer + Entity.turn).
			float dx = 0, dy = 0;
			if (!axisBroken)
			{
				try
				{
					dx = Input.GetAxisRaw("Mouse X") / MouseAxisScale;
					dy = -Input.GetAxisRaw("Mouse Y") / MouseAxisScale;
				}
				catch (Exception e)
				{
					axisBroken = true;
					Plugin.L.LogWarning("PTRCraft: Unity mouse axes unavailable (" + e.Message + "); using Rewired");
				}
			}
			if (axisBroken)
			{
				var delta = Rewired.ReInput.controllers.Mouse.screenPositionDelta;
				dx = delta.x;
				dy = -delta.y;
			}
			LastMouse = $"{dx:F1},{dy:F1}";
			double d6 = sensitivity * 0.6 + 0.2;
			double f = d6 * d6 * d6 * 8.0;
			Yaw += (float)(dx * f * 0.15);
			Pitch = Mathf.Clamp(Pitch + (float)(dy * f * 0.15), -90f, 90f);
		}
	}
}
