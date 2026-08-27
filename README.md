# Vent

A first-person zombie survival game for **Unity 6.3 LTS (6000.3.22f1)**, written to read like
production code. One sealed office building, zombies pouring out of the AC vents, two guns that
level up from kills, and a ladder of levels where only the numbers change — until the front door
opens onto a five-by-five-block district full of cars. Get in one and run them over.

Everything — geometry, prefabs, meshes, textures, materials, data, scenes, sounds — is **generated
from code**. There is no binary art in the repository; a clean checkout rebuilds the project with one
command.

## Quick start

```bash
make regen    # regenerate every asset, prefab and scene   (Unity menu: Vent ▸ Rebuild Everything)
make test     # EditMode (pure logic) + PlayMode (end to end in the generated scene), headless
make build    # macOS player → Builds/Vent.app             (make build-windows for Windows x64)
make run      # launch it
make help     # everything else: windowed tests, editor, logs, GPU benchmark, clean
```

Or open the project, run **Vent ▸ Rebuild Everything**, open `Assets/_Project/Scenes/Boot.unity`
and press Play. The `make` targets wrap `tools/*.sh`; those are bash scripts.

| On foot | | Driving | |
|---|---|---|---|
| Move / sprint / jump | WASD · Shift · Space | Throttle / brake | W / S (hold S at a stop to reverse) |
| Look / fire / aim | Mouse · LMB · RMB | Steer / handbrake | A D · Space |
| Reload / switch weapon | R · 1 2 Q scroll | Look around | Mouse (the camera swings back behind the car) |
| Interact | **E** — doors, cables, racks, drawers, cars | Fire out of the window | LMB (pistol only) |
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
- **Two ways out.** The front door unlocks at level 4. Or, from level 1: read the lobby whiteboard,
  find three patch cables, patch the server rack, find the one desk whose monitor comes back, take
  the key from its drawer. The key hunt re-rolls every run (`KeyHuntDirector`).
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

- **EditMode** (`Assets/Tests/EditMode`, 132): every pure rule (progression, levels, difficulty,
  ballistics, front door, key hunt, tyre/suspension/steering/drivetrain models), the room-type quotas
  over five thousand seeds, layers and the collision matrix, the generated prefabs and scenes, and
  that every `MonoBehaviour` lives in a file named after it.
- **PlayMode** (`Assets/Tests/PlayMode`, 53): the generated building end to end — spawning, chasing,
  killing with credit, levelling, the door, the whole key chain, getting in and out of cars, drive-by,
  roadkill — plus `VehicleHandlingTests`, which drives the sedan on a proving ground: top speed
  through the gears, braking, full lock at top speed on all four wheels, handbrake slide, a kerb at
  speed, reverse, straight hands-off. Five tests need a GPU or real input and run under `make test-gui`.

Last verified on 6000.3.22f1 (macOS): regen, EditMode 132/132, PlayMode 48/48 + 5 GPU-only, GUI
render tests 3/3, `tools/build.sh` → `Builds/Vent.app`.

## Layout

```
Assets/_Project/Scripts/{Core,Player,Weapons,Enemies,Vehicles,Gameplay,UI,Editor}   one asmdef each
Assets/_Project/{Data,Prefabs,Materials,Textures,Meshes,Scenes,UI}                  generated (UXML/USS hand-written)
Assets/_Project/Shaders                                                             the foliage shader
Assets/Tests/{EditMode,PlayMode}
Assets/Settings                                                                     URP assets
tools/                                                                              regen / test / build / gpubench
```
