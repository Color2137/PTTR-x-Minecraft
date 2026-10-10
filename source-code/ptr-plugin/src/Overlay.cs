using UnityEngine;
using UnityEngine.UI;

namespace PTRCraft
{
	/// <summary>
	/// Minecraft's hand, held item, hotbar, hearts and screens, captured by the Minecraft mod with a
	/// transparent background, drawn over PTR's frame.
	/// </summary>
	internal static class Overlay
	{
		private static GameObject root;
		private static RawImage image;
		private static Texture2D tex;
		public static int Width, Height;
		public static long Frames;

		private static void Ensure()
		{
			if (root != null) return;
			root = new GameObject("PTRCraftOverlay");
			Object.DontDestroyOnLoad(root);
			var canvas = root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 30000;
			var imgGo = new GameObject("Minecraft");
			imgGo.transform.SetParent(root.transform, false);
			image = imgGo.AddComponent<RawImage>();
			image.raycastTarget = false;
			var rt = image.rectTransform;
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
			image.enabled = false;
		}

		public static void Show(bool show)
		{
			if (root == null && !show) return;
			Ensure();
			if (!show) image.enabled = false;
		}

		public static void Pump(Link link)
		{
			int slot = link.AcquireOverlay(out int w, out int h, out bool bottomUp);
			if (slot < 0 || w <= 0 || h <= 0 || w > Proto.MaxOverlayW || h > Proto.MaxOverlayH) return;
			Ensure();
			if (tex == null || tex.width != w || tex.height != h)
			{
				if (tex != null) Object.Destroy(tex);
				tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
				tex.filterMode = FilterMode.Point;
				tex.wrapMode = TextureWrapMode.Clamp;
				image.texture = tex;
				Width = w;
				Height = h;
			}
			tex.LoadRawTextureData(link.OverlayPixels(slot), w * h * 4);
			tex.Apply(false);
			// Unity's texture rows are bottom-up; flip top-down frames.
			image.uvRect = bottomUp ? new Rect(0, 0, 1, 1) : new Rect(0, 1, 1, -1);
			image.enabled = true;
			Frames++;
		}
	}
}
