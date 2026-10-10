package ptrcraft.ptr.unity;

import java.io.IOException;

/** Decodes a Unity Texture2D (top mip) to RGBA, rows top-down. */
public final class TextureDecoder {
	public record Image(String name, int width, int height, int[] argb) {
	}

	private TextureDecoder() {
	}

	public static Image decode(AssetEnv env, UObj tex) throws IOException {
		int w = tex.i("m_Width"), h = tex.i("m_Height"), fmt = tex.i("m_TextureFormat");
		byte[] data = tex.bytes("image data");
		if (data.length == 0) {
			byte[] s = env.streamed(tex.obj("m_StreamData"));
			if (s != null) data = s;
		}
		if (w <= 0 || h <= 0 || data.length == 0) throw new IOException(tex.s("m_Name") + ": no image data");
		int[] px = new int[w * h]; // bottom-up while decoding, flipped at the end
		switch (fmt) {
			case 1 -> { // Alpha8
				for (int i = 0; i < w * h; i++) px[i] = ((data[i] & 0xFF) << 24) | 0xFFFFFF;
			}
			case 3 -> { // RGB24
				for (int i = 0; i < w * h; i++) px[i] = 0xFF000000 | ((data[3 * i] & 0xFF) << 16) | ((data[3 * i + 1] & 0xFF) << 8) | (data[3 * i + 2] & 0xFF);
			}
			case 4 -> { // RGBA32
				for (int i = 0; i < w * h; i++) px[i] = ((data[4 * i + 3] & 0xFF) << 24) | ((data[4 * i] & 0xFF) << 16) | ((data[4 * i + 1] & 0xFF) << 8) | (data[4 * i + 2] & 0xFF);
			}
			case 5 -> { // ARGB32
				for (int i = 0; i < w * h; i++) px[i] = ((data[4 * i] & 0xFF) << 24) | ((data[4 * i + 1] & 0xFF) << 16) | ((data[4 * i + 2] & 0xFF) << 8) | (data[4 * i + 3] & 0xFF);
			}
			case 14 -> { // BGRA32
				for (int i = 0; i < w * h; i++) px[i] = ((data[4 * i + 3] & 0xFF) << 24) | ((data[4 * i + 2] & 0xFF) << 16) | ((data[4 * i + 1] & 0xFF) << 8) | (data[4 * i] & 0xFF);
			}
			case 7 -> { // RGB565
				for (int i = 0; i < w * h; i++) px[i] = 0xFF000000 | rgb565((data[2 * i] & 0xFF) | ((data[2 * i + 1] & 0xFF) << 8));
			}
			case 13 -> { // RGBA4444
				for (int i = 0; i < w * h; i++) {
					int v = (data[2 * i] & 0xFF) | ((data[2 * i + 1] & 0xFF) << 8);
					int r = (v >> 12) & 15, g = (v >> 8) & 15, b = (v >> 4) & 15, a = v & 15;
					px[i] = (a * 17 << 24) | (r * 17 << 16) | (g * 17 << 8) | (b * 17);
				}
			}
			case 10 -> blocks(data, w, h, px, false);
			case 12 -> blocks(data, w, h, px, true);
			default -> throw new IOException(tex.s("m_Name") + ": texture format " + fmt + " not supported");
		}
		int[] out = new int[w * h];
		for (int y = 0; y < h; y++) System.arraycopy(px, y * w, out, (h - 1 - y) * w, w);
		return new Image(tex.s("m_Name"), w, h, out);
	}

	private static int rgb565(int c) {
		int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
		return ((r << 3 | r >> 2) << 16) | ((g << 2 | g >> 4) << 8) | (b << 3 | b >> 2);
	}

	private static void blocks(byte[] d, int w, int h, int[] px, boolean dxt5) throws IOException {
		int bw = (w + 3) / 4, bh = (h + 3) / 4, bs = dxt5 ? 16 : 8;
		if (d.length < bw * bh * bs) throw new IOException("DXT data too short");
		int[] col = new int[4];
		int[] alpha = new int[8];
		for (int by = 0; by < bh; by++) {
			for (int bx = 0; bx < bw; bx++) {
				int o = (by * bw + bx) * bs;
				long aBits = 0;
				if (dxt5) {
					int a0 = d[o] & 0xFF, a1 = d[o + 1] & 0xFF;
					alpha[0] = a0;
					alpha[1] = a1;
					if (a0 > a1) for (int k = 1; k < 7; k++) alpha[k + 1] = ((7 - k) * a0 + k * a1) / 7;
					else {
						for (int k = 1; k < 5; k++) alpha[k + 1] = ((5 - k) * a0 + k * a1) / 5;
						alpha[6] = 0;
						alpha[7] = 255;
					}
					for (int k = 0; k < 6; k++) aBits |= (long) (d[o + 2 + k] & 0xFF) << (8 * k);
					o += 8;
				}
				int c0 = (d[o] & 0xFF) | ((d[o + 1] & 0xFF) << 8);
				int c1 = (d[o + 2] & 0xFF) | ((d[o + 3] & 0xFF) << 8);
				int p0 = rgb565(c0), p1 = rgb565(c1);
				col[0] = 0xFF000000 | p0;
				col[1] = 0xFF000000 | p1;
				if (c0 > c1 || dxt5) {
					col[2] = 0xFF000000 | mix(p0, p1, 2, 1, 3);
					col[3] = 0xFF000000 | mix(p0, p1, 1, 2, 3);
				} else {
					col[2] = 0xFF000000 | mix(p0, p1, 1, 1, 2);
					col[3] = 0; // transparent black
				}
				int idx = (d[o + 4] & 0xFF) | ((d[o + 5] & 0xFF) << 8) | ((d[o + 6] & 0xFF) << 16) | ((d[o + 7] & 0xFF) << 24);
				for (int y = 0; y < 4; y++) {
					int py = by * 4 + y;
					if (py >= h) break;
					for (int x = 0; x < 4; x++) {
						int pxx = bx * 4 + x;
						if (pxx >= w) continue;
						int k = y * 4 + x;
						int c = col[(idx >>> (2 * k)) & 3];
						if (dxt5) c = (c & 0xFFFFFF) | (alpha[(int) ((aBits >>> (3 * k)) & 7)] << 24);
						px[py * w + pxx] = c;
					}
				}
			}
		}
	}

	private static int mix(int a, int b, int wa, int wb, int div) {
		int r = (((a >> 16) & 255) * wa + ((b >> 16) & 255) * wb) / div;
		int g = (((a >> 8) & 255) * wa + ((b >> 8) & 255) * wb) / div;
		int bl = ((a & 255) * wa + (b & 255) * wb) / div;
		return (r << 16) | (g << 8) | bl;
	}
}
