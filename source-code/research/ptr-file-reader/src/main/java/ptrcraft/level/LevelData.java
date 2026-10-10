package ptrcraft.level;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * The Biker Bar as read from the player's Paint the Town Red, already in Minecraft's axes
 * (x = PTR x, y = PTR y, z = -PTR z, 1 metre = 1 block) relative to the level origin.
 */
public final class LevelData {
	public static final int FORMAT = 3;
	/** Collision resolution: sub-cells per block edge. */
	public static final int SUB = 8;

	public String ptrBuild = "";
	public final List<Tex> textures = new ArrayList<>();
	public final List<Batch> batches = new ArrayList<>();
	/** block key (see {@link #key}) -> 512 bits (8x8x8 sub-cells, index x + 8*(y + 8*z)). */
	public final Map<Long, long[]> solid = new HashMap<>();
	public float spawnX, spawnY, spawnZ, spawnYaw;
	public final List<Person> people = new ArrayList<>();
	public final Map<String, Part> rig = new HashMap<>();
	public float[] min = {0, 0, 0}, max = {0, 0, 0};
	public final List<String> report = new ArrayList<>();

	/** A texture file in the cache folder. */
	public record Tex(String name, String file, int width, int height) {
	}

	/** Triangles sharing one texture, tint and blend mode: 8 floats per vertex (x y z u v nx ny nz). */
	public static final class Batch {
		public int texture;
		public int tint = 0xFFFFFFFF;
		/** 0 opaque, 1 cutout, 2 translucent */
		public int mode;
		public float[] verts = new float[0];
		public int vertexCount;
	}

	/**
	 * One person placed in the bar. Bone transforms are PTR's, in PTR's own axes, relative to the
	 * person's root; the renderer converts them.
	 */
	public static final class Person {
		public String name = "";
		public String settings = "";
		public int appearance;
		public float x, y, z, yaw; // Minecraft axes, level space
		public float scale = 1;
		public boolean sitting;
		public int headTexture = -1, bodyTexture = -1;
		// PTR-side numbers (enemy_settings)
		public float health = 1, damageMul = 1, attackSpeedMul = 1, receivedMul = 1, knockbackMul = 1;
		public boolean canBeKnockedDown = true;
		public float koMin = 4, koMax = 7;
		/** bone name -> local TRS matrix (column-major, PTR axes) and parent bone name */
		public final List<Bone> bones = new ArrayList<>();
	}

	public record Bone(String name, String parent, float[] local) {
	}

	/** One Blockman box (or other part mesh), in its bone's local PTR space: 8 floats per vertex. */
	public static final class Part {
		public String name;
		public boolean head;
		public float[] verts;
		public int vertexCount;
	}

	public static long key(int bx, int by, int bz) {
		return ((long) (bx & 0x3FFFFF) << 42) | ((long) (by & 0xFFFFF) << 22) | (bz & 0x3FFFFF);
	}

	public static int keyX(long k) {
		return (int) (k << 0 >> 42);
	}

	public static int keyY(long k) {
		return (int) (k << 22 >> 44);
	}

	public static int keyZ(long k) {
		return (int) (k << 42 >> 42);
	}

	public boolean solidAt(int sx, int sy, int sz) {
		int bx = Math.floorDiv(sx, SUB), by = Math.floorDiv(sy, SUB), bz = Math.floorDiv(sz, SUB);
		long[] bits = solid.get(key(bx, by, bz));
		if (bits == null) return false;
		int i = Math.floorMod(sx, SUB) + SUB * (Math.floorMod(sy, SUB) + SUB * Math.floorMod(sz, SUB));
		return (bits[i >> 6] & (1L << (i & 63))) != 0;
	}

	public void setSolid(int sx, int sy, int sz) {
		int bx = Math.floorDiv(sx, SUB), by = Math.floorDiv(sy, SUB), bz = Math.floorDiv(sz, SUB);
		long[] bits = solid.computeIfAbsent(key(bx, by, bz), k -> new long[SUB * SUB * SUB / 64]);
		int i = Math.floorMod(sx, SUB) + SUB * (Math.floorMod(sy, SUB) + SUB * Math.floorMod(sz, SUB));
		bits[i >> 6] |= 1L << (i & 63);
	}
}
