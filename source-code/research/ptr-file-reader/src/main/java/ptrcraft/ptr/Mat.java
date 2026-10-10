package ptrcraft.ptr;

/** Column-major 4x4 matrices as float[16], the minimum the extractor needs. */
public final class Mat {
	private Mat() {
	}

	public static float[] identity() {
		return new float[]{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
	}

	/** Translation * rotation (quaternion x,y,z,w) * scale. */
	public static float[] trs(float[] t, float[] q, float[] s) {
		float x = q[0], y = q[1], z = q[2], w = q[3];
		float n = (float) Math.sqrt(x * x + y * y + z * z + w * w);
		if (n > 0) {
			x /= n;
			y /= n;
			z /= n;
			w /= n;
		}
		float[] m = new float[16];
		m[0] = (1 - 2 * (y * y + z * z)) * s[0];
		m[1] = (2 * (x * y + z * w)) * s[0];
		m[2] = (2 * (x * z - y * w)) * s[0];
		m[4] = (2 * (x * y - z * w)) * s[1];
		m[5] = (1 - 2 * (x * x + z * z)) * s[1];
		m[6] = (2 * (y * z + x * w)) * s[1];
		m[8] = (2 * (x * z + y * w)) * s[2];
		m[9] = (2 * (y * z - x * w)) * s[2];
		m[10] = (1 - 2 * (x * x + y * y)) * s[2];
		m[12] = t[0];
		m[13] = t[1];
		m[14] = t[2];
		m[15] = 1;
		return m;
	}

	public static float[] mul(float[] a, float[] b) {
		float[] r = new float[16];
		for (int c = 0; c < 4; c++) {
			for (int rr = 0; rr < 4; rr++) {
				r[c * 4 + rr] = a[rr] * b[c * 4] + a[4 + rr] * b[c * 4 + 1] + a[8 + rr] * b[c * 4 + 2] + a[12 + rr] * b[c * 4 + 3];
			}
		}
		return r;
	}

	public static void point(float[] m, float x, float y, float z, float[] out, int o) {
		out[o] = m[0] * x + m[4] * y + m[8] * z + m[12];
		out[o + 1] = m[1] * x + m[5] * y + m[9] * z + m[13];
		out[o + 2] = m[2] * x + m[6] * y + m[10] * z + m[14];
	}

	public static void dir(float[] m, float x, float y, float z, float[] out, int o) {
		out[o] = m[0] * x + m[4] * y + m[8] * z;
		out[o + 1] = m[1] * x + m[5] * y + m[9] * z;
		out[o + 2] = m[2] * x + m[6] * y + m[10] * z;
	}

	/** Inverse of an affine matrix (rotation/scale/translation). */
	public static float[] invertAffine(float[] m) {
		float a = m[0], b = m[4], c = m[8], d = m[1], e = m[5], f = m[9], g = m[2], h = m[6], i = m[10];
		float det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
		if (Math.abs(det) < 1e-12f) return identity();
		float id = 1 / det;
		float[] r = new float[16];
		r[0] = (e * i - f * h) * id;
		r[4] = (c * h - b * i) * id;
		r[8] = (b * f - c * e) * id;
		r[1] = (f * g - d * i) * id;
		r[5] = (a * i - c * g) * id;
		r[9] = (c * d - a * f) * id;
		r[2] = (d * h - e * g) * id;
		r[6] = (b * g - a * h) * id;
		r[10] = (a * e - b * d) * id;
		r[12] = -(r[0] * m[12] + r[4] * m[13] + r[8] * m[14]);
		r[13] = -(r[1] * m[12] + r[5] * m[13] + r[9] * m[14]);
		r[14] = -(r[2] * m[12] + r[6] * m[13] + r[10] * m[14]);
		r[15] = 1;
		return r;
	}
}
