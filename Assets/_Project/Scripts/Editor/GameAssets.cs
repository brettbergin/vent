using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Vent.Core.Data;
using Vent.Core.Events;
using Vent.Core.Perks;
using Vent.Enemies.Data;
using Vent.Enemies.Runtime;
using Vent.Enemies.Spawning;
using Vent.Player.Input;
using Vent.Vehicles.Data;
using Vent.Weapons.Data;

namespace Vent.Editor
{
    /// <summary>
    /// Handles to every generated asset, passed from the asset factory to the prefab and scene
    /// builders. Plain fields: this is a build-time bag, not runtime data.
    /// </summary>
    public sealed class GameAssets
    {
        // Events
        public KillEventChannel Kill;
        public LevelEventChannel Level;
        public IntEventChannel KillsThisLevel;
        public HealthEventChannel Health;
        public VoidEventChannel PlayerDied;
        public WeaponHudEventChannel WeaponHud;
        public WeaponLevelUpEventChannel WeaponLevelUp;
        public BoolEventChannel Hit;
        public NoiseEventChannel Noise;
        public PerkEventChannel PerkCollected;
        public GameStateEventChannel GameState;
        public RunSummaryEventChannel RunSummary;
        public IntEventChannel BestLevel;
        public VoidEventChannel PlayRequested;
        public VoidEventChannel ResumeRequested;
        public VoidEventChannel RestartRequested;
        public VoidEventChannel MenuRequested;
        public VoidEventChannel QuitRequested;
        public StringEventChannel Prompt;
        public StringEventChannel Announcement;
        public FloatEventChannel VehicleSpeed;

        // Data
        public DifficultyProfile Difficulty;
        public ZombieDefinition Zombie;
        public WeaponLevelCurve WeaponLevels;
        public PerkDropTable PerkDrops;
        public WeaponDefinition Smg;
        public WeaponDefinition Pistol;
        public InputActionAsset InputActions;
        public InputReader InputReader;
        public ZombieRuntimeSet Zombies;
        public VentRuntimeSet Vents;
        public VehicleDefinition Sedan;
        public VehicleDefinition Van;
        public PanelSettings PanelSettings;

        // Materials
        public Material Floor, Wall, Ceiling, Trim, Prop, PropAlt, VentMetal, LightPanel, ZombieSkin, ZombieHead, ZombieClothes, ZombieGore, ZombieEye, HealthBarTrack, HealthBarFill, GunMetal, GunAccent, GunPolymer, GunSteel, Brass, Tracer, Flash, Spark, Blood, Concrete,
            Wood, MetalGrey, MetalDark, Fabric, FabricLight, Plastic, Screen, Paper, Glass, Plant, Terracotta, VendingRed, BookA, BookB, BookC, LedGreen, LedAmber, WindowGlass, Asphalt, DistantBuilding, Skybox, PerkOrb, Stain, PosterA, PosterB, PosterC, FlashSprite, Smoke, SparkGlow,
            // District
            Brick, Stucco, MetalPanel, Pavers, Grass, Dirt, DarkGlass, LitWindow, NeonRed, NeonBlue, NeonAmber, PaintWhite, PaintYellow, Fence, LampHead, Awning,
            // Cars
            CarGlass, Tyre, Chrome, CarInterior, Headlight, Taillight, Skin,
            // Nature: the leaf atlas (outdoor wind and the still indoor version), bark, mulch, glazed pots
            Foliage, FoliageCanopy, FoliageIndoor, Bark, Birch, Mulch, Ceramic, CeramicDark;
        public Material[] CarPaints;

        // Prefabs
        public GameObject MuzzleFlashPrefab, MuzzleSmokePrefab, TracerPrefab, ImpactPrefab, BloodImpactPrefab;
        public GameObject ShellCasingPrefab;
        public GameObject SedanPrefab, VanPrefab;
        public GameObject SmgViewModel, PistolViewModel;
        public GameObject ZombiePrefab, VentPrefab, PlayerPrefab, PerkPickupPrefab;
    }
}
