package ptrcraft.ptr;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Finds the player's own Paint the Town Red install (Steam app 337320). */
public final class PtrLocator {
	public static final String APP_ID = "337320";
	public static final String STORE_URL = "https://store.steampowered.com/app/337320/";

	public record Install(Path dir, Path data, String buildId) {
		public Path streamingAssets() {
			return data.resolve("StreamingAssets");
		}

		public Path bundles() {
			return streamingAssets().resolve("aa").resolve("StandaloneWindows64");
		}

		public Path catalog() {
			return streamingAssets().resolve("aa").resolve("catalog.json");
		}
	}

	private PtrLocator() {
	}

	/** The install, or null when Paint the Town Red isn't on this PC. */
	public static Install find() {
		String override = System.getenv("PTRCRAFT_PTR_DIR");
		if (override != null && !override.isBlank()) {
			Install i = check(Path.of(override.trim()), "custom");
			if (i != null) return i;
		}
		for (Path lib : steamLibraries()) {
			Path manifest = lib.resolve("steamapps").resolve("appmanifest_" + APP_ID + ".acf");
			if (!Files.isRegularFile(manifest)) continue;
			try {
				String acf = Files.readString(manifest, StandardCharsets.UTF_8);
				String dir = vdfValue(acf, "installdir");
				String build = vdfValue(acf, "buildid");
				Install i = check(lib.resolve("steamapps").resolve("common").resolve(dir == null ? "Paint the Town Red" : dir), build == null ? "unknown" : build);
				if (i != null) return i;
			} catch (IOException ignored) {
			}
		}
		return null;
	}

	private static Install check(Path dir, String build) {
		Path data = dir.resolve("PaintTheTownRed_Data");
		if (Files.isDirectory(data.resolve("StreamingAssets").resolve("aa").resolve("StandaloneWindows64"))) return new Install(dir, data, build);
		return null;
	}

	private static String vdfValue(String vdf, String key) {
		Matcher m = Pattern.compile("\"" + Pattern.quote(key) + "\"\\s+\"([^\"]*)\"").matcher(vdf);
		return m.find() ? m.group(1).replace("\\\\", "\\") : null;
	}

	static List<Path> steamLibraries() {
		Set<Path> roots = new LinkedHashSet<>();
		String reg = registrySteamPath();
		if (reg != null) roots.add(Path.of(reg));
		String pf86 = System.getenv("ProgramFiles(x86)");
		if (pf86 != null) roots.add(Path.of(pf86, "Steam"));
		String pf = System.getenv("ProgramFiles");
		if (pf != null) roots.add(Path.of(pf, "Steam"));
		String home = System.getProperty("user.home");
		roots.add(Path.of(home, ".steam", "steam"));
		roots.add(Path.of(home, ".local", "share", "Steam"));
		Set<Path> libs = new LinkedHashSet<>();
		for (Path root : roots) {
			Path vdf = root.resolve("steamapps").resolve("libraryfolders.vdf");
			if (!Files.isRegularFile(vdf)) continue;
			libs.add(root);
			try {
				Matcher m = Pattern.compile("\"path\"\\s+\"([^\"]+)\"").matcher(Files.readString(vdf, StandardCharsets.UTF_8));
				while (m.find()) libs.add(Path.of(m.group(1).replace("\\\\", "\\")));
			} catch (IOException ignored) {
			}
		}
		return new ArrayList<>(libs);
	}

	private static String registrySteamPath() {
		if (!System.getProperty("os.name", "").toLowerCase().contains("win")) return null;
		try {
			Process p = new ProcessBuilder("reg", "query", "HKCU\\Software\\Valve\\Steam", "/v", "SteamPath").redirectErrorStream(true).start();
			String out = new String(p.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
			p.waitFor();
			Matcher m = Pattern.compile("SteamPath\\s+REG_SZ\\s+(.+)").matcher(out);
			return m.find() ? m.group(1).trim() : null;
		} catch (IOException | InterruptedException e) {
			return null;
		}
	}
}
