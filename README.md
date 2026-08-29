# Vent

A first-person zombie survival game built in **Unity 6.3 LTS (6000.3.22f1)**. One sealed office
building, zombies pouring out of the AC vents, two guns that level up from kills, and a ladder of
levels where only the numbers change — until the front door opens onto a five-by-five-block district
full of cars. Get in one and run them over.

Everything — geometry, prefabs, meshes, textures, materials, data, scenes, sounds — is **generated
from code**. There is no binary art in the repository; a clean checkout rebuilds the project with one
command.

## Download and play

Grab the latest build from **[Releases](https://github.com/brettbergin/vent/releases/latest)** —
macOS (Apple Silicon and Intel) or Windows x64. No Unity, no build step.

The game is not code-signed, so the first launch needs one extra step. After that it updates
itself: Vent checks GitHub on launch and offers to download and restart into the new version, and
because those updates never come through a browser they carry no quarantine flag and never prompt
again.

**macOS** — unzip, drag `Vent.app` to your Applications folder, then run:

```bash
xattr -dr com.apple.quarantine /Applications/Vent.app
```

The old right-click ▸ *Open* trick no longer works on macOS 15 and later. If you would rather not
use Terminal: double-click the app, let it be blocked, then allow it in
**System Settings ▸ Privacy & Security ▸ Open Anyway**. Moving the app to Applications matters —
run it from Downloads and macOS launches it from a read-only copy, which blocks self-updates.

**Windows** — unzip the whole folder somewhere writable (your user folder, not `Program Files`) and
run `Vent.exe`. SmartScreen will warn about an unknown publisher: **More info ▸ Run anyway**. Keep
`Vent.exe` next to `Vent_Data`.

## Build from source

```bash
make regen    # regenerate every asset, prefab and scene   (Unity menu: Vent ▸ Rebuild Everything)
make test     # EditMode (pure logic) + PlayMode (end to end in the generated scene), headless
make build    # macOS player → Builds/Vent.app             (make build-windows for Windows x64)
make run      # launch it
make help     # everything else: packaging, releases, windowed tests, editor, logs, GPU benchmark
```

Or open the project, run **Vent ▸ Rebuild Everything**, open `Assets/_Project/Scenes/Boot.unity`
and press Play. The `make` targets wrap `tools/*.sh`; those are bash scripts.

| On foot | | Driving | |
|---|---|---|---|
| Move / sprint / jump | WASD · Shift · Space | Throttle / brake | W / S (hold S at a stop to reverse) |
| Look / fire / aim | Mouse · LMB · RMB | Steer / handbrake | A D · Space |
| Reload / switch weapon | R · 1 2 Q scroll | Look around | Mouse (the camera swings back behind the car) |
| Interact | **E** — doors, cables, racks, drawers, cars, items | Fire out of the window | LMB (pistol only) |
| Map | **C** (once you have found one) | | |
| Pause | Esc | Get out | E |

Gamepad is bound too (B = interact).

## How it plays

- **Levels.** `DifficultyProfile` is the whole progression model: per level it sets kills to
  advance, zombie HP, damage, speed, spawn rate, concurrency, XP — and *aggression*. Level-1
  zombies shamble near their vent until you get close, shoot nearby, or hit them; by level 13 they
  always know where you are. The building, the zombie and the guns never change.
- **Guns.** SMG and pistol level up from kills (`WeaponProgression`): damage, magazine, handling.
  Chambered round, staged reloads, recoil that climbs, damage falloff — all pure arithmetic.
- **Perks.** A kill may drop an orb: Instant Reload, Invulnerable, One Shot, Nuke.
- **Two ways out.** The front door unlocks at level 4. Or, from level 1: read the lobby whiteboard
  (the three patch cables only appear once you have), find them, patch the server rack, find the one
  desk whose monitor comes back, take the key from its drawer. The hunt re-rolls every run (`KeyHuntDirector`).
- **Things to find.** A printed floor plan and a vanity mirror lie somewhere in the office each run
  (`OfficeItemDirector`, rolled like the key hunt). The map is a translucent overlay on **C** with
  you on it — rooms by type, doors, vents, the front door; the mirror is a live rear view at the
  top of the screen from a second, cheap camera on the back of your head (`RearViewMirror`).
- **The district.** Streets, lots, shops, towers, a park and a construction site, generated from the
  same kit as the offices. Outdoor vents (manholes, drains) wake when the door opens.
- **Cars.** Twenty parked near the door: a red hero sedan plus a mix of hatchbacks, SUVs, pickups
  and vans. Driving is a custom probe-wheel model (no WheelColliders) with a gearbox, a yaw assist
  and cornering that physically cannot roll the car; the pistol goes out of the window; running a
  zombie over is lethal above 9 m/s and counts for the level, not for weapon XP.

## Architecture

Assemblies enforce the dependency direction; arrows point at dependencies.

```
Vent.Editor    ─► everything      generators; editor only
Vent.Gameplay  ─► all below       GameManager, LevelDirector, FrontDoor, KeyHuntDirector, VehicleDriver
Vent.Vehicles  ─► Core            car simulation, seat, roadkill, chase camera, lights, audio
Vent.Player    ─► Weapons, Core   controller, look, health, input, interaction
Vent.Weapons   ─► Core            definitions, progression, hitscan, view-model
Vent.Enemies   ─► Core            zombie AI, spawner, vents
Vent.UI        ─► Core            UI Toolkit screens; only listen to channels
Vent.Core                         events, pooling, damage, services, data, audio, perks
```

Patterns you will meet, and why they are there:

| Pattern | Where | Why |
|---|---|---|
| ScriptableObject event channels | `Core/Events` | Producers and consumers never reference each other. |
| Data as ScriptableObjects, defaults in code | `Data/*.asset`, `*.ApplyDefaults()` | Tunable in the inspector, reviewable in a diff, regenerated from code. |
| Pure-C# rules | `LevelRules`, `WeaponProgression`, `Ballistics`, `FrontDoorState`, `KeyHuntState`, `Vehicles/Simulation` | The rules that define the game are engine-free and unit tested. |
| Tiny service locator | `Core/Services/GameServices` | Scene singletons only; interfaces (`IPlayerTarget`, `IVehicleOccupant`) keep assemblies apart. |
| One input reader | `Player/Input/InputReader` | The only class that sees the Input System. |
| Explicit state machines | `Weapon`, `Zombie`, `GameManager` | Enum + switch, short enough to read top to bottom. |
| Awaitable transitions | `GameManager` | Unity 6 `Awaitable` under a linked cancellation token; no coroutines. |
| Interact by looking | `IInteractable`, `PlayerInteractor` | A camera raycast; the prompt answers "what am I looking at". |
| Damage in code, never by contact | `VehicleRoadkill`, `Zombie.TryStrike` | Zombies are NavMeshAgents nothing can push: cars pass through them and hurt them with an overlap query. |
| A car as arithmetic | `Vehicles/Simulation`, `VehicleController`, `VehicleWheel` | Tyre, suspension, steering and drivetrain are static functions on plain structs. Tyre forces act at centre-of-mass height, so a roll from steering is impossible by construction; the visible lean is cosmetic. |
| Bodies from profiles | `Editor/CarBodyLibrary.cs` | A silhouette is a dozen stations of three numbers and two flags; the loft makes a hull with wheel wells, glass and an underbody. A body style is a station list. |
| One camera, borrowed | `VehicleChaseCamera` | The chase rig re-parents the player's camera, so post-processing, the listener and the shake keep working. |
| Procedural presentation | `WeaponViewModel`, `ZombieAnimator`, `CameraMotion`, `ProceduralSoundBank` | No clips, no imports: animation and every sound are computed. |
| Generated textures and foliage | `TextureFactory`, `FoliageTextureFactory`, `FoliageLibrary`, `Shaders/Foliage.shader` | Surfaces from tileable noise with world-scale UVs; every leaf is a card from one painted atlas on a hand-written URP shader with wind and translucency. |
| Baked bounce, realtime direct | `SceneBuilder.Bake`, `BuildingGenerator.BuildProbes` | Mixed lights: shadows stay live, the lightmapper bakes the bounce and probes at regen. GPU Resident Drawer is off on purpose (`GeneratedSceneTests` pins it). |
| UI Toolkit with data binding | `UI/*.uxml`, `HudViewModel` | The HUD binds to a plain view model; controllers only write the model. |

Reading order: `Core/Events` → `DifficultyProfile` → `GameManager`/`LevelDirector` →
`PlayerCharacter` → `Weapon` → `Zombie`/`ZombieSpawner` → `FrontDoorState`/`KeyHuntDirector` →
`VehicleController`/`VehicleDriver` → `HudScreen` → `Editor/Bootstrap` and the generators.

## Generation

`Vent ▸ Rebuild Everything` (`Editor/Bootstrap.cs`) runs project settings → data and materials
(`AssetFactory`) → prefabs (`PrefabFactory`) → scenes (`SceneBuilder`, `BuildingGenerator`,
`DistrictGenerator`, `VehiclePlacer`), then bakes lighting and the NavMesh. It is idempotent: assets
keep their GUIDs, so references never break — edit the defaults in code, regenerate, commit.
**Vent ▸ Rebuild Assets and Prefabs** does the first half in under a minute for iterating on a prefab.

Look before you ship. **Vent ▸ Snapshot Player View / Rooms / District / Cars / Nature / Key Hunt**
render to `Logs/snapshot-*.png`; `make test-gui` runs the GPU render tests, which capture frames of
gunfire and driving to `Logs/render-*.png` and fail on shader errors. Headless renders skip the GPU,
so a build that only passed headless has not been seen.

If a build feels slow, read the numbers first: the player logs `[FrameRateLog]` lines (scene,
resolution, fps, worst frame) to `~/Library/Logs/Vent Studio/Vent/Player.log`, and `make gpubench`
times a fixed workload on each GPU — a laptop on a small charger with a flat battery caps its
discrete GPU to a few percent, and no build runs well on that.

## Tuning

| What | Where |
|---|---|
| Difficulty curves, aggression, grace periods | `DifficultyProfile.ApplyDefaults()` → `Data/DifficultyProfile.asset` |
| Guns and their level scaling | `Data/Weapon_*.asset`, `WeaponLevelCurve.ApplyDefaults()` |
| Perk chances, durations, orb lifetime | `PerkDropTable.ApplyDefaults()`, colours in `PerkStyle` |
| Zombie stats, hit reactions, body | `Data/Zombie.asset`, `PrefabFactory.CreateZombie`, `ZombieAnimator` |
| Cars: mass, grip, gearbox, steering, assists, roadkill | `VehicleDefinition.ApplyDefaults(shape)` → `Data/Vehicle_*.asset` — `maxLateralG` is how tight it corners, `yawAssist` how eagerly the nose follows |
| Car bodies | station lists in `CarBodyLibrary.For(shape)`; fleet mix in `VehiclePlacer` |
| Building: grid, rooms, seed, furniture | `BuildingLayout`, `PlanRoomTypes`, `PropLibrary` in `Editor/BuildingGenerator.cs` |
| District: seed, lots, streets, bays | `DistrictLayout` in `Editor/DistrictGenerator.cs` |
| Key hunt: cable count, distances, seed | `KeyHuntDirector` on the scene's `KeyHunt` root |
| Front door unlock level | `FrontDoor.unlockLevel` (4) |

## Tests

- **EditMode** (`Assets/Tests/EditMode`, 207): every pure rule (progression, levels, difficulty,
  ballistics, front door, key hunt, tyre/suspension/steering/drivetrain models), the room-type quotas
  over five thousand seeds, layers and the collision matrix, the generated prefabs and scenes,
  the updater's version comparison, manifest parsing, install-path resolution and generated helper
  scripts, and that every `MonoBehaviour` lives in a file named after it.
- **PlayMode** (`Assets/Tests/PlayMode`, 53): the generated building end to end — spawning, chasing,
  killing with credit, levelling, the door, the whole key chain, getting in and out of cars, drive-by,
  roadkill — plus `VehicleHandlingTests`, which drives the sedan on a proving ground: top speed
  through the gears, braking, full lock at top speed on all four wheels, handbrake slide, a kerb at
  speed, reverse, straight hands-off. Five tests need a GPU or real input and run under `make test-gui`.

Last verified on 6000.3.22f1 (macOS): EditMode 207/207, PlayMode 53/53 + 6 headless-only,
`tools/build.sh` → `Builds/Vent.app`, `tools/package.sh` → `dist/`.

## Releasing

A release is a git tag. `v0.2.0` builds both players, stamps them `0.2.0`, and publishes two zips
plus `latest.json` — the manifest installed copies poll.

```bash
make release VERSION=0.2.0                 # test, build both, package, tag, publish
make release VERSION=0.2.0 ARGS=--dry-run  # everything except the tag and the release
make package                               # just zip what is already in Builds/
```

Pushing the tag runs the same thing on GitHub Actions (`.github/workflows/release.yml`), which
needs `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` as repository secrets. **Until those are
set both workflows skip themselves** with a note rather than failing — releases are cut locally in
the meantime, and CI starts working the moment the secrets appear. The local path exists because
Unity's licensing on ephemeral CI runners is the flakiest part of the pipeline; if CI cannot
activate, `make release` still ships.

The version reaches the player through the `VENT_VERSION` environment variable, which
`BuildScript.ApplyVersion` writes to `PlayerSettings.bundleVersion` — so it surfaces as
`Application.version`, which is what the updater compares. A plain `make build` leaves it alone.

**How updating works.** `UpdateService` (`Core/Updates`) installs itself after the first scene
loads, the way `FrameRateLog` does — nothing in the generated Boot scene references it, so the
updater needs no regen. It fetches `latest.json` from the `/releases/latest/download/` permalink
(the REST API allows only 60 unauthenticated requests an hour), and the main menu shows a banner.
Accepting it streams the platform zip to `persistentDataPath`, checks its SHA-256, writes a
detached helper script, and quits so the helper can replace files the running process holds open.
macOS unpacks with `ditto` (`System.IO.Compression` drops the executable bit and the bundle stops
launching) and moves the old bundle aside so a failed copy rolls back; Windows mirrors with
`robocopy /MIR`. It refuses to install — and links the release page instead — from the editor, from
a translocated bundle, from a folder it cannot write, or when the install does not look like Vent.
The decision logic, path resolution and script generation are pure and unit tested, because that is
the code that overwrites someone's game.

## Layout

```
Assets/_Project/Scripts/{Core,Player,Weapons,Enemies,Vehicles,Gameplay,UI,Editor}   one asmdef each
Assets/_Project/Scripts/Core/Updates                                                the self-updater
Assets/_Project/{Data,Prefabs,Materials,Textures,Meshes,Scenes,UI}                  generated (UXML/USS hand-written)
Assets/_Project/Shaders                                                             the foliage shader
Assets/Tests/{EditMode,PlayMode}
Assets/Settings                                                                     URP assets
tools/                                                                              regen / test / build / package / release / gpubench
.github/workflows/                                                                  tests on push, release on a v* tag
```
