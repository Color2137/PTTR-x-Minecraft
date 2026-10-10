package ptrcraft.ptr.unity;

import java.io.IOException;

/** LZ4 block decompression (the format UnityFS uses for LZ4 and LZ4HC blocks). */
public final class Lz4 {
	private Lz4() {
	}

	public static void decompress(byte[] src, int srcOff, int srcLen, byte[] dst, int dstOff, int dstLen) throws IOException {
		int sp = srcOff, srcEnd = srcOff + srcLen;
		int dp = dstOff, dstEnd = dstOff + dstLen;
		while (sp < srcEnd) {
			int token = src[sp++] & 0xFF;
			int lit = token >>> 4;
			if (lit == 15) {
				int b;
				do {
					b = src[sp++] & 0xFF;
					lit += b;
				} while (b == 255);
			}
			if (dp + lit > dstEnd || sp + lit > srcEnd) throw new IOException("LZ4: literal run out of bounds");
			System.arraycopy(src, sp, dst, dp, lit);
			sp += lit;
			dp += lit;
			if (sp >= srcEnd) break;
			int offset = (src[sp] & 0xFF) | ((src[sp + 1] & 0xFF) << 8);
			sp += 2;
			if (offset == 0 || offset > dp - dstOff) throw new IOException("LZ4: bad match offset");
			int len = token & 15;
			if (len == 15) {
				int b;
				do {
					b = src[sp++] & 0xFF;
					len += b;
				} while (b == 255);
			}
			len += 4;
			if (dp + len > dstEnd) throw new IOException("LZ4: match out of bounds");
			int from = dp - offset;
			if (offset >= len) {
				System.arraycopy(dst, from, dst, dp, len);
				dp += len;
			} else {
				for (int i = 0; i < len; i++) dst[dp++] = dst[from++];
			}
		}
		if (dp != dstEnd) throw new IOException("LZ4: decompressed " + (dp - dstOff) + " of " + dstLen + " bytes");
	}
}
