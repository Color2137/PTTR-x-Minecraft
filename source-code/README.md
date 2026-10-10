# PTRCraft: Minecraft inside Paint the Town Red

You play **Paint the Town Red** as a **Minecraft player**. PTR is the game on screen: its levels,
its people, their AI, the brawls, the ragdolls and the red paint are all PTR's own. Minecraft runs
hidden in the background (you only see it in Task Manager) and *is* the player: its movement and
physics, its hotbar, hearts and hunger, its hand and items, its blocks, its combat.

- A BepInEx plugin inside PTR (`ptr-plugin/`, C#) turns PTR's player into a puppet of the Minecraft
  player, lays Minecraft's own picture over PTR's frame, and lands your Minecraft hits on PTR's
  people through PTR's own damage code.
- What's Minecraft's is drawn by Minecraft: your hand and HUD, and also your placed blocks, mobs,
  other players, arrows, dropped items, particles, block outlines and cracks, and your own body in
  F5. Minecraft draws them on a transparent background in the same picture as its HUD, and PTR's
  walls, floors, furniture and people hide whatever is behind them: PTR's geometry is drawn into
  Minecraft's depth buffer first as an invisible layer.
- A Fabric mod inside Minecraft (`minecraft/`, Java) is a fork of
  [SkyCraft](https://github.com/chasmlol/SkyCraft) by chasmlol (MIT), the same design that puts
  Minecraft inside Skyrim, linked to PTR instead of Skyrim through the shared memory `Local\PTRCraft_v1`.

**Local only.** This is not on Melty: Melty can't install into or start Paint the Town Red until it
adds PTR to its catalog. It runs on this PC from your own copies of both games.

## What you need

- Paint the Town Red on Steam (1.3.4, developed on build 15837486).
- Minecraft: Java Edition (your Microsoft account) and Prism Launcher signed in with it.
- About 4 GB of extra RAM for the hidden Minecraft.

## Install

```bat
powershell -ExecutionPolicy Bypass -File tools\install.ps1
```

It installs BepInEx 6 (bleeding edge 788, IL2CPP) into PTR's folder, the PTRCraft plugin, and a
Prism instance named **PTRCraft** (Minecraft 26.3, Fabric 0.19.5, Fabric API 0.162.0+26.3, the
ptrcraft mod). Your other Prism instances are not touched. The very first start of PTR after
installing BepInEx takes about a minute longer (BepInEx prepares itself once).

## Play

Start Paint the Town Red from Steam. PTR starts the PTRCraft Minecraft hidden by itself. Pick a level
(the Biker Bar is the one this was built and tested on). Once Minecraft has joined (a few seconds),
your PTR player becomes the Minecraft player at PTR's own start position.

Every PTR level has its own Minecraft world, with its own builds, inventory and mobs. The first time
you enter a level, its world is created in the PTRCraft instance's `saves` folder:

| PTR level | Minecraft world |
|---|---|
| Biker Bar, Disco, Prison, Saloon | `PTRCraft-BikerBar`, `PTRCraft-Disco`, `PTRCraft-Prison`, `PTRCraft-Saloon` |
| Pirate Cove by day / by night | `PTRCraft-PirateCove`, `PTRCraft-PirateCoveNight` |
| Arena challenges 1st to 5th | `PTRCraft-Arena1` ... `PTRCraft-Arena5` |
| Endless Arena, Sandbox | `PTRCraft-EndlessArena`, `PTRCraft-Sandbox` |
| Beneath's hub | `PTRCraft-BeneathHub` |
| Beneath, each floor of each area | `PTRCraft-Beneath-CAV-1`, `-CAV-2` ... `-CRY-`, `-RUI-`, `-ISL-`, `-FIR-` |
| Beneath, the Trickster's rooms | `PTRCraft-Beneath-TRI-BikerBar`, `-Disco`, `-Prison`, `-PirateCove` |
| A custom or Workshop level | `PTRCraft-Custom-<level name>-<6 letters>`, one per level file |

Going to another level saves the world you were in and opens that level's (a few seconds; PTR's
player waits). Back on PTR's menu, the world of the last level stays open.

| Key | Does |
|---|---|
| WASD, Space, Shift, Ctrl, mouse | Minecraft movement and look |
| Left / right mouse | Minecraft attack/break and use/place |
| 1-9, wheel | hotbar |
| E, Q, T, /, F3... | Minecraft (inventory, drop, chat, commands, debug); in Minecraft screens the mouse cursor is free |
| **F5** | Minecraft's cameras: first person (no body), behind you, in front of you (your skin and armour) |
| **G** | PTR's "use": press PTR buttons, take Beneath items and upgrades, shop pickups |
| **O** | Minecraft's pause / options menu |
| **Esc** | PTR's pause menu (or closes an open Minecraft screen) |

- Hit PTR's people with anything Minecraft has: fists, swords, bows. The first hit starts PTR's
  brawl and the bar joins in. A bare-fist punch is tuned to take about four to drop a biker.
- Minecraft fire sets PTR's people burning with PTR's own fire, like the arena's fire traps: fire
  blocks, lava, campfires, magma, Fire Aspect and Flame arrows (not yet tried in game).
- In Beneath, Minecraft hits push enemies about a tenth as far as in the scenarios (PTR itself pushes
  them much less); both are settings (`Knockback`, `KnockbackBeneath`).
- PTR's punches cost Minecraft hearts (armour helps). If you die, Minecraft's respawn should put you
  back at the level's start (not tested yet).
- Place blocks anywhere: Minecraft draws them; PTR's people should bump into them and torches
  should light PTR (built, not yet confirmed on screen).

**Tested so far (2026-10-09, Biker Bar):** PTR starts the Prism Minecraft hidden; you spawn at PTR's
start as the Minecraft player; walking and jumping on PTR's floor and stairs; Minecraft's HUD and
hand at about 45 fps on a 4K screen; 130 Minecraft hits on PTR's people (knockouts and deaths through
PTR's own damage, the whole bar brawling); PTR's punches taking Minecraft hearts; PTR's health bar
and powers hidden.

## Settings

`BepInEx\config\dev.ptrcraft.cfg` in PTR's folder (made on the first start); every entry is
described in `sheets/settings.json`. The useful ones: damage both ways (`[Combat]`), overlay size
and Minecraft's frame cap (`[Display]`), and `StartWithPTR` (turn off to start Minecraft yourself).
`[Display] MinecraftDrawsWorld` (on) is who draws Minecraft's blocks and entities: Minecraft, in its
picture; off, PTR draws them itself as meshes in its own scene, lit by PTR's lights (the 0.1.1 way).

## Known limits (0.1.3)

- Built but not yet tried in game: a world per level and switching between them (0.1.3), and a
  world per arena challenge (0.1.4; PTR's challenge number is checked every second, so moving on to
  the next challenge without a reload also switches worlds).
- Minecraft drawing its blocks, mobs, arrows, items and F5 body behind PTR's walls (0.1.2) has run
  (an Enderman and an Ender Dragon in the Biker Bar, by the logs) but I haven't seen it on a
  screenshot yet. Not yet tried: the free cursor in Minecraft screens, the F5 cameras, G for PTR's use,
  following PTR's own teleports (Beneath floors, portals), collision that refreshes when PTR changes
  its level geometry, and Minecraft deaths ending the PTR level (0.1.1).
- Minecraft's things come in Minecraft's picture, at Minecraft's frame rate and a frame or two
  behind PTR's camera: when you turn fast they can trail a little behind PTR's world.
- What hides them is PTR's collision, not its looks: doors and small loose props (not solid for
  Minecraft) don't hide anything, people are boxes, and an invisible PTR wall would hide things too.
- Minecraft lights its own blocks and mobs (always noon); PTR's lamps don't light them. Torches you
  place do light PTR.
- With Minecraft drawing the world, view bobbing and the hurt camera tilt are off (PTR's camera
  doesn't bob, so Minecraft's things would sway against PTR's world).
- PTR's doors and loose props are not solid for Minecraft (you walk through doors). Colliders PTR
  can't hand out (70 in the Biker Bar, 272 in Beneath's hub) are measured with PTR's own physics
  (rays 25 cm apart), a little each frame, nearest first: in a big level the far parts arrive a few
  seconds after the part you stand in. (0.1.6, not yet tried in game.)
- Played in the Biker Bar only. In the Disco, Minecraft never let go of the player in 0.1.2 (it
  waited for collision under the spawn that PTR never sent); fixed in 0.1.3, not yet re-tried.
  The other levels load and get collision but haven't been played through.
- Beneath builds every floor anew each run, but a floor's Minecraft world stays: what you built on
  floor 2 of the caves is there again on the next run's floor 2, whose rooms are laid out differently
  (it may float or end up in a wall).

Debugging: PTR's log is `BepInEx\LogOutput.log`; Minecraft's is
`%APPDATA%\PrismLauncher\instances\PTRCraft\minecraft\logs\latest.log`.

## Uninstall

```bat
powershell -ExecutionPolicy Bypass -File tools\install.ps1 -Uninstall
```

Removes exactly what was added to PTR's folder (see `docs/UNINSTALL-BepInEx.txt`) and the PTRCraft
Prism instance (with its Minecraft world). Your PTR saves were never changed; a backup from before
the first modded start is in `%USERPROFILE%\.universal-modder\backups\ptr-saves\`.

## How it's built

- `sheets/`: the design, one JSON sheet per kind of thing (PTR hooks, link channels, systems, input
  map, combat rules, settings). `node tools/sheets.mjs preflight` checks every cell, every reference
  between sheets, that every system exists, and that every PTR member the plugin touches still exists
  in PTR's metadata; `node tools/sheets.mjs gen` generates the settings, combat constants and hook
  list the plugin uses.
- `ptr-plugin/`: `dotnet build -c Release -p:Deploy=true` builds and copies the plugin into PTR.
- `minecraft/`: `gradlew build` (JDK 25). Its build output and dev run folder go to `%TEMP%\ptrmc`.
  Minecraft's own world drawing is `client/mixin/LevelRendererMixin.java`; PTR's invisible depth layer
  is `client/render/PtrOccluders.java`.
- `tools/ptrdev.mjs`: talks to the plugin's debug console (127.0.0.1:47347): `state`, `scene`,
  `tree <path>`, `fields <path> <Component>`, `load <Level>`.
- `research/`: earlier work (a Java reader for PTR's asset files, written for a Minecraft-host design
  that was dropped) and SkyCraft's protocol header.

## Credits

- **SkyCraft** by chasmlol (MIT): the Minecraft mod this one is forked from, and the protocol.
- **BepInEx** (LGPL-2.1), **Il2CppInterop**, **HarmonyX** (MIT): loading the plugin into PTR.
- **Fabric** Loader and API (Apache-2.0); **Prism Launcher** (GPL-3.0).
- **Paint the Town Red** by South East Games; **Minecraft** by Mojang Studios. Not affiliated with
  either; you need to own both games. No game files are included: PTR is modified only at runtime.
- Built with Claude Code (Anthropic) using the universal-modder toolkit, directed by color2644.
