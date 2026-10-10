package ptrcraft.ptr.unity;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;

/** One Unity object (or nested struct) read through its type tree. */
public final class UObj extends LinkedHashMap<String, Object> {
	public UObj obj(String k) {
		Object v = get(k);
		return v instanceof UObj o ? o : new UObj();
	}

	public boolean has(String k) {
		return containsKey(k);
	}

	public int i(String k) {
		Object v = get(k);
		if (v instanceof Number n) return n.intValue();
		if (v instanceof Boolean b) return b ? 1 : 0;
		return 0;
	}

	public long l(String k) {
		Object v = get(k);
		return v instanceof Number n ? n.longValue() : 0;
	}

	public float f(String k) {
		Object v = get(k);
		return v instanceof Number n ? n.floatValue() : 0f;
	}

	public float f(String k, float dflt) {
		Object v = get(k);
		return v instanceof Number n ? n.floatValue() : dflt;
	}

	public boolean b(String k) {
		Object v = get(k);
		if (v instanceof Boolean b) return b;
		return v instanceof Number n && n.intValue() != 0;
	}

	public String s(String k) {
		Object v = get(k);
		return v instanceof String s ? s : "";
	}

	public byte[] bytes(String k) {
		Object v = get(k);
		return v instanceof byte[] b ? b : new byte[0];
	}

	@SuppressWarnings("unchecked")
	public List<Object> list(String k) {
		Object v = get(k);
		return v instanceof List<?> l ? (List<Object>) l : Collections.emptyList();
	}

	public List<UObj> objs(String k) {
		List<Object> l = list(k);
		java.util.ArrayList<UObj> out = new java.util.ArrayList<>(l.size());
		for (Object o : l) if (o instanceof UObj u) out.add(u);
		return out;
	}

	/** A PPtr field: {m_FileID, m_PathID}. */
	public Ptr ptr(String k) {
		UObj p = obj(k);
		return new Ptr(p.i("m_FileID"), p.l("m_PathID"));
	}

	public Ptr asPtr() {
		return new Ptr(i("m_FileID"), l("m_PathID"));
	}

	public float[] vec3(String k) {
		UObj v = obj(k);
		return new float[]{v.f("x"), v.f("y"), v.f("z")};
	}

	public float[] quat(String k) {
		UObj v = obj(k);
		return new float[]{v.f("x"), v.f("y"), v.f("z"), v.f("w", 1f)};
	}

	public float[] color(String k) {
		UObj v = obj(k);
		return new float[]{v.f("r", 1f), v.f("g", 1f), v.f("b", 1f), v.f("a", 1f)};
	}

	public record Ptr(int fileId, long pathId) {
		public boolean isNull() {
			return pathId == 0;
		}
	}
}
