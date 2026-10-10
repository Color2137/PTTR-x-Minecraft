using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PTRCraft
{
	/// <summary>
	/// Development console on 127.0.0.1: one text command per line, answered from Unity's main
	/// thread. Lets an agent read the live scene (hierarchy, components, fields) without UnityExplorer.
	/// </summary>
	internal static class DevBridge
	{
		private sealed class Job
		{
			public string Line;
			public string Reply;
			public readonly ManualResetEventSlim Done = new();
		}

		private static readonly ConcurrentQueue<Job> Jobs = new();

		public static void Start(int port)
		{
			var t = new Thread(() =>
			{
				try
				{
					var l = new TcpListener(IPAddress.Loopback, port);
					l.Start();
					Plugin.L.LogInfo($"PTRCraft debug console on 127.0.0.1:{port}");
					while (true)
					{
						var c = l.AcceptTcpClient();
						new Thread(() => Serve(c)) { IsBackground = true }.Start();
					}
				}
				catch (Exception e)
				{
					Plugin.L.LogWarning("debug console stopped: " + e.Message);
				}
			}) { IsBackground = true, Name = "PTRCraft-dev" };
			t.Start();
		}

		private static void Serve(TcpClient c)
		{
			using (c)
			using (var s = c.GetStream())
			using (var r = new StreamReader(s, Encoding.UTF8))
			using (var w = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true })
			{
				string line;
				while ((line = r.ReadLine()) != null)
				{
					var j = new Job { Line = line.Trim() };
					Jobs.Enqueue(j);
					if (!j.Done.Wait(20000)) j.Reply = "ERROR timeout (main thread busy?)";
					w.Write(j.Reply ?? "");
					w.Write("\n.\n");
				}
			}
		}

		/// <summary>Runs queued commands; called from Runner.Update.</summary>
		public static void Pump()
		{
			while (Jobs.TryDequeue(out var j))
			{
				try
				{
					j.Reply = Run(j.Line);
				}
				catch (Exception e)
				{
					j.Reply = "ERROR " + e;
				}
				j.Done.Set();
			}
		}

		private static string Run(string line)
		{
			var parts = line.Split(' ', 2);
			string cmd = parts[0], arg = parts.Length > 1 ? parts[1] : "";
			var sb = new StringBuilder();
			switch (cmd)
			{
				case "ping":
					return "pong " + Time.frameCount;
				case "scene":
					for (int i = 0; i < SceneManager.sceneCount; i++)
					{
						var sc = SceneManager.GetSceneAt(i);
						sb.AppendLine($"scene {sc.name} loaded={sc.isLoaded} roots={sc.rootCount}");
						foreach (var go in sc.GetRootGameObjects()) sb.AppendLine("  " + go.name + (go.activeSelf ? "" : " [off]"));
					}
					return sb.ToString();
				case "tree":
				{
					var a = arg.Split(' ');
					int depth = a.Length > 1 ? int.Parse(a[1]) : 2;
					var t = Find(a[0]);
					if (t == null) return "not found";
					Tree(t, 0, depth, sb);
					return sb.ToString();
				}
				case "comps":
				{
					var t = Find(arg);
					if (t == null) return "not found";
					foreach (var comp in t.GetComponents<Component>())
						sb.AppendLine(comp == null ? "(missing)" : comp.GetIl2CppType().FullName + (comp.TryCast<Behaviour>() is { } b ? (b.enabled ? "" : " [disabled]") : ""));
					var p = t.position;
					sb.AppendLine($"pos {p.x:F3} {p.y:F3} {p.z:F3} rot {t.eulerAngles.x:F1} {t.eulerAngles.y:F1} {t.eulerAngles.z:F1} layer {LayerMask.LayerToName(t.gameObject.layer)}");
					return sb.ToString();
				}
				case "fields":
				{
					// fields <path> <ComponentType>
					int k = arg.LastIndexOf(' ');
					var t = Find(arg.Substring(0, k));
					if (t == null) return "not found";
					string type = arg.Substring(k + 1);
					foreach (var comp in t.GetComponents<Component>())
					{
						if (comp == null || comp.GetIl2CppType().Name != type) continue;
						Fields(comp, sb);
					}
					return sb.ToString();
				}
				case "find":
				{
					int n = 0;
					foreach (var t in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Transform>()))
					{
						var tr = t.Cast<Transform>();
						if (!tr.name.Contains(arg, StringComparison.OrdinalIgnoreCase)) continue;
						sb.AppendLine(PathOf(tr) + (tr.gameObject.activeInHierarchy ? "" : " [off]") + " scene=" + tr.gameObject.scene.name);
						if (++n > 200) break;
					}
					return sb.ToString();
				}
				case "type":
				{
					// every live object with a component of this type
					int n = 0;
					foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<MonoBehaviour>()))
					{
						var mb = o.Cast<MonoBehaviour>();
						if (mb.GetIl2CppType().Name != arg) continue;
						sb.AppendLine(PathOf(mb.transform) + (mb.isActiveAndEnabled ? "" : " [inactive]") + " scene=" + mb.gameObject.scene.name);
						if (++n > 200) break;
					}
					return sb.ToString();
				}
				case "load":
					GameManager.LoadLevel(arg);
					return "loading " + arg;
				case "active":
				{
					int k = arg.LastIndexOf(' ');
					var t = Find(arg.Substring(0, k));
					if (t == null) return "not found";
					t.gameObject.SetActive(arg.Substring(k + 1) == "1");
					return "ok";
				}
				case "state":
					return Runner.Describe();
				default:
					return Runner.DevCommand(cmd, arg) ?? "unknown command " + cmd;
			}
		}

		internal static string PathOf(Transform t)
		{
			var s = t.name;
			for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
			return s;
		}

		internal static Transform Find(string path)
		{
			var go = GameObject.Find(path);
			if (go != null) return go.transform;
			foreach (var t in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Transform>()))
			{
				var tr = t.Cast<Transform>();
				if (PathOf(tr) == path) return tr;
			}
			return null;
		}

		private static void Tree(Transform t, int depth, int max, StringBuilder sb)
		{
			var comps = t.GetComponents<Component>();
			var names = new StringBuilder();
			foreach (var c in comps)
			{
				if (c == null) continue;
				var n = c.GetIl2CppType().Name;
				if (n == "Transform") continue;
				names.Append(n).Append(' ');
			}
			sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [off]").Append("  ").AppendLine(names.ToString());
			if (depth >= max) return;
			for (int i = 0; i < t.childCount; i++) Tree(t.GetChild(i), depth + 1, max, sb);
		}

		private static void Fields(Component comp, StringBuilder sb)
		{
			var flags = Il2CppSystem.Reflection.BindingFlags.Instance | Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic;
			for (var ty = comp.GetIl2CppType(); ty != null && ty.Name != "MonoBehaviour"; ty = ty.BaseType)
			{
				foreach (var f in ty.GetFields(flags))
				{
					string v;
					try
					{
						var val = f.GetValue(comp);
						v = val == null ? "null" : val.ToString();
					}
					catch (Exception e)
					{
						v = "<" + e.GetType().Name + ">";
					}
					sb.AppendLine($"{ty.Name}.{f.Name} : {f.FieldType.Name} = {v}");
				}
			}
		}
	}
}
