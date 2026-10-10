using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace PTRCraft
{
	/// <summary>
	/// Starts the PTRCraft Minecraft (Prism instance, hidden) when PTR starts. If Minecraft hasn't
	/// linked a while later (Prism busy, or it hadn't seen the instance yet), it asks again, a few times.
	/// </summary>
	internal static class Launcher
	{
		private const int MaxTries = 3;
		private const float RetryAfter = 75f; // seconds: Minecraft's first start with Prism takes a while
		private static int tries;
		private static float lastTry = -1000f;
		public static string Status = "not started";

		public static void EnsureRunning(Link link)
		{
			if (!Settings.StartMinecraft.Value) return;
			// The Minecraft mod holds this mutex while it runs (SkyLink.announceRunning).
			if (link.McAlive() || Native.MutexExists(Proto.MappingName + "_minecraft"))
			{
				if (tries == 0) Status = "already running";
				else if (Status != "running") Status = "running";
				return;
			}
			if (tries >= MaxTries)
			{
				Status = $"Minecraft didn't start after {MaxTries} tries: open Prism and launch 'PTRCraft' once to see why";
				return;
			}
			if (Time.realtimeSinceStartup - lastTry < RetryAfter) return;
			lastTry = Time.realtimeSinceStartup;
			tries++;
			string exe = Settings.Launcher.Value;
			if (string.IsNullOrWhiteSpace(exe))
				exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PrismLauncher", "prismlauncher.exe");
			if (!File.Exists(exe))
			{
				Status = "Prism Launcher not found at " + exe;
				tries = MaxTries;
				Plugin.L.LogWarning("PTRCraft: " + Status);
				return;
			}
			try
			{
				// Through the shell, so Minecraft isn't a child of PTR (it outlives a PTR crash and saves).
				Process.Start(new ProcessStartInfo(exe, Settings.LauncherArgs.Value) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
				Status = $"starting Minecraft (try {tries})";
				Plugin.L.LogInfo($"PTRCraft: started {exe} {Settings.LauncherArgs.Value} (try {tries})");
			}
			catch (Exception e)
			{
				Status = "couldn't start Minecraft: " + e.Message;
				Plugin.L.LogWarning("PTRCraft: " + Status);
			}
		}
	}
}
