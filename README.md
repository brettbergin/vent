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
# 1. Regenerate all assets/prefabs/scenes (also runs from the Unity menu: Vent ▸ Rebuild Everything)
./tools/regen.sh

# 2. Run the test suites (EditMode: pure logic; PlayMode: end-to-end in the generated scene)
./tools/test.sh

# 3. Build a macOS player into Builds/Vent.app
./tools/build.sh
```

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
* Zombie damage is `baseDamage × DamageMultiplier(level)`; zombies already alive are re-scaled when
  the level changes, so damage is always relative to the level being played.

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
| **Procedural presentation** | `WeaponViewModel`, `ZombieAnimator`, `CameraMotion`, `ProceduralSoundBank` | No clips, no imports: sway/bob/recoil/flinch and every sound are computed. |
| **Camera stacking** | `PrefabFactory.CreatePlayer` | URP overlay camera renders the gun so it never clips into walls. |
| **UI Toolkit** | `UI/*.uxml`, `UI/Screens` | Screens are documents + a controller bound to channels; visibility follows `GameState`. |

### Reading order

1. `Core/Events/EventChannel.cs` → `GameplayEventChannels.cs`
2. `Core/Data/DifficultyProfile.cs` (the whole progression model)
3. `Gameplay/Flow/GameManager.cs` → `Gameplay/Levels/LevelDirector.cs` / `LevelRules.cs`
4. `Player/PlayerCharacter.cs` → `Movement/FirstPersonController.cs` → `Movement/PlayerLook.cs`
5. `Weapons/Runtime/Weapon.cs` → `WeaponInventory.cs` → `Progression/WeaponProgression.cs`
6. `Enemies/Runtime/Zombie.cs` → `Spawning/ZombieSpawner.cs` → `Spawning/AirVent.cs`
7. `UI/Screens/HudScreen.cs`
8. `Editor/Bootstrap.cs` → `BuildingGenerator.cs` → `PrefabFactory.cs` → `SceneBuilder.cs`

## Tuning

* Difficulty: `Assets/_Project/Data/DifficultyProfile.asset` (curves over level). Defaults in
  `DifficultyProfile.ApplyDefaults()`.
* Guns: `Data/Weapon_SMG.asset`, `Data/Weapon_Pistol.asset`; level scaling in
  `Data/WeaponLevels_Standard.asset` (`WeaponLevelCurve.ApplyDefaults()`).
* Zombie: `Data/Zombie.asset`.
* Building: `BuildingLayout` in `Editor/BuildingGenerator.cs` (grid size, room size, seed, door density).

Re-running **Vent ▸ Rebuild Everything** rewrites assets in place (GUIDs are preserved), so
references never break. Edit the defaults in code, regenerate, commit.

## Tests

* EditMode (`Assets/Tests/EditMode`): progression, level rules, difficulty monotonicity, event
  channels, pooling, audio synthesis, save file.
* PlayMode (`Assets/Tests/PlayMode`): loads the generated Building scene and checks vents are on the
  NavMesh, the building is sealed, a zombie emerges/chases/dies with kill credit, firing consumes
  ammo and kills level the weapon, and N kills advance the level and refill ammo.

## Layout

```
Assets/_Project/Scripts/{Core,Player,Weapons,Enemies,Gameplay,UI,Editor}   source, one asmdef each
Assets/_Project/{Data,Prefabs,Materials,Scenes,UI}                         generated (UXML/USS are hand-written)
Assets/Tests/{EditMode,PlayMode}
Assets/Settings                                                            URP assets from the Unity template
tools/                                                                     headless regen / test / build scripts
```
