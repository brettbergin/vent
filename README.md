# Vent

A complete, self-contained first-person zombie survival game for **Unity 6.3 LTS (6000.3.22f1)**,
built to read like production code. One sealed building, many rooms, zombies pouring out of the
AC vents, two guns that level up from kills, and an endless ladder of levels where nothing changes
except the numbers.

Everything — level geometry, prefabs, materials, ScriptableObject data, scenes — is **generated
from code** by editor scripts. There are no Asset Store downloads and no binary art; a clean
checkout regenerates the whole project with one command.

## Quick start

```bash
make regen    # 1. Regenerate all assets/prefabs/scenes (Unity menu: Vent ▸ Rebuild Everything)
make test     # 2. Run the test suites (EditMode: pure logic; PlayMode: end-to-end in the generated scene)
make build    # 3. Build a macOS player into Builds/Vent.app (make build-windows for Windows x64)
make run      #    ...and launch it
make help     #    everything else (windowed test run, open in the editor, logs, clean)
```

The `make` targets wrap the scripts in `tools/`; call those directly if you prefer.

Or open the project in the Editor, run **Vent ▸ Rebuild Everything**, open
`Assets/_Project/Scenes/Boot.unity` and press Play.

Controls: WASD move · Shift sprint · Space jump · Mouse look · LMB fire · RMB aim · R reload ·
1/2/Q/scroll switch weapon · Esc pause. Gamepad is bound too.

## The game loop

```
                 ┌────────────────────────────────────────────────────┐
                 │                 DifficultyProfile (SO)             │
                 │  level ─► kills-to-advance, HP×, DMG×, speed×,     │
                 │           spawn interval, max concurrent, XP×      │
                 └───────┬───────────────────────────┬────────────────┘
                         │ Evaluate(level)           │ KillsRequired(level)
                         ▼                           ▼
   AirVent ◄──pick──  ZombieSpawner            LevelDirector ──LevelEventChannel──► HUD banner
      │                  │ pool.Get              ▲   (LevelRules: count kills,      Player: refill ammo, heal
      ▼                  ▼                       │    advance at N)                 Spawner: new snapshot
   Zombie (NavMeshAgent state machine) ──KillEventChannel(Killer=Weapon)──┘
      ▲                                              │
      │ hitscan via Hitbox → IDamageable             ▼
   Weapon ◄─── WeaponInventory.OnKill: GrantExperience → WeaponProgression → level up (damage×, mag×, …)
```

* The **environment never changes** (one generated building), the **zombie never changes** (one
  definition), the **guns never change** (SMG + Pistol). Only `DifficultyProfile` curves and
  `WeaponLevelCurve` curves move the numbers.
* **Aggression is one of those numbers.** At level 1 zombies are merely annoyed: they shamble near
  their vent until they see you up close, hear a shot nearby, or get hit. Every level they notice
  from farther away, sense you through walls from farther away, re-path more often and strike
  faster, until around level 13 they are enraged and always know where you are.
  (`DifficultyProfile.Aggression` → `ZombieStats.From` → `Zombie` states `Wandering`/`Chasing`.)
* Zombie damage is `baseDamage × DamageMultiplier(level)`; zombies already alive are re-scaled when
  the level changes, so damage is always relative to the level being played.
* **Perks.** A weapon kill has a chance (`PerkDropTable`, default 12%) to leave a glowing orb where
  the zombie fell; walk through it to collect. Instant Reload tops up both guns at once, Invulnerable
  (10 s) ignores damage, One Shot (8 s) makes any hit lethal, Nuke kills every zombie in the building
  (those kills count toward the level, but not toward weapon XP, and never drop more perks).
  Orbs fade after 25 s and at most three lie around at once. (`Core/Perks`, `Gameplay/Perks/PerkSystem`,
  `Evt_PerkCollected` → player / weapons / HUD.)

## Architecture

Assemblies (`.asmdef`) enforce the dependency direction; arrows point at dependencies.

```
Vent.Editor ─► everything (generators; editor-only)
Vent.Gameplay ─► Player, Weapons, Enemies, Core     (GameManager, LevelDirector, persistence)
Vent.Player   ─► Weapons, Core                       (controller, look, health, input reader)
Vent.Weapons  ─► Core                                (definitions, progression, hitscan, view-model)
Vent.Enemies  ─► Core                                (zombie AI, spawner, vents)
Vent.UI       ─► Core                                (UI Toolkit screens; only listens to channels)
Vent.Core                                            (events, pooling, damage, services, data, audio)
```

Patterns you will see, and why:

| Pattern | Where | Why |
|---|---|---|
| **ScriptableObject event channels** | `Core/Events` | Producers and consumers never reference each other. The HUD, the player and the spawner all react to `Evt_LevelChanged` without knowing the director exists. |
| **RuntimeSet** | `Core/Collections`, `Set_Zombies`, `Set_Vents` | Live lists that objects add themselves to; no `FindObjectsOfType` in the loop. |
| **Data as ScriptableObjects** | `Data/*.asset` | Weapons, zombie, difficulty and weapon-level curves are assets; the shipped values are defined in code (`ApplyDefaults`, `AssetFactory`) so they are reviewable. |
| **Pure-C# rules** | `WeaponProgression`, `LevelRules`, `Cooldown` | The rules that define the game are engine-free and unit tested. |
| **Object pooling** | `Core/Pooling` on top of `UnityEngine.Pool` | Zombies, tracers, flashes, impacts are never instantiated at runtime. |
| **Tiny service locator** | `Core/Services/GameServices` | For true scene singletons only (player target, pools, audio, director). Interfaces (`IPlayerTarget`) keep Enemies independent of Player. |
| **Input reader asset** | `Player/Input/InputReader` | The only class that touches the Input System; exposes polled values and C# events; switches action maps. |
| **Explicit state machines** | `Weapon`, `Zombie`, `GameManager` | Enum + switch. Small enough to read top to bottom. |
| **Awaitable transitions** | `GameManager` | Scene loads and the death delay are Unity 6 `Awaitable`s under a `CancellationTokenSource` linked to `destroyCancellationToken`; a new transition cancels the old one. No coroutines. |
| **Procedural presentation** | `WeaponViewModel`, `ZombieAnimator`, `CameraMotion`, `ProceduralSoundBank` | No clips, no imports: sway/bob/recoil/flinch and every sound are computed. The view-model also animates gun parts by name — magazine drops and reseats on reload, slide/bolt cycles per shot and locks back on empty — and ejects pooled brass. |
| **Gunfire VFX from sprites** | `TextureFactory.MuzzleFlashSprite/SmokeSprite/SparkSprite`, `PrefabFactory.CreateMuzzleFlash/CreateMuzzleSmoke` | The flash is three additive sprite planes (two crossed along the bore, one facing forward) with a random roll and size per shot and a light that spikes and dies; a separate pooled world-space burst leaves smoke drifting and sparks streaking after the gun has moved on. The tracer is an additive line with a white-hot core. `SceneRendersTests.ShotDrawsWithoutErrors` fires and captures the frames to `Logs/render-shot-*.png`. |
| **Gun handling in numbers** | `Weapons/Runtime/Ballistics.cs`, `WeaponDefinition` "handling" | Chambered round (+1 on a tactical reload), slower empty reload that ends with a rack, recoil that climbs over sustained fire, damage falloff by distance, per-weapon flash size. Pure arithmetic is unit tested. |
| **Camera stacking** | `PrefabFactory.CreatePlayer` | URP overlay camera renders the gun so it never clips into walls. |
| **Generated post-processing** | `SceneBuilder.BuildPostProcessProfile` | One `VolumeProfile` (ACES, bloom off the emissive panels, grading, vignette, grain) built from code; a global `Volume` in each lit scene. |
| **Generated textures, world-scale UVs** | `Editor/TextureFactory.cs`, `Editor/MeshLibrary.cs` | Drywall, ceiling tile, vinyl, wood, concrete, asphalt, brushed metal and fabric are synthesised (albedo + normal) from tileable noise and written as PNGs at regen; still no hand-made binary art. Building blocks use cube meshes whose UVs are in metres, so a 50 cm floor tile is 50 cm on every block. |
| **Baked bounce, realtime direct** | `SceneBuilder.EnsureLightingSettings` / `Bake`, `BuildingGenerator.BuildProbes` | Lights are Mixed (Indirect Only): direct light and shadows stay realtime, the CPU lightmapper bakes the bounce into lightmaps and a light-probe grid (two heights per room) at regen (~1.5 min). Per-room box-projected reflection probes render once on load — a headless editor bake writes garbage cubemaps, and `SceneRendersTests` fails on the magenta that produces. SSAO is on in `PC_Renderer` (`ProjectBootstrap.ConfigureAmbientOcclusion`). |
| **Forward+ lighting, no GPU Resident Drawer** | `Assets/Settings/PC_RPAsset.asset`, `BuildingGenerator` | Room lights cast hard shadows within a 20 m shadow distance. The GPU Resident Drawer is deliberately **off**: with it on, the macOS player drew only the clear colour (its instancing shader variants were stripped from the build; the editor keeps every variant, so it looked fine there). `GeneratedSceneTests` pins this. Geometry stays static-batched. |
| **UI Toolkit + runtime data binding** | `UI/*.uxml`, `UI/Screens`, `HudViewModel` | Screens are documents + a controller bound to channels; visibility follows `GameState`. The HUD binds declaratively (`<Bindings>` in `Hud.uxml`) to a plain `HudViewModel`; the controller only writes the model. |

### Reading order

1. `Core/Events/EventChannel.cs` → `GameplayEventChannels.cs`
2. `Core/Data/DifficultyProfile.cs` (the whole progression model)
3. `Gameplay/Flow/GameManager.cs` → `Gameplay/Levels/LevelDirector.cs` / `LevelRules.cs`
4. `Player/PlayerCharacter.cs` → `Movement/FirstPersonController.cs` → `Movement/PlayerLook.cs`
5. `Weapons/Runtime/Weapon.cs` → `WeaponInventory.cs` → `Progression/WeaponProgression.cs`
6. `Enemies/Runtime/Zombie.cs` → `Spawning/ZombieSpawner.cs` → `Spawning/AirVent.cs`
7. `Core/Perks/PerkDropTable.cs` → `Gameplay/Perks/PerkSystem.cs` → `Core/Perks/PerkPickup.cs`
8. `UI/Screens/HudScreen.cs`
9. `Editor/Bootstrap.cs` → `BuildingGenerator.cs` → `PrefabFactory.cs` → `SceneBuilder.cs`

## Tuning

* Difficulty: `Assets/_Project/Data/DifficultyProfile.asset` (curves over level). Defaults in
  `DifficultyProfile.ApplyDefaults()`. Includes the aggression curve and the grace periods: `runStartGrace` (seconds before
  the first zombie) and `levelStartGrace` (seconds of breather after each level-up).
* Guns: `Data/Weapon_SMG.asset`, `Data/Weapon_Pistol.asset`; level scaling in
  `Data/WeaponLevels_Standard.asset` (`WeaponLevelCurve.ApplyDefaults()`).
* Perks: `Data/PerkDrops.asset` — drop chance, per-kind weights and durations, orb lifetime and floor cap
  (`PerkDropTable.ApplyDefaults()`). Colours and names in `Core/Perks/PerkStyle.cs`.
* Zombie: `Data/Zombie.asset` — base stats, melee timing, hit reactions (stagger threshold and
  length, limb damage multiplier), and the "annoyed" ends of the aggression ranges (notice/hearing
  radii, slower strikes, wander speed); the enraged ends are the base values.
* Zombie body: `PrefabFactory.CreateZombie` builds a jointed rig (hips → spine → neck → head/jaw,
  shoulder → elbow, hip → knee) with hitboxes per zone (head 2.5×, torso 1×, limbs 0.65×) and a
  world-space health bar; `ZombieAnimator` drives the pivots (shuffle, reach, lunge, stagger,
  topple-and-sink death) and randomises height, tint, stride and arm pose per spawn.
* Building: `BuildingLayout` in `Editor/BuildingGenerator.cs` (grid size, room size, seed, door density).
  Each room is given a purpose (`RoomType`: office, conference, break room, lobby at the spawn,
  storage, server room) and furnished from `Editor/PropLibrary.cs` — desks, chairs, cabinets,
  shelves, vending machines, racks and the rest, all primitives with colliders on the Environment
  layer, so they are cover, they carve the NavMesh, and they never sit on a door or a vent landing.
  **Vent ▸ Snapshot Rooms** photographs every room to `Logs/`.
  Outer walls carry windows (glass panes keep the building sealed and are shootable). Rooms are lit
  by their ceiling panel plus a warm dusk spot light outside each window — the only light that comes
  "through" a window, so nothing leaks through walls — and a sun on its own rendering layer lights
  only the exterior (ground, distant buildings, procedural dusk sky).

Re-running **Vent ▸ Rebuild Everything** rewrites assets in place (GUIDs are preserved), so
references never break. Edit the defaults in code, regenerate, commit.

## Tests

* EditMode (`Assets/Tests/EditMode`): progression, level rules, difficulty monotonicity, event
  channels, pooling, audio synthesis, save file.
* PlayMode (`Assets/Tests/PlayMode`): loads the generated Building scene and checks vents are on the
  NavMesh, the building is sealed, a zombie emerges/chases/dies with kill credit, firing consumes
  ammo and kills level the weapon, and N kills advance the level and refill ammo.

## Conventions worth knowing

* **One `MonoBehaviour`/`ScriptableObject` per file, named after the class.** Unity resolves an
  asset's script by file name; a class that shares a file loads as "missing script". The EditMode
  test `ScriptFileNamingTests` fails the build if this is violated.
* The `tools/*.sh` scripts are bash; run them directly (`./tools/test.sh`) rather than sourcing
  them from zsh.
* Regeneration is idempotent: existing assets keep their GUIDs, prefabs and scenes are rewritten.

## Verification status

Last verified on Unity 6000.3.22f1 (macOS, Intel): headless regeneration succeeds, EditMode
35/35 and PlayMode 6/6 pass, and `tools/build.sh` produces `Builds/Vent.app`.

## Layout

```
Assets/_Project/Scripts/{Core,Player,Weapons,Enemies,Gameplay,UI,Editor}   source, one asmdef each
Assets/_Project/{Data,Prefabs,Materials,Textures,Meshes,Scenes,UI}         generated (UXML/USS are hand-written; Scenes/<name>/ holds the baked lightmaps)
Assets/Tests/{EditMode,PlayMode}
Assets/Settings                                                            URP assets from the Unity template
tools/                                                                     headless regen / test / build scripts
```
