package ptrcraft.ptr.unity;

import java.io.IOException;
import java.io.RandomAccessFile;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * A UnityFS asset bundle opened read-only. Only the directory is read up front; the files inside
 * (serialized files, .resS, .resource) are decompressed one block at a time when read.
 */
public final class UnityBundle implements AutoCloseable {
	public record Node(String name, long offset, long size, int flags) {
	}

	private record Block(long uncompressedStart, int uncompressedSize, long compressedStart, int compressedSize, int flags) {
	}

	public final Path path;
	public final String unityRevision;
	public final List<Node> nodes = new ArrayList<>();
	private final List<Block> blocks = new ArrayList<>();
	private final RandomAccessFile file;
	private long totalUncompressed;
	// small LRU of decompressed blocks (128 KB each in PTR's bundles)
	private final LinkedHashMap<Integer, byte[]> cache = new LinkedHashMap<>(32, 0.75f, true) {
		@Override
		protected boolean removeEldestEntry(Map.Entry<Integer, byte[]> e) {
			return size() > 48;
		}
	};

	public UnityBundle(Path path) throws IOException {
		this.path = path;
		this.file = new RandomAccessFile(path.toFile(), "r");
		ByteSource raw = new ByteSource() {
			@Override
			public long length() {
				try {
					return file.length();
				} catch (IOException e) {
					return 0;
				}
			}

			@Override
			public void read(long pos, byte[] dst, int off, int len) throws IOException {
				synchronized (file) {
					file.seek(pos);
					file.readFully(dst, off, len);
				}
			}
		};
		DataReader r = new DataReader(raw, true);
		String sig = r.cstr();
		if (!sig.equals("UnityFS")) throw new IOException(path.getFileName() + ": not a UnityFS bundle (" + sig + ")");
		int version = r.i32();
		r.cstr(); // player version
		unityRevision = r.cstr();
		r.i64(); // file size
		int compressedInfo = r.i32();
		int uncompressedInfo = r.i32();
		int flags = r.i32();
		if (version >= 7) r.align(16);
		byte[] info;
		long afterHeader = r.position();
		if ((flags & 0x80) != 0) {
			info = raw.read(raw.length() - compressedInfo, compressedInfo);
		} else {
			info = r.bytes(compressedInfo);
		}
		info = decompress(info, uncompressedInfo, flags & 0x3F);
		DataReader ir = new DataReader(ByteSource.of(info), true);
		ir.skip(16);
		int blockCount = ir.i32();
		long dataStart = (flags & 0x80) != 0 ? afterHeader : r.position();
		if ((flags & 0x200) != 0) dataStart = (dataStart + 15) & ~15L;
		long cpos = dataStart, upos = 0;
		for (int i = 0; i < blockCount; i++) {
			int us = ir.i32(), cs = ir.i32(), bf = ir.u16();
			blocks.add(new Block(upos, us, cpos, cs, bf));
			upos += us & 0xFFFFFFFFL;
			cpos += cs & 0xFFFFFFFFL;
		}
		totalUncompressed = upos;
		int nodeCount = ir.i32();
		for (int i = 0; i < nodeCount; i++) {
			long off = ir.i64(), size = ir.i64();
			int nf = ir.i32();
			nodes.add(new Node(ir.cstr(), off, size, nf));
		}
	}

	private static byte[] decompress(byte[] data, int uncompressedSize, int kind) throws IOException {
		switch (kind) {
			case 0:
				return data;
			case 2:
			case 3: {
				byte[] out = new byte[uncompressedSize];
				Lz4.decompress(data, 0, data.length, out, 0, uncompressedSize);
				return out;
			}
			default:
				throw new IOException("unsupported bundle compression " + kind + " (LZMA bundles are not used by Paint the Town Red)");
		}
	}

	private byte[] block(int index) throws IOException {
		synchronized (cache) {
			byte[] b = cache.get(index);
			if (b != null) return b;
		}
		Block bl = blocks.get(index);
		byte[] comp = new byte[bl.compressedSize];
		synchronized (file) {
			file.seek(bl.compressedStart);
			file.readFully(comp);
		}
		byte[] out = decompress(comp, bl.uncompressedSize, bl.flags & 0x3F);
		synchronized (cache) {
			cache.put(index, out);
		}
		return out;
	}

	private int blockAt(long upos) {
		int lo = 0, hi = blocks.size() - 1;
		while (lo < hi) {
			int mid = (lo + hi + 1) >>> 1;
			if (blocks.get(mid).uncompressedStart <= upos) lo = mid;
			else hi = mid - 1;
		}
		return lo;
	}

	/** Reads from the bundle's uncompressed stream. */
	void readStream(long pos, byte[] dst, int off, int len) throws IOException {
		if (pos < 0 || pos + len > totalUncompressed) throw new IOException("bundle read out of range");
		while (len > 0) {
			int bi = blockAt(pos);
			Block bl = blocks.get(bi);
			byte[] data = block(bi);
			int inBlock = (int) (pos - bl.uncompressedStart);
			int n = Math.min(len, bl.uncompressedSize - inBlock);
			System.arraycopy(data, inBlock, dst, off, n);
			pos += n;
			off += n;
			len -= n;
		}
	}

	public Node node(String name) {
		for (Node n : nodes) if (n.name.equalsIgnoreCase(name)) return n;
		return null;
	}

	public ByteSource open(Node n) {
		return new ByteSource() {
			@Override
			public long length() {
				return n.size;
			}

			@Override
			public void read(long pos, byte[] dst, int off, int len) throws IOException {
				if (pos < 0 || pos + len > n.size) throw new IOException(n.name + ": read out of range");
				readStream(n.offset + pos, dst, off, len);
			}
		};
	}

	@Override
	public void close() throws IOException {
		file.close();
	}
}
