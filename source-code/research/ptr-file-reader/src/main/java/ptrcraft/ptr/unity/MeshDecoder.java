package ptrcraft.ptr.unity;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.List;

/** Decodes an uncompressed Unity 2019+ Mesh: positions, normals, uv0 and triangle lists per submesh. */
public final class MeshDecoder {
	public static final class Mesh {
		public String name;
		public int vertexCount;
		public float[] pos;
		public float[] normal; // may be null
		public float[] uv; // may be null
		public final List<int[]> submeshes = new ArrayList<>(); // triangle vertex indices
	}

	private MeshDecoder() {
	}

	private static int formatSize(int f) {
		return switch (f) {
			case 0, 10, 11 -> 4; // Float, UInt32, SInt32
			case 1, 4, 5, 8, 9 -> 2; // Float16, UNorm16, SNorm16, UInt16, SInt16
			default -> 1; // UNorm8, SNorm8, UInt8, SInt8
		};
	}

	public static Mesh decode(AssetEnv env, UObj m) throws IOException {
		if (m.i("m_MeshCompression") != 0) throw new IOException(m.s("m_Name") + ": compressed meshes are not supported");
		Mesh out = new Mesh();
		out.name = m.s("m_Name");
		UObj vd = m.obj("m_VertexData");
		int vc = vd.i("m_VertexCount");
		out.vertexCount = vc;
		byte[] data = vd.bytes("m_DataSize");
		if (data.length == 0) {
			byte[] s = env.streamed(m.obj("m_StreamData"));
			if (s != null) data = s;
		}
		List<UObj> ch = vd.objs("m_Channels");
		int streams = 0;
		for (UObj c : ch) if ((c.i("dimension") & 15) > 0) streams = Math.max(streams, c.i("stream") + 1);
		long[] streamOff = new long[streams];
		int[] stride = new int[streams];
		long off = 0;
		for (int s = 0; s < streams; s++) {
			int st = 0;
			for (UObj c : ch) if (c.i("stream") == s && (c.i("dimension") & 15) > 0) st += (c.i("dimension") & 15) * formatSize(c.i("format"));
			streamOff[s] = off;
			stride[s] = st;
			off += (long) vc * st;
			off = (off + 15) & ~15L;
		}
		ByteBuffer bb = ByteBuffer.wrap(data).order(ByteOrder.LITTLE_ENDIAN);
		if (ch.size() > 0) out.pos = channel(bb, ch.get(0), vc, streamOff, stride, 3);
		if (ch.size() > 1 && (ch.get(1).i("dimension") & 15) > 0) out.normal = channel(bb, ch.get(1), vc, streamOff, stride, 3);
		if (ch.size() > 4 && (ch.get(4).i("dimension") & 15) > 0) out.uv = channel(bb, ch.get(4), vc, streamOff, stride, 2);
		if (out.pos == null) throw new IOException(out.name + ": no positions");

		byte[] ib = m.bytes("m_IndexBuffer");
		boolean u32 = m.i("m_IndexFormat") == 1;
		ByteBuffer ibb = ByteBuffer.wrap(ib).order(ByteOrder.LITTLE_ENDIAN);
		for (UObj sm : m.objs("m_SubMeshes")) {
			int first = sm.i("firstByte"), count = sm.i("indexCount"), base = sm.i("baseVertex");
			if (sm.i("topology") != 0) {
				out.submeshes.add(new int[0]);
				continue;
			}
			int[] tri = new int[count - count % 3];
			for (int k = 0; k < tri.length; k++) {
				int idx = u32 ? ibb.getInt(first + 4 * k) : (ibb.getShort(first + 2 * k) & 0xFFFF);
				tri[k] = idx + base;
				if (tri[k] >= vc) throw new IOException(out.name + ": index " + tri[k] + " >= " + vc);
			}
			out.submeshes.add(tri);
		}
		return out;
	}

	private static float[] channel(ByteBuffer bb, UObj c, int vc, long[] streamOff, int[] stride, int want) throws IOException {
		int s = c.i("stream"), fmt = c.i("format"), dim = c.i("dimension") & 15, coff = c.i("offset");
		if (dim == 0) return null;
		int fs = formatSize(fmt);
		float[] out = new float[vc * want];
		for (int v = 0; v < vc; v++) {
			long base = streamOff[s] + (long) v * stride[s] + coff;
			if (base + (long) dim * fs > bb.capacity()) throw new IOException("vertex data out of range");
			for (int k = 0; k < Math.min(dim, want); k++) {
				int p = (int) base + k * fs;
				out[v * want + k] = switch (fmt) {
					case 0 -> bb.getFloat(p);
					case 1 -> Float.float16ToFloat(bb.getShort(p));
					case 2 -> (bb.get(p) & 0xFF) / 255f;
					case 3 -> Math.max(bb.get(p) / 127f, -1f);
					case 4 -> (bb.getShort(p) & 0xFFFF) / 65535f;
					case 5 -> Math.max(bb.getShort(p) / 32767f, -1f);
					default -> 0f;
				};
			}
		}
		return out;
	}
}
