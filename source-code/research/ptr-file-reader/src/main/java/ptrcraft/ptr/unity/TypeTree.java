package ptrcraft.ptr.unity;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

/** A serialized type tree, and the generic reader that turns object bytes into {@link UObj} maps. */
public final class TypeTree {
	public record Node(String type, String name, int level, int byteSize, int metaFlag, int typeFlags) {
		boolean align() {
			return (metaFlag & 0x4000) != 0;
		}
	}

	static final String[] COMMON = ("AABB|AnimationClip|AnimationCurve|AnimationState|Array|Base|BitField|bitset|bool|char|ColorRGBA|Component|data|deque|double|dynamic_array|FastPropertyName|first|float|Font|GameObject|Generic Mono|GradientNEW|GUID|GUIStyle|int|list|long long|map|Matrix4x4f|MdFour|MonoBehaviour|MonoScript|m_ByteSize|m_Curve|m_EditorClassIdentifier|m_EditorHideFlags|m_Enabled|m_ExtensionPtr|m_GameObject|m_Index|m_IsArray|m_IsStatic|m_MetaFlag|m_Name|m_ObjectHideFlags|m_PrefabInternal|m_PrefabParentObject|m_Script|m_StaticEditorFlags|m_Type|m_Version|Object|pair|PPtr<Component>|PPtr<GameObject>|PPtr<Material>|PPtr<MonoBehaviour>|PPtr<MonoScript>|PPtr<Object>|PPtr<Prefab>|PPtr<Sprite>|PPtr<TextAsset>|PPtr<Texture>|PPtr<Texture2D>|PPtr<Transform>|Prefab|Quaternionf|Rectf|RectInt|RectOffset|second|set|short|size|SInt16|SInt32|SInt64|SInt8|staticvector|string|TextAsset|TextMesh|Texture|Texture2D|Transform|TypelessData|UInt16|UInt32|UInt64|UInt8|unsigned int|unsigned long long|unsigned short|vector|Vector2f|Vector3f|Vector4f|m_ScriptingClassIdentifier|Gradient|Type*|int2_storage|int3_storage|BoundsInt|m_CorrespondingSourceObject|m_PrefabInstance|m_PrefabAsset|FileSize|Hash128|RenderingLayerMask|fixed_array|EntityId|LoadableObjectId|LoadableSceneId").split("\\|");
	private static final java.util.Map<Integer, String> COMMON_BY_OFFSET = new java.util.HashMap<>();

	static {
		int off = 0;
		for (String s : COMMON) {
			COMMON_BY_OFFSET.put(off, s);
			off += s.getBytes(java.nio.charset.StandardCharsets.UTF_8).length + 1;
		}
	}

	public final List<Node> nodes;
	/** Index of each node's last descendant + 1, so subtrees can be skipped in O(1). */
	private final int[] end;

	TypeTree(List<Node> nodes) {
		this.nodes = nodes;
		end = new int[nodes.size()];
		for (int i = 0; i < nodes.size(); i++) {
			int j = i + 1;
			while (j < nodes.size() && nodes.get(j).level > nodes.get(i).level) j++;
			end[i] = j;
		}
	}

	/** Reads the blob form (serialized file version 12+). */
	static TypeTree readBlob(DataReader r, int fileVersion) throws IOException {
		int count = r.i32();
		int strSize = r.i32();
		int nodeSize = fileVersion >= 19 ? 32 : 24;
		byte[] raw = r.bytes(count * nodeSize);
		byte[] strings = r.bytes(strSize);
		DataReader nr = new DataReader(ByteSource.of(raw), false);
		List<Node> nodes = new ArrayList<>(count);
		for (int i = 0; i < count; i++) {
			nr.u16(); // version
			int level = nr.u8();
			int typeFlags = nr.u8();
			int typeOff = nr.i32();
			int nameOff = nr.i32();
			int byteSize = nr.i32();
			nr.i32(); // index
			int meta = nr.i32();
			if (nodeSize == 32) nr.i64();
			nodes.add(new Node(str(strings, typeOff), str(strings, nameOff), level, byteSize, meta, typeFlags));
		}
		return new TypeTree(nodes);
	}

	private static String str(byte[] local, int off) {
		if ((off & 0x80000000) != 0) {
			String s = COMMON_BY_OFFSET.get(off & 0x7FFFFFFF);
			return s != null ? s : "?" + (off & 0x7FFFFFFF);
		}
		int e = off;
		while (e < local.length && local[e] != 0) e++;
		return new String(local, off, e - off, java.nio.charset.StandardCharsets.UTF_8);
	}

	public UObj read(DataReader r) throws IOException {
		Object v = value(r, 0);
		return v instanceof UObj o ? o : new UObj();
	}

	private Object value(DataReader r, int i) throws IOException {
		Node n = nodes.get(i);
		boolean align = n.align();
		Object v;
		switch (n.type) {
			case "SInt8" -> v = (int) (byte) r.u8();
			case "UInt8", "char" -> v = r.u8();
			case "short", "SInt16" -> v = (int) r.i16();
			case "UInt16", "unsigned short" -> v = r.u16();
			case "int", "SInt32" -> v = r.i32();
			case "unsigned int", "UInt32", "Type*" -> v = r.u32();
			case "long long", "SInt64" -> v = r.i64();
			case "unsigned long long", "UInt64", "FileSize" -> v = r.i64();
			case "float" -> v = r.f32();
			case "double" -> v = r.f64();
			case "bool" -> v = r.bool();
			case "string" -> {
				v = r.alignedString();
				// the string's own Array child carries no extra alignment beyond the string's
			}
			case "TypelessData" -> {
				int size = r.i32();
				v = r.bytes(size);
			}
			case "map" -> {
				// map > Array > size, data(pair > first, second)
				int arr = i + 1;
				if (nodes.get(arr).align()) align = true;
				int pair = arr + 2;
				int first = pair + 1;
				int second = end[first];
				int size = r.i32();
				List<Object[]> list = new ArrayList<>(Math.max(0, Math.min(size, 1 << 16)));
				for (int k = 0; k < size; k++) list.add(new Object[]{value(r, first), value(r, second)});
				v = list;
			}
			default -> {
				if (i + 1 < nodes.size() && nodes.get(i + 1).type.equals("Array") && nodes.get(i + 1).level == n.level + 1) {
					Node arr = nodes.get(i + 1);
					if (arr.align()) align = true;
					int elem = i + 3; // Array > size, data
					v = array(r, elem);
				} else {
					UObj o = new UObj();
					int j = i + 1, stop = end[i];
					while (j < stop) {
						o.put(nodes.get(j).name, value(r, j));
						j = end[j];
					}
					v = o;
				}
			}
		}
		if (align) r.align(4);
		return v;
	}

	private Object array(DataReader r, int elem) throws IOException {
		int size = r.i32();
		if (size < 0 || size > r.length() - r.position() + 16) throw new IOException("bad array size " + size);
		Node e = nodes.get(elem);
		boolean leaf = end[elem] == elem + 1;
		if (leaf && !e.align()) {
			switch (e.type) {
				case "UInt8", "SInt8", "char": return r.bytes(size);
				case "float": {
					float[] a = new float[size];
					for (int k = 0; k < size; k++) a[k] = r.f32();
					return a;
				}
				case "int", "SInt32", "unsigned int", "UInt32": {
					int[] a = new int[size];
					for (int k = 0; k < size; k++) a[k] = r.i32();
					return a;
				}
				case "UInt16", "unsigned short", "SInt16", "short": {
					int[] a = new int[size];
					for (int k = 0; k < size; k++) a[k] = e.type.startsWith("S") || e.type.equals("short") ? r.i16() : r.u16();
					return a;
				}
				default:
			}
		}
		List<Object> list = new ArrayList<>(Math.min(size, 1 << 16));
		for (int k = 0; k < size; k++) list.add(value(r, elem));
		return list;
	}
}
