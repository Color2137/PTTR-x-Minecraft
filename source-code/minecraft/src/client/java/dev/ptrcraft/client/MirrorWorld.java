package dev.ptrcraft.client;

import dev.ptrcraft.SkyCraft;
import dev.ptrcraft.link.SkyLink;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.Difficulty;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.LevelSettings;
import net.minecraft.world.level.WorldDataConfiguration;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.presets.WorldPreset;

/**
 * Opens (or creates) the void "mirror" world automatically once Skyrim is connected.
 *
 * PTRCraft: every PTR level has its own Minecraft world, named after the level PTR is in
 * (PTRCraft-BikerBar, PTRCraft-Disco, PTRCraft-PirateCoveNight, PTRCraft-Custom-..., see the PTR
 * plugin's Mapping.LevelKey). It opens when PTR enters a level, and when PTR goes to another
 * level Minecraft saves and leaves its world and opens that level's.
 */
public final class MirrorWorld {
	private static final ResourceKey<WorldPreset> PRESET =
		ResourceKey.create(Registries.WORLD_PRESET, Identifier.fromNamespaceAndPath(SkyCraft.MOD_ID, "mirror"));
	private static final String PREFIX = SkyCraft.WORLD_NAME + "-";
	private static boolean attempted;
	// The world folder we opened (PTRCraft-BikerBar), null while in none or in a friend's.
	private static @org.jspecify.annotations.Nullable String openWorld;
	private static long lastLog;
	// /join: a friend's world for this session (the e4mc link their "Open to LAN" shows); null: our own.
	private static @org.jspecify.annotations.Nullable String sessionJoin;
	// Shown in chat once the player is in a world again (why they're back in their own, ...).
	private static @org.jspecify.annotations.Nullable String pendingNote;

	private MirrorWorld() {
	}

	/**
	 * The address in config/skycraft.properties ({@code join=abc-def.e4mc.link}), if any. Written
	 * with the template below the first time, so there's something to fill in.
	 */
	private static @org.jspecify.annotations.Nullable String joinAddress(Minecraft minecraft) {
		java.nio.file.Path file = minecraft.gameDirectory.toPath().resolve("config").resolve("ptrcraft.properties");
		java.util.Properties props = new java.util.Properties();
		try {
			if (!java.nio.file.Files.exists(file)) {
				java.nio.file.Files.createDirectories(file.getParent());
				java.nio.file.Files.writeString(file, """
					# SkyCraft
					# To play in a friend's world instead of your own: put their address after join=
					# (the link e4mc shows them when they open their world to LAN), then restart Minecraft.
					join=
					""");
			}
			try (var in = java.nio.file.Files.newBufferedReader(file)) {
				props.load(in);
			}
		} catch (java.io.IOException e) {
			SkyCraft.LOG.warn("SkyCraft: couldn't read {}", file, e);
			return null;
		}
		String join = props.getProperty("join", "").trim();
		return join.isEmpty() ? null : join;
	}

	/** /join: leave this world and play in a friend's (their e4mc link, or any server address). */
	public static void joinFriend(Minecraft minecraft, String link) {
		// People paste all sorts: "https://abc-def.e4mc.link/", " abc-def.e4mc.link ".
		String address = link.trim().replaceFirst("^[A-Za-z]+://", "").replaceAll("/+$", "");
		if (address.isEmpty()) {
			return;
		}
		SkyCraft.LOG.info("SkyCraft: /join {}", address);
		sessionJoin = address;
		leaveWorld(minecraft);
	}

	/** The friend's world we're in (the address we joined), or null in our own. */
	public static @org.jspecify.annotations.Nullable String friendAddress(Minecraft minecraft) {
		if (sessionJoin != null) {
			return sessionJoin;
		}
		var server = minecraft.isLocalServer() ? null : minecraft.getCurrentServer();
		return server != null ? server.ip : null;
	}

	/** /leave: back to our own world. */
	public static void leaveFriend(Minecraft minecraft) {
		if (sessionJoin == null) {
			minecraft.gui.hud.getChat().addClientSystemMessage(net.minecraft.network.chat.Component.literal("You're already in your own world."));
			return;
		}
		SkyCraft.LOG.info("SkyCraft: /leave {}", sessionJoin);
		sessionJoin = null;
		pendingNote = "Back in your own world.";
		leaveWorld(minecraft);
	}

	/** PTRCraft: the world of the level PTR is in, or null before PTR has entered one. */
	private static @org.jspecify.annotations.Nullable String wantedWorld() {
		String key = SkyClient.sky().levelKey;
		return key.isEmpty() ? null : PREFIX + key;
	}

	/**
	 * PTRCraft: true while Minecraft is in the world of PTR's current level (or a friend's world,
	 * which serves every level). Until then Minecraft doesn't follow PTR's teleports.
	 */
	public static boolean inLevelWorld(Minecraft minecraft) {
		if (minecraft.level == null) {
			return false;
		}
		String wanted = wantedWorld();
		return !minecraft.isLocalServer() || wanted == null || wanted.equals(openWorld);
	}

	/** PTRCraft: 0.1.x kept every level in one world, "PTRCraft"; what was built there was built in the Biker Bar. */
	private static void adoptOldWorld(Minecraft minecraft, String name) {
		var source = minecraft.getLevelSource();
		if (!name.equals(PREFIX + "BikerBar") || source.levelExists(name) || !source.levelExists(SkyCraft.WORLD_NAME)) {
			return;
		}
		try {
			try (var access = source.createAccess(SkyCraft.WORLD_NAME)) {
				access.renameLevel(SkyCraft.WORLD_NAME + ": Biker Bar");
			}
			java.nio.file.Files.move(source.getBaseDir().resolve(SkyCraft.WORLD_NAME), source.getBaseDir().resolve(name));
			SkyCraft.LOG.info("PTRCraft: the old one-world save is now the Biker Bar's world ({})", name);
		} catch (java.io.IOException e) {
			SkyCraft.LOG.warn("PTRCraft: couldn't turn the old world into {}", name, e);
		}
	}

	private static void leaveWorld(Minecraft minecraft) {
		attempted = false;
		openWorld = null;
		minecraft.disconnectFromWorld(net.minecraft.client.multiplayer.ClientLevel.DEFAULT_QUIT_MESSAGE);
		minecraft.gui.setScreen(new TitleScreen());  // openWhenReady takes it from the title screen
	}

	/** Every client tick: a note for the player once they're in a world again. */
	public static void tick(Minecraft minecraft) {
		if (pendingNote != null && minecraft.player != null) {
			minecraft.gui.hud.getChat().addClientSystemMessage(net.minecraft.network.chat.Component.literal(pendingNote));
			pendingNote = null;
		}
	}

	public static void openWhenReady(Minecraft minecraft) {
		// Couldn't reach a friend's world, or it closed under us: back to our own, and say why.
		if (minecraft.gui.screen() instanceof net.minecraft.client.gui.screens.DisconnectedScreen && minecraft.level == null) {
			pendingNote = sessionJoin != null
				? "Couldn't stay in " + sessionJoin + " (check the link, and that your friend's world is still open to LAN). You're back in your own world."
				: "Disconnected. You're back in your own world.";
			SkyCraft.LOG.info("SkyCraft: disconnected; back to the mirror world");
			sessionJoin = null;
			attempted = false;
			openWorld = null;
			minecraft.gui.setScreen(new TitleScreen());
			return;
		}
		// PTRCraft: PTR went to another level: save and leave this level's world for that one's.
		SkyLink.SkyState sky = SkyClient.sky();
		String wanted = wantedWorld();
		if (minecraft.level != null && minecraft.isLocalServer() && openWorld != null && wanted != null && !wanted.equals(openWorld)
			&& sky.inGame() && !sky.loading()) {
			SkyCraft.LOG.info("PTRCraft: PTR is in {} now: leaving {} for {}", sky.levelTitle, openWorld, wanted);
			leaveWorld(minecraft);
			return;
		}
		if (attempted && minecraft.level == null && minecraft.gui.screen() != null && System.currentTimeMillis() - lastLog > 5000) {
			lastLog = System.currentTimeMillis();
			SkyCraft.LOG.info("SkyCraft: still not in the mirror world; current screen {}", minecraft.gui.screen().getClass().getName());
		}
		if (attempted || minecraft.level != null || minecraft.gui.overlay() != null) {
			return;
		}
		// Wait for the menu to settle on the title screen; skip any first-launch prompts in front of it.
		if (!(minecraft.gui.screen() instanceof TitleScreen)) {
			if (minecraft.gui.screen() != null && System.currentTimeMillis() - lastLog > 5000) {
				lastLog = System.currentTimeMillis();
				SkyCraft.LOG.info("SkyCraft: waiting on screen {} before opening the mirror world", minecraft.gui.screen().getClass().getName());
			}
			if (minecraft.gui.screen() == null || minecraft.gui.screen().getClass().getName().contains("Onboarding")) {
				minecraft.gui.setScreen(new TitleScreen());
			}
			return;
		}
		TitleScreen title = (TitleScreen) minecraft.gui.screen();
		// PTRCraft: no world until PTR is in a level (then that level's).
		if (wanted == null && sessionJoin == null) {
			return;
		}
		attempted = true;
		// Multiplayer: join a friend's world (their e4mc link, or any server address) instead.
		String join = sessionJoin != null ? sessionJoin : joinAddress(minecraft);
		if (join != null) {
			SkyCraft.LOG.info("SkyCraft: joining {}", join);
			pendingNote = "Joined " + join + ". Type /leave to go back to your own world.";
			net.minecraft.client.gui.screens.ConnectScreen.startConnecting(title, minecraft, net.minecraft.client.multiplayer.resolver.ServerAddress.parseString(join),
				new net.minecraft.client.multiplayer.ServerData("SkyCraft", join, net.minecraft.client.multiplayer.ServerData.Type.OTHER), false, null);
			return;
		}
		if (wanted == null) {
			attempted = false; // friend's world gone; wait for PTR's level
			return;
		}
		String name = wanted;
		adoptOldWorld(minecraft, name);
		openWorld = name;
		if (minecraft.getLevelSource().levelExists(name)) {
			SkyCraft.LOG.info("PTRCraft: opening {} ({})", name, sky.levelTitle);
			minecraft.createWorldOpenFlows().openWorld(name, () -> minecraft.gui.setScreen(title));
			return;
		}
		SkyCraft.LOG.info("PTRCraft: creating {} ({})", name, sky.levelTitle);
		String levelName = sky.levelTitle.isEmpty() ? sky.levelKey : sky.levelTitle;
		LevelSettings settings = new LevelSettings(
			SkyCraft.WORLD_NAME + ": " + levelName,
			GameType.SURVIVAL,
			new LevelSettings.DifficultySettings(Difficulty.NORMAL, false, false),
			true,
			WorldDataConfiguration.DEFAULT
		);
		minecraft.createWorldOpenFlows().createFreshLevel(
			name,
			settings,
			new WorldOptions(0L, false, false),
			registries -> registries.lookupOrThrow(Registries.WORLD_PRESET).getOrThrow(PRESET).value().createWorldDimensions(),
			title
		);
	}
}
