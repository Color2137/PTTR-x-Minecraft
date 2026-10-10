using System;
using System.Runtime.InteropServices;

namespace PTRCraft
{
	internal static class Native
	{
		[DllImport("kernel32")] public static extern ulong GetTickCount64();
		[DllImport("kernel32")] public static extern bool QueryPerformanceCounter(out long count);
		[DllImport("kernel32")] public static extern bool QueryPerformanceFrequency(out long freq);
		[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr OpenMutexW(uint access, bool inherit, string name);
		[DllImport("kernel32")] public static extern bool CloseHandle(IntPtr h);
		[DllImport("user32")] public static extern IntPtr GetForegroundWindow();
		[DllImport("user32")] public static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

		public const uint SYNCHRONIZE = 0x00100000;

		/// <summary>True when a window of this process (PTR) has keyboard focus.</summary>
		public static bool PtrHasFocus()
		{
			var w = GetForegroundWindow();
			if (w == IntPtr.Zero) return false;
			GetWindowThreadProcessId(w, out int pid);
			return pid == Environment.ProcessId;
		}

		public static bool MutexExists(string name)
		{
			var h = OpenMutexW(SYNCHRONIZE, false, name);
			if (h == IntPtr.Zero) return false;
			CloseHandle(h);
			return true;
		}

		public static long Qpc()
		{
			QueryPerformanceCounter(out long c);
			return c;
		}
	}
}
