package ptrcraft.ptr.unity;

import java.io.IOException;
import java.nio.charset.StandardCharsets;

/** Sequential reader over a {@link ByteSource} with switchable byte order, buffered in 64 KB windows. */
public final class DataReader {
	private final ByteSource src;
	private final long base;
	private final long limit;
	private long pos;
	private boolean bigEndian;
	private final byte[] buf = new byte[1 << 16];
	private long bufStart = -1;
	private int bufLen;

	public DataReader(ByteSource src, long base, long length, boolean bigEndian) {
		this.src = src;
		this.base = base;
		this.limit = length;
		this.bigEndian = bigEndian;
	}

	public DataReader(ByteSource src, boolean bigEndian) {
		this(src, 0, src.length(), bigEndian);
	}

	public long position() {
		return pos;
	}

	public void seek(long p) {
		pos = p;
	}

	public void skip(long n) {
		pos += n;
	}

	public long length() {
		return limit;
	}

	public void bigEndian(boolean b) {
		bigEndian = b;
	}

	public void align(int n) {
		long r = pos % n;
		if (r != 0) pos += n - r;
	}

	private int byteAt(long p) throws IOException {
		if (p < 0 || p >= limit) throw new IOException("read past end at " + p + " (length " + limit + ")");
		if (p < bufStart || p >= bufStart + bufLen) {
			bufStart = p;
			bufLen = (int) Math.min(buf.length, limit - p);
			src.read(base + p, buf, 0, bufLen);
		}
		return buf[(int) (p - bufStart)] & 0xFF;
	}

	public int u8() throws IOException {
		return byteAt(pos++);
	}

	public boolean bool() throws IOException {
		return u8() != 0;
	}

	public void bytes(byte[] dst, int off, int len) throws IOException {
		if (pos + len > limit) throw new IOException("read past end: " + pos + "+" + len + " > " + limit);
		if (len > 4096) {
			src.read(base + pos, dst, off, len);
		} else {
			for (int i = 0; i < len; i++) dst[off + i] = (byte) byteAt(pos + i);
		}
		pos += len;
	}

	public byte[] bytes(int len) throws IOException {
		byte[] b = new byte[len];
		bytes(b, 0, len);
		return b;
	}

	public int u16() throws IOException {
		int a = u8(), b = u8();
		return bigEndian ? (a << 8) | b : (b << 8) | a;
	}

	public short i16() throws IOException {
		return (short) u16();
	}

	public int i32() throws IOException {
		int a = u8(), b = u8(), c = u8(), d = u8();
		return bigEndian ? (a << 24) | (b << 16) | (c << 8) | d : (d << 24) | (c << 16) | (b << 8) | a;
	}

	public long u32() throws IOException {
		return i32() & 0xFFFFFFFFL;
	}

	public long i64() throws IOException {
		long a = u32(), b = u32();
		return bigEndian ? (a << 32) | b : (b << 32) | a;
	}

	public float f32() throws IOException {
		return Float.intBitsToFloat(i32());
	}

	public double f64() throws IOException {
		return Double.longBitsToDouble(i64());
	}

	/** Null-terminated UTF-8 string. */
	public String cstr() throws IOException {
		java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
		int c;
		while ((c = u8()) != 0) out.write(c);
		return out.toString(StandardCharsets.UTF_8);
	}

	/** int32 length, bytes, align 4 (Unity's serialized string). */
	public String alignedString() throws IOException {
		int n = i32();
		if (n < 0 || n > limit - pos) throw new IOException("bad string length " + n);
		String s = new String(bytes(n), StandardCharsets.UTF_8);
		align(4);
		return s;
	}
}
