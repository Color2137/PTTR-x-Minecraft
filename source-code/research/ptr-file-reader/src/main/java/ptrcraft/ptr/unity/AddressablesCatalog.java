package ptrcraft.ptr.unity;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.IOException;
import java.io.Reader;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Base64;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/** Unity Addressables catalog.json (ContentCatalogData): asset address, path or GUID -> its location and bundles. */
public final class AddressablesCatalog {
	public record Location(String internalId, String provider, List<String> bundles) {
		/** The bundle file that holds the asset itself (first dependency). */
		public String bundle() {
			return bundles.isEmpty() ? null : bundles.get(0);
		}
	}

	public final String[] internalIds;
	private final String[] providers;
	private final int[][] entries; // internalId, provider, dependencyKey, depHash, data, primaryKey, resourceType
	private final List<int[]> buckets = new ArrayList<>();
	private final Map<String, Integer> keyToBucket = new HashMap<>();

	public AddressablesCatalog(Path catalogJson) throws IOException {
		JsonObject j;
		try (Reader r = Files.newBufferedReader(catalogJson, StandardCharsets.UTF_8)) {
			j = JsonParser.parseReader(r).getAsJsonObject();
		}
		internalIds = strings(j.getAsJsonArray("m_InternalIds"));
		providers = strings(j.getAsJsonArray("m_ProviderIds"));
		ByteBuffer kd = b64(j, "m_KeyDataString");
		ByteBuffer bd = b64(j, "m_BucketDataString");
		ByteBuffer ed = b64(j, "m_EntryDataString");
		int nb = bd.getInt(0);
		int o = 4;
		for (int b = 0; b < nb; b++) {
			int keyOff = bd.getInt(o), n = bd.getInt(o + 4);
			o += 8;
			int[] es = new int[n];
			for (int k = 0; k < n; k++) es[k] = bd.getInt(o + 4 * k);
			o += 4 * n;
			buckets.add(es);
			String key = key(kd, keyOff);
			if (key != null) keyToBucket.putIfAbsent(key, b);
		}
		int ne = ed.getInt(0);
		entries = new int[ne][7];
		for (int e = 0; e < ne; e++) for (int k = 0; k < 7; k++) entries[e][k] = ed.getInt(4 + 28 * e + 4 * k);
	}

	private static String[] strings(JsonArray a) {
		String[] s = new String[a.size()];
		for (int i = 0; i < s.length; i++) s[i] = a.get(i).getAsString();
		return s;
	}

	private static ByteBuffer b64(JsonObject j, String k) {
		return ByteBuffer.wrap(Base64.getDecoder().decode(j.get(k).getAsString())).order(ByteOrder.LITTLE_ENDIAN);
	}

	private static String key(ByteBuffer kd, int off) {
		int type = kd.get(off) & 0xFF;
		switch (type) {
			case 0, 1 -> {
				int n = kd.getInt(off + 1);
				byte[] b = new byte[n];
				kd.get(off + 5, b);
				return new String(b, type == 0 ? StandardCharsets.US_ASCII : StandardCharsets.UTF_16LE);
			}
			case 4 -> {
				return Integer.toString(kd.getInt(off + 1));
			}
			case 3 -> {
				return Long.toString(kd.getInt(off + 1) & 0xFFFFFFFFL);
			}
			default -> {
				return null;
			}
		}
	}

	private Location location(int entry) {
		int[] e = entries[entry];
		List<String> deps = new ArrayList<>();
		if (e[2] >= 0) {
			for (int d : buckets.get(e[2])) {
				String id = internalIds[entries[d][0]];
				deps.add(id.substring(Math.max(id.lastIndexOf('\\'), id.lastIndexOf('/')) + 1));
			}
		}
		return new Location(internalIds[e[0]], providers[e[1]], deps);
	}

	/** Every location stored under a key (asset path, address or GUID). */
	public List<Location> find(String key) {
		Integer b = keyToBucket.get(key);
		List<Location> out = new ArrayList<>();
		if (b == null) return out;
		for (int e : buckets.get(b)) out.add(location(e));
		return out;
	}

	/** The location whose internal id is exactly this asset path. */
	public Location byPath(String assetPath) {
		for (int e = 0; e < entries.length; e++) {
			if (internalIds[entries[e][0]].equals(assetPath)) return location(e);
		}
		return null;
	}
}
