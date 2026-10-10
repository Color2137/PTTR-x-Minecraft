# MODLOG: PTRCraft (Minecraft inside Paint the Town Red)

## Versions and paths (2026-10-09)
- Paint the Town Red 1.3.4 r5682, Steam app 337320, build 15837486, `E:\SteamLibrary\steamapps\common\Paint the Town Red`.
  Unity 2020.3.39f1, IL2CPP (metadata v27.1), Addressables bundles in `PaintTheTownRed_Data/StreamingAssets/aa`,
  FMOD banks for audio, Rewired for input. No anti-cheat found. Saves: `%USERPROFILE%\AppData\LocalLow\South East Games\Paint The Town Red`.
- BepInEx 6.0.0-be.788+5b766a3 (Unity.IL2CPP win x64), SHA-256 f4cc496b...817a. Interop generated on first start (~30 s).
- Minecraft 26.3, Fabric Loader 0.19.5, Fabric API 0.162.0+26.3, Java 25 (Prism's java-runtime-epsilon).
- Prism Launcher 10.0.5 (installed, `%LOCALAPPDATA%\Programs\PrismLauncher`), instance `PTRCraft`.
- PTR metadata dump (Il2CppDumper 6.7.46): `%TEMP%\ptrmc\dump\dump.cs` (not in the project).
- Backup of PTR saves before the first modded start: `~\.universal-modder\backups\ptr-saves\20261009-193353.zip`.

## Route
1. First design (dropped): Minecraft as host, PTR's Biker Bar read from PTR's files into Minecraft (Java Unity reader in
   `research/ptr-file-reader`). Chosen because Melty can only start catalog games and PTR isn't one.
2. The user wanted the real PTR on screen (SkyCraft-style), local only, so: **passthrough with PTR as host**.
   - PTR side: BepInEx 6 IL2CPP plugin (C#, .NET 6), Harmony patches through Il2CppInterop.
   - Minecraft side: fork of SkyCraft's Fabric mod (MIT), renamed to ptrcraft; mapping `Local\PTRCraft_v1`, magic PTRC.
   - The PTR plugin implements the host role SkyCraft's SKSE plugin has (same protocol v11).

## How PTR works (what the plugin relies on)
- Player: `PTTRPlayer` (+ `PTTRFirstPersonController` movement/look, `SimpleMouseRotator` on the player and
  `Head Joint`, `WeaponAttack` fists/kicks/weapons, `CharacterController`). Camera: `PTTRPlayer/Head Joint/First Person Camera`.
  Transform position = feet + 0.9 (`GetFeetPosition`).
- Player damage funnels through `PTTRPlayer.SetHealth(value, loweringHealth)`; `GetPunched(... Enemy hittingEnemy)` is the melee hit.
- People: `Enemy` (health ~1.0 x settings multiplier, `originalHealth`, `isDead`, `IsKnockedDown`, `inCombat`, hips/feet/head
  transforms). `Enemy.Damage(amount, hitPoint, localDir, force, IEnemyTarget, sounds...)` applies damage;
  `StartCombat(t, sync, alertRange)` starts the brawl. Bodies are voxel models (MagicaVoxel) on PTR's Blockman rig.
- Menus: `GameManager.Paused`, `GameManager.InMenu`; `GameManager.LoadLevel("BikerBar")` loads a level.
- HUD: `GameplayUICanvas/PanelGameHUD` (health, powers), `ControlsHelp` (key prompts like "Q - KICK").
- Shaders: `Shader.Find` only sees always-included shaders; PTR's own (`Custom/VertexColourStandardUVSpec`, lit + vertex
  colour) is found through `Resources.FindObjectsOfTypeAll<Shader>()` once the level's bundles are loaded.
- Biker Bar collision: 590 BoxColliders + 43 readable MeshColliders (27k tris) + 70 unreadable (skipped unless convex).

## Mapping
MC = (PTR.x + levelX, PTR.y, -PTR.z), 1 m = 1 block; levelX = 4096 x (index + 1) per level (BikerBar 4096).
MC yaw = Unity yaw - 180. Pitch the same. Mouse look on the PTR side with Minecraft's sensitivity curve.

## Verification log
- 2026-10-09 19:55: first link with a dev client; takeover, collision (51,513 voxel blocks), overlay.
- 20:02: user report "Minecraft lags" -> overlay was 4K (33 MB/frame). Fixed: overlay capped at 1080 high, Minecraft paced
  to 75 fps. Overlay ~3 fps -> ~45 fps.
- 20:09: Prism instance launched by the plugin (after a Prism rescan, see gotcha 4); user played: 130 hits on PTR people,
  15 hits on the player, brawl across the bar (screenshots shots/ptr4-ptr6 in %TEMP%\ptrmc).

## Gotchas
1. Long project path (>190 chars, MSIX-virtualized AppData): git clone and process working dirs fail. Build output,
   Gradle project cache and run dirs go to `%TEMP%\ptrmc`.
2. Voxelizing collision on PTR's main thread froze PTR (big floor triangles rasterized once per region). Now: Unity reads on
   the main thread (ms), voxelizing on a worker with System.Numerics (no Il2Cpp calls off the main thread), one pass per triangle.
3. BepInEx's console window was the process's main window, so window capture grabbed it; turned off in BepInEx.cfg.
4. A running Prism doesn't see a new instance folder until its watcher fires after the folder is complete
   ("PTRCraft resolves to nothing"). The launcher now retries (3 tries, 75 s apart).
5. While PTR is paused or unfocused the Minecraft side freezes on purpose (SkyCraft's menu rule): measure frame rates in play.
6. Harmony patches are applied one class at a time, so a renamed PTR method costs one feature, not the plugin;
   the plugin also checks every member in sheets/ptr_hooks.json at startup and logs what's missing.

7. PTR only finishes loading a level while its window isn't minimized (it sat on LoadingScene while minimized).
   Window capture (gfxcapture) also gets no frames from a minimized window.
8. PTR re-locks the cursor every frame in GameManager.SetCursorAppropriate: patched (prefix) so Minecraft screens get a
   free cursor.
9. AccessTools.TypeByName scans every assembly and trips over Unity UIElements types that don't load under interop
   (TypeLoadException spam): hook checks look types up in Assembly-CSharp / Unity modules directly.

## 0.1.1 (built 2026-10-09; user asked: inventory cursor, all modes, F5)
- Minecraft screens (E inventory, chat, chests, crafting): cursor freed and shown (CursorPatch + ApplyCursor).
- F5: PTR's whole player body hidden; Minecraft's player model (RenAvatar + RenTexture) drawn at the feet in third
  person; camera behind / in front with Minecraft's zoom (pulls in at once, eases out over 0.2 s), as SkyCraft does.
- Other Minecraft entities and particles (RenScene) drawn.
- All modes (Scenarios, Arena, Beneath): PTR teleports detected (player moved more than 1.5 m by PTR) and sent to
  Minecraft; collision refreshed when PTR's static geometry fingerprint changes (checked every 3 s), emptied regions
  cleared; G = PTR use (WeaponAttack.InteractWithWeaponOrButton) for buttons, Beneath items, shops; Minecraft death ->
  PTR death (setting MinecraftDeathEndsPtrLevel).
- Tested so far: builds, 27/27 hooks found at startup, Minecraft starts and links. Not yet played (PTR was minimized
  while the user played something else).

## 0.1.2 (built 2026-10-10; user asked: Minecraft's mobs/entities, arrows, players, blocks rendered by Minecraft, shown like its HUD)
- Minecraft renders its level again (LevelRendererMixin no longer cancels it): placed blocks, mobs, players, arrows, items,
  particles, outlines, cracks and the F5 body go into the same overlay as the hand and HUD. No sky pass (shouldRenderSky
  forced false), clouds off, classic transparency; the clear pass already leaves alpha 0, so PTR shows through.
- Occlusion: a "ptrcraft_occluders" frame pass, added right before LevelRenderer's main pass, draws depth-only (pipeline from
  WATER_MASK_SNIPPET, TRIANGLES, no cull, colour write mask 0, depth bias -1/-8 so things resting on PTR surfaces don't
  flicker) PTR's exact collision triangles (deduplicated; PTR sends a triangle once per 8-block region it touches; stair
  helper ramps skipped) plus a 0.8-width box per standing PTR person from the actor table. The level mesh is rebuilt on a
  worker when SkyCollision.triangleVersion() changes (at most every 0.5 s while PTR streams).
- View bobbing and damage tilt are turned off while Minecraft draws the world (they move Minecraft's camera, not PTR's).
- PTR side: setting `[Display] MinecraftDrawsWorld` (default on) -> host flag SkyHostDrawsWorld when off. With it on,
  Minecraft only sends solid-block bits and lights (PTR people bump into builds, torches light PTR) and the placement cube on
  PTR surfaces; no meshes, atlas animation, entities or avatar. Switching resends everything.
- Also in this build: CheckHooks looks types up directly (gotcha 9), now deployed.
- Tested so far: both build; preflight 0 errors. Not run yet (PTR wasn't running; not started while the user was in another app).

## Session 2026-10-10 00:18-00:22 (user played 0.1.2, no one watching; read from the logs afterwards)
- PTR: 27/27 hooks found (the CheckHooks fix works), Prism instance started on try 1, takeover in the Biker Bar.
- Minecraft: "Minecraft draws Minecraft's blocks and entities", occluders 27036 triangles, no mixin errors. User ran
  /gamemode creative and gave themselves an ender_dragon_spawn_egg: the dragon hit ~40 PTR people for 10 each (proxy hits
  reach PTR). Biker Bar -> Disco -> Biker Bar.
- Found: (1) the Disco never took over: Minecraft teleported (12292, -0.17, -14.2) but held the player waiting for the
  collision region 8 blocks under the feet, which is below PTR's level bounding box, and the 6 s timeout also required it.
  (2) PTR rebuilt the whole level's collision every 3 s during the brawl (47 rebuilds): the fingerprint counted mesh colliders
  on loose props. (3) The world folder PTRCraft was gone from saves at 00:22:57, after Minecraft saved at 00:22:47; not deleted
  by the mod, assumed the user removed it.

## 0.1.3 (built 2026-10-10; user asked: a separate Minecraft world for every scenario)
- One Minecraft world per PTR level: PTR sends a level key and name (host state +0x40 / +0x70, NUL-padded UTF-8); Minecraft
  opens or creates `PTRCraft-<key>` (MirrorWorld) once PTR is in a level, and saves, leaves and opens the next level's when
  PTR changes level. Keys: the scene name; `PirateCoveNight` when `PaintTheTownRed.PirateCoveNight`; custom levels
  `Custom-<file name>-<hash of LevelEditor.LevelFileToPlay>`. World display name `PTRCraft: <level name>`.
- Minecraft only follows PTR's teleports (and so only lets PTR's player be taken over) in the right level's world; PTR's
  player waits whenever Minecraft is out of its world.
- The old one-world save `PTRCraft`, if present, becomes `PTRCraft-BikerBar` (it isn't there any more on this PC).
- Each level keeps its stretch of X (known list append-only; PirateCoveNight added at the end).
- Fixes from the session: collision regions known one region beyond the level all round + hold released after 15 s at most;
  the change fingerprint ignores colliders on rigidbodies.
- Tested so far: both build, preflight 0 errors. Not run yet.

## 0.1.4 (built 2026-10-10; user asked: a world for each arena challenge, 1st-5th and endless)
- The arena's challenges share the scene `Arena`; `Arena.CurrentChallengeIndex` (static, 0-based; PTR's own
  LevelNameForDisplay takes an arenaChallengeIndex) picks the key `Arena1`..`Arena5`. Endless: scene `EndlessArena`, or
  `Arena` with `GameManager.IsEndlessArena`, key `EndlessArena`.
- Runner re-reads the key every second while in a level: if it changes without a scene change (next challenge in place),
  PTR's player is handed back and the level starts over (new epoch, teleport) in the new key's world.
- Not run yet. Unknown: whether CurrentChallengeIndex is set before the arena scene loads or only in it (either way the
  1-second check catches it, at worst after Minecraft opened the previous challenge's world).

## 0.1.5 (built 2026-10-10; user asked: all of Beneath, each in its own world)
- Beneath (from dump.cs): 6 areas `Beneath.AreaIndex_*` CAV 0, CRY 1, RUI 2, ISL 3, TRI 4, FIR 5; statics
  `Beneath.CurrentArea`, `CurrentLevel`, `CurrentTricksterRoom` (TricksterRoom_* BikerBar 0, Disco 1, Prison 2,
  PirateCove 3), `AreaNames`, per-run seeds `CurrentAreasSeeds[area][level]`.
- Keys in the BeneathLevel scene: `Beneath-<area>-<CurrentLevel+1>`, and `Beneath-TRI-<room>` in the Trickster's area;
  titles from AreaNames (rich-text tags stripped). The hub stays `BeneathHub`. The 1-second key check (0.1.4) starts the
  level over in the new world when PTR moves to another floor without changing scene.
- Chosen: a world per area and floor, not per run (seeded floors would make a new world every floor of every run).
  Unknown until played: whether CurrentLevel counts within the area or across the run (the key is unique either way; only
  the floor number in the title would differ).
- Not run yet.

## Session 2026-10-10 01:11-01:13 (user played Beneath on 0.1.5: "i'm falling down all the time")
- 35/35 hooks. Hub: 300 boxes, 1458 meshes (590,206 triangles), 272 unreadable meshes, 46 other. First build:
  737,600 blocks in **280,778 regions, 34 s** (bounding box of everything, padded; a few huge triangles sampled at 1/16).
  Builds after that never finished before the next level replaced them. Minecraft player died in the hub (fell).
- Caves floor 1 (Beneath-CAV-1 world created fine): the build at level start saw **1 box** (Beneath generates the floor
  after the scene loads); the refresh 3 s later had 534 meshes (283,477 triangles) but never finished in the 30 s there.
- Per-level worlds worked: BeneathHub -> Beneath-CAV-1 -> BeneathHub, each opened/created and the player taken over.

## 0.1.6 (built 2026-10-10; user asked: make the colliders as they are in PTR)
- Collision streams nearest the player first: the worker indexes triangles/boxes by region, then encodes pending regions
  sorted by distance to the player's current position (re-sorted every max(256, n/16) regions).
- Known regions = regions with geometry and their 26 neighbours (not the bounding box). Triangles are filled per region
  over the area of their dominant projection (conservative 2D cells, plane span per cell): tested against 1/64-block dense
  sampling on 5 triangles, 0 voxels missed, 2-3 % extra at edges. Triangles spanning > 200k regions are left out. Boxes over
  4096 m^3 are hollow.
- A level's first build waits until the colliders' fingerprint is unchanged for 1 s with at least 8 colliders (10 s at most).
- Colliders PTR can't hand out (unreadable meshes, terrain, others) are measured with Collider.Raycast (Sampler): 25 cm grid
  (coarser above 40k cells or 400 columns), six directions, up to 8 surfaces deep, each surface taken by the rays along its
  main axis, merged into strips per grid row; 3 ms per frame, nearest collider first, cached per collider pose. Spheres and
  capsules are tessellated (were boxes); CharacterControllers and WheelColliders are skipped.
- Minecraft keeps holding the player while nothing of the level has arrived (up to 60 s), 15 s once it is arriving.
- Not run yet.

## Session 2026-10-10 01:28-01:30 (user played Beneath's hub on 0.1.6: "no collision", "can't enter the rest of beneath")
- 37/37 hooks. Hub: 594,112 triangles + 246 colliders measured with rays (4,866 triangles; all 246 cached by the second
  build), streamed in **3.0 s** (was 34 s): 752,674 blocks in 47,766 regions. Minecraft had all 598,930 triangles by 01:28:45.
- But a refresh ran every 3 s for the whole session (25 of them): the change fingerprint counted the player's own colliders
  and PTR's people, which move. Each one re-sent all 47,766 regions; Minecraft's server fell 5 s behind once.
- The player walked ~90 s, then died at 01:30:00 "fell from a high place" (last look at y -218): the elevator shaft, by the
  user's account. PTR's elevator / exits never react: their OnTriggerEnter needs the player's CharacterController, which is
  off while Minecraft plays.

## 0.1.7 (built 2026-10-10; user asked: a trigger on the elevator that takes you down to Beneath)
- Triggers.cs stands in for PhysX for the player: each frame Physics.OverlapCapsuleNonAlloc with the player's capsule
  (QueryTriggerInteraction.Collide), and OnTriggerEnter / Stay (physics rate) / Exit by SendMessage to the trigger's object,
  its rigidbody's object and the player, with the player's CharacterController as the collider. All of PTR's trigger zones,
  not only the elevator (139 trigger handlers). Physics.GetIgnoreLayerCollision and Component.GetComponentInParent are
  stripped from PTR's build (preflight caught both), so zones on PTR's people are left out by walking up the parents.
- Fingerprint skips the player, PTR's people and CharacterControllers; a refresh only resends regions whose content hash
  changed.
- tools/sheets.mjs: dump.cs has several classes with the same name (System.ComponentModel.Component and
  UnityEngine.Component); preflight now checks all of them (it reported Component.GetComponent missing).
- Not run yet.

## Session 2026-10-10 01:39-01:43 (user on 0.1.7: Biker Bar -> hub -> Caves 1; "fell off when the mc controller loads")
- 41/41 hooks. The hub's elevator worked (Triggers): BeneathHub -> Beneath-CAV-1. Hub refreshes now send 111, then 4
  regions (47,655 unchanged).
- Caves 1: Minecraft was teleported to (0, 193, 0) Unity: Beneath parks PTR's player high above the floor while it builds it,
  then moves it down. Minecraft held there (no collision that high) until the 15 s release, then PTR took over and both fell:
  "fell from a high place" at 01:41:03. After PTR's restart (epoch 4, start y 199) the takeover came quickly (something solid
  under y 199) and the player later "fell out of the world" (below y -1088) at 01:42:23.
- Caves 1 collision: 340k-426k triangles but **9,067,993 blocks in 418,572 regions, 20 s**: some huge collider(s).

## 0.1.8 (built 2026-10-10)
- Takeover only once PTR's player is at rest (moved < 5 cm for 0.75 s, Beneath.InTransition false); Minecraft is then sent
  to that spot (teleport seq bumped), and again if PTR's player moves more than 30 cm before Minecraft arrives.
- Minecraft no longer lets go of a held player without collision around its feet (only after 60 s).
- Collision streams within 160 blocks of the player; farther regions follow as the player gets near (a refresh may replace
  a worker that is only waiting for that). Regions that went away are emptied right after indexing.
- The three largest colliders of each build are logged (to find what is so big in the Caves).
- Not run yet.

## Sessions 2026-10-10 02:28 and 10:11-10:31 (0.1.8)
- Caves 1 start fixed: Minecraft teleported to the parking spot (y 199), then, once PTR's player was at rest, to the floor
  (y 12.7); the user played the Caves for 14 minutes. A Minecraft death ended the PTR floor and PTR restarted it (setting
  MinecraftDeathEndsPtrLevel verified). 10:32-10:35: Minecraft linked, PTR never reported a level (no custom-level session
  in any log yet).

## 0.1.9 (built 2026-10-10; user asked: custom levels with collision, less knockback in Beneath, Minecraft fire burns PTR people)
- Custom levels (LevelEditorPlay builds rooms, platforms, stairs, props and people after the scene loads): collision and the
  takeover wait while LevelEditorPlay.CreatingObjects or until LevelEditorPlay.StartedPlaying (20 s at most); the settle
  limit is 20 s. Testing from the editor (LevelEditor.TestingLevel, shared temp file) names the world after
  LevelEditor.CurrentLevelSimpleName. Untested: no custom level has been played with the mod yet.
- Knockback: Enemy.ApplyImpactForce got 60 x knockback + 20 x damage (24-224); PTR's own impacts on Beneath's enemies are
  5-10 (EnemyCrystalCrab/EnemyMinion MeleeImpactForce 5, EnemyCharger ChargeImpactForce 10). Settings [Combat] Knockback
  (1.0) and KnockbackBeneath (0.1, GameManager.IsBeneath) scale both the Damage force and the impact.
- Fire: the stand-ins (SkyrimActorEntity) now run applyEffectsFromBlocks() each tick (Minecraft only applies fire, lava,
  campfire and magma contact after an entity's own move). Fire damage already reached PTR flagged HIT_FIRE; PTR now calls
  Enemy.SetOnFire() (PTR's own burning, as the arena's fire traps) unless Enemy.onFire, and skips Minecraft's fire damage.
  Setting [Combat] MinecraftFireBurnsPtr. Fire can stand on PTR floors (BlockStateBaseMixin makes PTR ground sturdy).
- Not run yet.

## Session 2026-10-10 ~10:57-12:02 (0.1.9)
- 49/49 hooks. A custom level ("StayinShadow", LevelEditorPlay) worked: own world, takeover once at rest, collision in ~1 s
  (155,750 triangles; 1,923 regions near the player). Its largest collider: `_Level/StopZone` BoxCollider 2000x50x2000 (its
  faces leave ~392k far regions pending, never sent). Then Biker Bar and Disco. 731 Minecraft hits in the bar, knockback
  0.4-0.5 and up to 7 damage: Enemy.ApplyImpactForce up to 170 a hit (the user: enemies "teleporting sometimes").

## 0.2.0 (built 2026-10-10; user asked: hit enemies stay where they are, as in PTR)
- An ordinary Minecraft hit (knockback <= 0.45) no longer calls ApplyImpactForce: PTR's people flinch in place (AnimateImpact,
  PTR's own hurt reaction). Only real knockback pushes: 10 per knockback point above 0.45 (sprint hit ~4.5, Knockback II
  ~15), in PTR's own range (5-30). The damage term is gone (a sword hurts more, it doesn't shove further). The force handed
  to Damage (how a knocked-out body falls) is 3 x (0.5 + knockback), was 6 + 4 x knockback. Knockback/KnockbackBeneath
  settings scale both.
- Package: E:\PTTR x MC (75 MB): "1 - copy into the Paint the Town Red folder" (winhttp.dll, doorstop files, dotnet,
  BepInEx core/plugins/patchers/config; not interop or unity-libs, which BepInEx makes from the user's own PTR; plugin
  debug console off), "2 - copy into Prism Launcher's instances folder\PTRCraft" (instance.cfg without Java path or play
  times, mmc-pack.json, mods, config, icon; no worlds, logs or options), source, HOW TO INSTALL.txt, LICENSE-SkyCraft.txt.
  The instance is copied as a folder, not imported: PTR launches it by its folder name.
- Not run yet.

## Next
- 0.2.0 in game: hits leave PTR's people where they stand; sprint hits still push a little.
- The package on a clean PC (or a second Prism instance folder): install steps as written.
- 0.1.9 in game: a custom level (collision, takeover), Beneath knockback, fire on a PTR person (block, lava, Fire Aspect).
- 0.1.8 in game: Caves 1 start (no fall), what the largest colliders are, Caves streaming time.
- 0.1.7 in game: the hub's elevator takes you to the Caves; no refresh churn in the hub; where the hub has no collision.
- 0.1.6 in game: Beneath hub and floors (no falling), Biker Bar collision still right, sampling time for the hub's 272
  unreadable meshes, frame time while sampling.
- 0.1.5 in game: Beneath hub -> CAV floor 1 -> floor 2, a Trickster's room; each in its own world.
- 0.1.4 in game: arena challenges 1-5 and endless each in their own world.
- 0.1.3 in game: a world per level (Biker Bar -> Disco -> Pirate Cove night), the Disco takeover, collision refreshes only
  when the level really changes.
- 0.1.2 in game: Minecraft blocks/mobs drawn and hidden behind PTR walls and people; frame rate with the level drawn; how far
  Minecraft's things trail PTR's camera when turning (if it shows: send the world and HUD as two layers and re-aim the world
  layer to PTR's camera).
- Confirm placed blocks on screen, PTR people colliding with builds, torch lights.
- Draw arrows / dropped items (world entities) and the third-person body.
- PTR's death/restart flow vs Minecraft respawn; other levels.
