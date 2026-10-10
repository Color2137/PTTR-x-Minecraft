package ptrcraft.ptr.unity;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Stream;

/**
 * All of a game's Addressables bundles, opened lazily: finds which bundle holds a serialized file
 * (CAB-...), follows PPtrs across files and reads streamed data (.resS / .resource).
 */
public final class AssetEnv implements AutoCloseable {
	public record Ref(SerializedFile file, long pathId) {
		public UObj read() throws IOException {
			return file.read(pathId);
		}

		public String type() {
			return file.typeName(pathId);
		}
	}

	private final Map<String, Path> nodeToBundle = new HashMap<>();
	private final Map<Path, UnityBundle> open = new HashMap<>();
	private final Map<String, SerializedFile> files = new HashMap<>();
	public final int bundleCount;

	public AssetEnv(Path bundleDir) throws IOException {
		int n = 0;
		try (Stream<Path> s = Files.list(bundleDir)) {
			for (Path p : (Iterable<Path>) s.filter(p -> p.toString().endsWith(".bundle")).sorted()::iterator) {
				try (UnityBundle b = new UnityBundle(p)) {
					for (UnityBundle.Node node : b.nodes) nodeToBundle.put(node.name().toLowerCase(), p);
				}
				n++;
			}
		}
		bundleCount = n;
	}

	public int nodeCount() {
		return nodeToBundle.size();
	}

	public synchronized UnityBundle bundle(Path p) throws IOException {
		UnityBundle b = open.get(p);
		if (b == null) {
			b = new UnityBundle(p);
			open.put(p, b);
		}
		return b;
	}

	public Path bundleOf(String nodeName) {
		return nodeToBundle.get(nodeName.toLowerCase());
	}

	/** The serialized file with this node name (e.g. "cab-5a08...", or "cab-x.sharedassets"). */
	public synchronized SerializedFile file(String nodeName) throws IOException {
		String key = nodeName.toLowerCase();
		SerializedFile f = files.get(key);
		if (f != null) return f;
		Path bp = nodeToBundle.get(key);
		if (bp == null) return null;
		UnityBundle b = bundle(bp);
		UnityBundle.Node node = b.node(key);
		f = new SerializedFile(node.name(), b.open(node));
		files.put(key, f);
		return f;
	}

	/** All serialized files (not .resS/.resource) inside one bundle. */
	public List<SerializedFile> filesIn(Path bundlePath) throws IOException {
		UnityBundle b = bundle(bundlePath);
		java.util.ArrayList<SerializedFile> out = new java.util.ArrayList<>();
		for (UnityBundle.Node n : b.nodes) {
			String nm = n.name().toLowerCase();
			if (nm.endsWith(".ress") || nm.endsWith(".resource")) continue;
			out.add(file(nm));
		}
		return out;
	}

	public Ref resolve(SerializedFile from, UObj.Ptr p) throws IOException {
		if (p == null || p.isNull()) return null;
		SerializedFile f;
		if (p.fileId() == 0) f = from;
		else {
			if (p.fileId() - 1 >= from.externals.size()) return null;
			f = file(from.externals.get(p.fileId() - 1));
		}
		if (f == null || !f.objects.containsKey(p.pathId())) return null;
		return new Ref(f, p.pathId());
	}

	public Ref resolve(Ref from, UObj.Ptr p) throws IOException {
		return from == null ? null : resolve(from.file, p);
	}

	/** Bytes from a StreamingInfo {offset, size, path} next to the given file. */
	public byte[] streamed(UObj info) throws IOException {
		String path = info.s("path");
		long off = info.l("offset");
		int size = info.i("size");
		if (path.isEmpty() || size == 0) return null;
		String name = path.substring(path.lastIndexOf('/') + 1).toLowerCase();
		Path bp = nodeToBundle.get(name);
		if (bp == null) throw new IOException("stream file not found: " + path);
		UnityBundle b = bundle(bp);
		return b.open(b.node(name)).read(off, size);
	}

	@Override
	public synchronized void close() throws IOException {
		for (UnityBundle b : open.values()) b.close();
		open.clear();
		files.clear();
	}
}
