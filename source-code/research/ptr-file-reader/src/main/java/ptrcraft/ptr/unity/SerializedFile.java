package ptrcraft.ptr.unity;

import java.io.IOException;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** A Unity serialized file (scene or asset file) with type trees, as stored inside a bundle. */
public final class SerializedFile {
	public record SType(int classId, TypeTree tree) {
	}

	public record ObjInfo(long pathId, long byteStart, long byteSize, int typeIndex) {
	}

	public final String name;
	public final ByteSource data;
	public final int version;
	public String unityVersion = "";
	public final List<SType> types = new ArrayList<>();
	public final Map<Long, ObjInfo> objects = new LinkedHashMap<>();
	/** External file names, lower case, without the archive:/CAB-x/ prefix. Index 0 here = fileID 1. */
	public final List<String> externals = new ArrayList<>();
	private final long dataOffset;
	private final boolean bigEndian;
	private final Map<Long, UObj> cache = new HashMap<>();

	public SerializedFile(String name, ByteSource data) throws IOException {
		this.name = name;
		this.data = data;
		DataReader r = new DataReader(data, true);
		long metadataSize = r.u32();
		long fileSize = r.u32();
		version = r.i32();
		long dOff = r.u32();
		if (version < 17) throw new IOException(name + ": serialized file version " + version + " not supported");
		bigEndian = r.u8() != 0;
		r.skip(3);
		if (version >= 22) {
			metadataSize = r.u32();
			fileSize = r.i64();
			dOff = r.i64();
			r.i64();
		}
		dataOffset = dOff;
		r.bigEndian(bigEndian);
		unityVersion = r.cstr();
		r.i32(); // target platform
		boolean typeTree = r.bool();
		int typeCount = r.i32();
		for (int t = 0; t < typeCount; t++) types.add(readType(r, typeTree, false));
		int objCount = r.i32();
		for (int o = 0; o < objCount; o++) {
			r.align(4);
			long pathId = r.i64();
			long start = version >= 22 ? r.i64() : r.u32();
			long size = r.u32();
			int typeIndex = r.i32();
			objects.put(pathId, new ObjInfo(pathId, start, size, typeIndex));
		}
		int scriptCount = r.i32();
		for (int s = 0; s < scriptCount; s++) {
			r.i32();
			r.align(4);
			r.i64();
		}
		int extCount = r.i32();
		for (int e = 0; e < extCount; e++) {
			r.cstr();
			r.skip(16);
			r.i32();
			String p = r.cstr();
			externals.add(p.substring(p.lastIndexOf('/') + 1).toLowerCase());
		}
	}

	private SType readType(DataReader r, boolean typeTree, boolean refType) throws IOException {
		int classId = r.i32();
		r.bool(); // stripped
		int scriptTypeIndex = r.i16();
		if (refType || classId == 114) r.skip(16);
		r.skip(16);
		TypeTree tree = null;
		if (typeTree) {
			tree = TypeTree.readBlob(r, version);
			if (version >= 21) {
				if (refType) {
					r.cstr();
					r.cstr();
					r.cstr();
				} else {
					int deps = r.i32();
					r.skip(4L * deps);
				}
			}
		}
		return new SType(classId, tree);
	}

	public int classId(long pathId) {
		ObjInfo o = objects.get(pathId);
		return o == null ? -1 : types.get(o.typeIndex).classId;
	}

	public String typeName(long pathId) {
		ObjInfo o = objects.get(pathId);
		if (o == null) return null;
		TypeTree t = types.get(o.typeIndex).tree;
		return t == null || t.nodes.isEmpty() ? "class" + types.get(o.typeIndex).classId : t.nodes.get(0).type();
	}

	public synchronized UObj read(long pathId) throws IOException {
		UObj c = cache.get(pathId);
		if (c != null) return c;
		ObjInfo o = objects.get(pathId);
		if (o == null) return null;
		TypeTree t = types.get(o.typeIndex).tree;
		if (t == null) throw new IOException(name + ": no type tree for object " + pathId);
		DataReader r = new DataReader(data, dataOffset + o.byteStart, o.byteSize, bigEndian);
		UObj v = t.read(r);
		// big objects (meshes, textures) are not kept; they are read once
		if (o.byteSize < 64 * 1024) cache.put(pathId, v);
		return v;
	}
}
