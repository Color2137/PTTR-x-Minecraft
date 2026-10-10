package ptrcraft.ptr.unity;

import java.io.IOException;

/** Random-access bytes: a plain file, or a file inside a bundle that is decompressed on demand. */
public interface ByteSource {
	long length();

	void read(long pos, byte[] dst, int off, int len) throws IOException;

	default byte[] read(long pos, int len) throws IOException {
		byte[] b = new byte[len];
		read(pos, b, 0, len);
		return b;
	}

	static ByteSource of(byte[] data) {
		return new ByteSource() {
			@Override
			public long length() {
				return data.length;
			}

			@Override
			public void read(long pos, byte[] dst, int off, int len) throws IOException {
				if (pos < 0 || pos + len > data.length) throw new IOException("read past end: " + pos + "+" + len + " > " + data.length);
				System.arraycopy(data, (int) pos, dst, off, len);
			}
		};
	}
}
