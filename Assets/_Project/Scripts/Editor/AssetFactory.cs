using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Vent.Core.Audio;
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
    /// Creates (or refreshes) every ScriptableObject and material the game needs. Assets are
    /// looked up by path first so re-running never breaks references: the GUID of an existing
    /// asset is preserved and only its contents are rewritten.
    /// </summary>
    public static class AssetFactory
    {
        [MenuItem("Vent/2. Generate Data & Materials")]
        public static void GenerateMenu()
        {
            ProjectBootstrap.EnsureFolders();
            CreateAll();
            AssetDatabase.SaveAssets();
            Debug.Log("[Vent] Data & materials generated.");
        }

        public static GameAssets CreateAll()
        {
            var a = new GameAssets();

            // ---- Event channels -------------------------------------------------------
            a.Kill = Event<KillEventChannel>("Evt_ZombieKilled", "Raised by a zombie when it dies. Payload: position, killer, headshot, XP.");
            a.Level = Event<LevelEventChannel>("Evt_LevelChanged", "Raised by the LevelDirector when the level changes (including level 1 at run start).");
            a.KillsThisLevel = Event<IntEventChannel>("Evt_KillsThisLevel", "Kills accumulated toward the next level.");
            a.Health = Event<HealthEventChannel>("Evt_PlayerHealth", "Raised on every player health change.");
            a.PlayerDied = Event<VoidEventChannel>("Evt_PlayerDied", "Raised once when player health reaches zero.");
            a.WeaponHud = Event<WeaponHudEventChannel>("Evt_WeaponHud", "Snapshot of the active weapon for the HUD.");
            a.WeaponLevelUp = Event<WeaponLevelUpEventChannel>("Evt_WeaponLevelUp", "A weapon gained a level.");
            a.Hit = Event<BoolEventChannel>("Evt_HitConfirmed", "A shot damaged something. Payload: headshot.");
            a.Noise = Event<NoiseEventChannel>("Evt_Noise", "A gunshot. Zombies within their hearing radius are alerted.");
            a.PerkCollected = Event<PerkEventChannel>("Evt_PerkCollected", "The player picked up a perk orb. Payload: kind, duration.");
            a.GameState = Event<GameStateEventChannel>("Evt_GameState", "Application state changed.");
            a.RunSummary = Event<RunSummaryEventChannel>("Evt_RunEnded", "Final tally for the game-over screen.");
            a.BestLevel = Event<IntEventChannel>("Evt_BestLevel", "Best level on record; raised when the menu opens.");
            a.PlayRequested = Event<VoidEventChannel>("Evt_PlayRequested", "UI → GameManager: start a run.");
            a.ResumeRequested = Event<VoidEventChannel>("Evt_ResumeRequested", "UI → GameManager: unpause.");
            a.RestartRequested = Event<VoidEventChannel>("Evt_RestartRequested", "UI → GameManager: restart the run.");
            a.MenuRequested = Event<VoidEventChannel>("Evt_MenuRequested", "UI → GameManager: back to the main menu.");
            a.QuitRequested = Event<VoidEventChannel>("Evt_QuitRequested", "UI → GameManager: quit the application.");
            a.Prompt = Event<StringEventChannel>("Evt_Prompt", "HUD interaction prompt (\"[E] OPEN DOOR\"); an empty string hides it. Raised by PlayerInteractor and the vehicle driver.");
            a.Announcement = Event<StringEventChannel>("Evt_Announcement", "Centre-screen banner: \"TITLE\\nSUBTITLE\". Raised by world events such as the front door unlocking.");
            a.Objective = Event<StringEventChannel>("Evt_Objective", "The key hunt's current step, shown as a standing line on the HUD; an empty string hides it.");
            a.KeyFound = Event<VoidEventChannel>("Evt_KeyFound", "The front door key was taken from a desk drawer. The front door listens so it knows the player is carrying one.");
            a.VehicleSpeed = Event<FloatEventChannel>("Evt_VehicleSpeed", "Driven car speed in km/h while the player drives; -1 when they get out.");

            // ---- Runtime sets ----------------------------------------------------------
            a.Zombies = GetOrCreate<ZombieRuntimeSet>($"{Paths.Data}/Set_Zombies.asset");
            a.Vents = GetOrCreate<VentRuntimeSet>($"{Paths.Data}/Set_Vents.asset");

            // ---- Tuning data -----------------------------------------------------------
            a.Difficulty = GetOrCreate<DifficultyProfile>($"{Paths.Data}/DifficultyProfile.asset", d => d.ApplyDefaults());
            a.Zombie = GetOrCreate<ZombieDefinition>($"{Paths.Data}/Zombie.asset");
            a.WeaponLevels = GetOrCreate<WeaponLevelCurve>($"{Paths.Data}/WeaponLevels_Standard.asset", c => c.ApplyDefaults(25));
            a.PerkDrops = GetOrCreate<PerkDropTable>($"{Paths.Data}/PerkDrops.asset", t => t.ApplyDefaults());
            foreach (VehicleShape shape in Enum.GetValues(typeof(VehicleShape)))
            {
                VehicleShape captured = shape;
                a.SetVehicle(shape, GetOrCreate<VehicleDefinition>($"{Paths.Data}/Vehicle_{shape}.asset", v => v.ApplyDefaults(captured)));
            }

            a.Smg = GetOrCreate<WeaponDefinition>($"{Paths.Data}/Weapon_SMG.asset", w => w.Configure(
                "SMG", WeaponSlot.Primary, FireMode.Automatic,
                baseDamage: 22f, rpm: 720f, magSize: 30, reserve: 150, reserveCap: 240, reload: 1.9f, draw: 0.4f,
                spreadBase: 0.7f, spreadMove: 2.2f, spreadShot: 0.28f, bloomMax: 3.5f, recovery: 9f, aimScale: 0.4f,
                vKick: new Vector2(0.45f, 0.8f), hKick: new Vector2(-0.35f, 0.35f),
                sound: SoundId.SmgShot, curve: a.WeaponLevels));
            a.Smg.ConfigureHandling(emptyReload: 2.5f, falloffStartMetres: 16f, falloffEndMetres: 40f, minDamage: 0.55f,
                rampShots: 8, rampMultiplier: 1.9f, flashScale: 1f);

            a.Pistol = GetOrCreate<WeaponDefinition>($"{Paths.Data}/Weapon_Pistol.asset", w => w.Configure(
                "Pistol", WeaponSlot.Secondary, FireMode.SemiAutomatic,
                baseDamage: 38f, rpm: 400f, magSize: 12, reserve: 72, reserveCap: 120, reload: 1.3f, draw: 0.3f,
                spreadBase: 0.35f, spreadMove: 1.6f, spreadShot: 0.6f, bloomMax: 3f, recovery: 11f, aimScale: 0.3f,
                vKick: new Vector2(1.2f, 1.8f), hKick: new Vector2(-0.5f, 0.5f),
                sound: SoundId.PistolShot, curve: a.WeaponLevels));
            a.Pistol.ConfigureHandling(emptyReload: 1.8f, falloffStartMetres: 12f, falloffEndMetres: 30f, minDamage: 0.6f,
                rampShots: 4, rampMultiplier: 1.35f, flashScale: 0.7f);

            // ---- Input -----------------------------------------------------------------
            a.InputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(Paths.InputActions);
            if (a.InputActions == null)
            {
                throw new InvalidOperationException($"Input actions asset not found at {Paths.InputActions}");
            }

            a.InputReader = GetOrCreate<InputReader>($"{Paths.Data}/InputReader.asset", r =>
            {
                var so = new SerializedObject(r);
                so.FindProperty("actions").objectReferenceValue = a.InputActions;
                so.ApplyModifiedPropertiesWithoutUndo();
            });

            // ---- UI panel --------------------------------------------------------------
            a.PanelSettings = CreatePanelSettings();

            // ---- Textures --------------------------------------------------------------
            TextureFactory.TextureSet drywall = TextureFactory.Drywall();
            TextureFactory.TextureSet ceilingTile = TextureFactory.CeilingTile();
            TextureFactory.TextureSet vinyl = TextureFactory.VinylFloor();
            TextureFactory.TextureSet wood = TextureFactory.Wood();
            TextureFactory.TextureSet concrete = TextureFactory.Concrete();
            TextureFactory.TextureSet asphalt = TextureFactory.Asphalt();
            TextureFactory.TextureSet brushed = TextureFactory.BrushedMetal();
            TextureFactory.TextureSet fabric = TextureFactory.Fabric();
            TextureFactory.TextureSet brick = TextureFactory.Brick();
            TextureFactory.TextureSet stucco = TextureFactory.Stucco();
            TextureFactory.TextureSet metalPanel = TextureFactory.MetalPanel();
            TextureFactory.TextureSet pavers = TextureFactory.Pavers();
            TextureFactory.TextureSet grass = TextureFactory.Grass();
            TextureFactory.TextureSet dirt = TextureFactory.Dirt();
            TextureFactory.TextureSet bark = TextureFactory.Bark();
            TextureFactory.TextureSet birch = TextureFactory.Birch();
            TextureFactory.TextureSet mulch = TextureFactory.Mulch();
            FoliageTextureFactory.Result foliage = FoliageTextureFactory.Atlas();

            // ---- Materials -------------------------------------------------------------
            // Base colours tint the (near-white) albedo textures.
            a.Floor = Lit("M_Floor", new Color(0.55f, 0.56f, 0.58f), smoothness: 0.45f, tex: vinyl);
            a.Wall = Lit("M_Wall", new Color(0.80f, 0.78f, 0.72f), smoothness: 0.12f, tex: drywall);
            a.Ceiling = Lit("M_Ceiling", new Color(0.74f, 0.75f, 0.75f), smoothness: 0.05f, tex: ceilingTile);
            a.Trim = Lit("M_Trim", new Color(0.12f, 0.12f, 0.13f), smoothness: 0.3f);
            a.Prop = Lit("M_Prop", new Color(0.45f, 0.36f, 0.25f), smoothness: 0.2f);
            a.PropAlt = Lit("M_PropAlt", new Color(0.30f, 0.38f, 0.32f), smoothness: 0.35f, metallic: 0.3f);
            a.VentMetal = Lit("M_VentMetal", new Color(0.20f, 0.21f, 0.22f), smoothness: 0.6f, metallic: 0.8f);
            a.LightPanel = Lit("M_LightPanel", new Color(0.9f, 0.9f, 0.85f), smoothness: 0.5f, emission: new Color(1.0f, 0.95f, 0.8f) * 2.2f);
            a.ZombieSkin = Lit("M_ZombieSkin", new Color(0.42f, 0.55f, 0.36f), smoothness: 0.35f);
            a.ZombieHead = Lit("M_ZombieHead", new Color(0.48f, 0.58f, 0.40f), smoothness: 0.4f);
            a.ZombieClothes = Lit("M_ZombieClothes", new Color(0.16f, 0.15f, 0.17f), smoothness: 0.08f);
            a.ZombieGore = Lit("M_ZombieGore", new Color(0.35f, 0.04f, 0.04f), smoothness: 0.55f);
            a.ZombieEye = Lit("M_ZombieEye", new Color(0.9f, 0.85f, 0.6f), smoothness: 0.7f, emission: new Color(0.9f, 0.5f, 0.15f) * 1.4f);
            a.HealthBarTrack = UnlitTransparent("M_HealthBarTrack", new Color(0.05f, 0.05f, 0.05f, 0.8f));
            a.HealthBarFill = UnlitTransparent("M_HealthBarFill", new Color(0.35f, 0.85f, 0.35f, 1f));
            a.GunMetal = Lit("M_GunMetal", new Color(0.16f, 0.17f, 0.19f), smoothness: 0.55f, metallic: 0.35f);
            a.GunAccent = Lit("M_GunAccent", new Color(0.55f, 0.45f, 0.30f), smoothness: 0.4f, metallic: 0.2f);
            a.GunPolymer = Lit("M_GunPolymer", new Color(0.09f, 0.09f, 0.10f), smoothness: 0.28f, metallic: 0f);
            a.GunSteel = Lit("M_GunSteel", new Color(0.30f, 0.31f, 0.34f), smoothness: 0.72f, metallic: 0.85f);
            a.Brass = Lit("M_Brass", new Color(0.78f, 0.62f, 0.28f), smoothness: 0.8f, metallic: 0.9f);
            // The key lies in a shadowed drawer with nothing to reflect: polished metal would go black there, so it is a
            // warmer, matter brass with a faint glow of its own.
            a.KeyBrass = Lit("M_KeyBrass", new Color(0.95f, 0.78f, 0.35f), smoothness: 0.55f, metallic: 0.35f, emission: new Color(0.9f, 0.7f, 0.3f) * 0.35f);
            a.Concrete = Lit("M_Concrete", new Color(0.62f, 0.62f, 0.62f), smoothness: 0.1f, tex: concrete);
            // Furniture
            a.Wood = Lit("M_Wood", new Color(0.70f, 0.58f, 0.46f), smoothness: 0.45f, tex: wood);
            a.MetalGrey = Lit("M_MetalGrey", new Color(0.62f, 0.63f, 0.65f), smoothness: 0.6f, metallic: 0.7f, tex: brushed);
            a.MetalDark = Lit("M_MetalDark", new Color(0.14f, 0.14f, 0.15f), smoothness: 0.5f, metallic: 0.6f);
            a.Fabric = Lit("M_Fabric", new Color(0.22f, 0.29f, 0.44f), smoothness: 0.05f, tex: fabric);
            a.FabricLight = Lit("M_FabricLight", new Color(0.66f, 0.67f, 0.62f), smoothness: 0.05f, tex: fabric);
            a.Plastic = Lit("M_Plastic", new Color(0.08f, 0.08f, 0.09f), smoothness: 0.35f);
            a.Screen = Lit("M_Screen", new Color(0.02f, 0.03f, 0.05f), smoothness: 0.9f, emission: new Color(0.15f, 0.35f, 0.6f) * 0.9f);
            a.Paper = Lit("M_Paper", new Color(0.86f, 0.86f, 0.82f), smoothness: 0.15f);
            a.Glass = Lit("M_Glass", new Color(0.55f, 0.7f, 0.75f), smoothness: 0.95f, metallic: 0.2f);
            a.Plant = Lit("M_Plant", new Color(0.16f, 0.38f, 0.14f), smoothness: 0.3f);
            a.Terracotta = Lit("M_Terracotta", new Color(0.55f, 0.32f, 0.22f), smoothness: 0.2f);
            a.VendingRed = Lit("M_VendingRed", new Color(0.62f, 0.10f, 0.10f), smoothness: 0.6f);
            a.BookA = Lit("M_BookA", new Color(0.55f, 0.15f, 0.12f), smoothness: 0.3f);
            a.BookB = Lit("M_BookB", new Color(0.12f, 0.25f, 0.45f), smoothness: 0.3f);
            a.BookC = Lit("M_BookC", new Color(0.22f, 0.40f, 0.22f), smoothness: 0.3f);
            a.LedGreen = Lit("M_LedGreen", new Color(0.2f, 0.9f, 0.3f), smoothness: 0.5f, emission: new Color(0.2f, 1f, 0.3f) * 2.5f);
            a.LedAmber = Lit("M_LedAmber", new Color(0.9f, 0.6f, 0.1f), smoothness: 0.5f, emission: new Color(1f, 0.6f, 0.1f) * 2.5f);
            // Key hunt. Not emissive: the coil carries its own small light, and making the jacket
            // glow as well washed it out to a white blob that read as a desk lamp rather than as a
            // blue coil of cable. The light does the "findable" job; this just has to look like cable.
            a.CableBlue = Lit("M_CableBlue", new Color(0.10f, 0.35f, 0.75f), smoothness: 0.4f);
            a.WhiteboardHint = LitImage("M_WhiteboardHint", TextureFactory.WhiteboardHint(), smoothness: 0.5f);
            // Windows & exterior
            // Grime under vents and on ceiling tiles: dark, translucent, matte.
            a.Stain = LitTransparent("M_Stain", new Color(0.10f, 0.08f, 0.06f, 0.32f), smoothness: 0.02f);
            a.PosterA = Lit("M_PosterA", new Color(0.85f, 0.25f, 0.20f), smoothness: 0.2f);
            a.PosterB = Lit("M_PosterB", new Color(0.20f, 0.45f, 0.75f), smoothness: 0.2f);
            a.PosterC = Lit("M_PosterC", new Color(0.90f, 0.75f, 0.25f), smoothness: 0.2f);
            a.WindowGlass = LitTransparent("M_WindowGlass", new Color(0.6f, 0.75f, 0.85f, 0.22f), smoothness: 0.97f);
            a.Asphalt = Lit("M_Asphalt", new Color(0.16f, 0.16f, 0.18f), smoothness: 0.15f, tex: asphalt);
            a.DistantBuilding = Lit("M_DistantBuilding", new Color(0.10f, 0.10f, 0.13f), smoothness: 0.2f);
            a.Skybox = Skybox("M_Skybox");
            // Tinted per perk at runtime through a MaterialPropertyBlock; the asset's white is a placeholder.
            a.PerkOrb = Lit("M_PerkOrb", Color.white, smoothness: 0.85f, metallic: 0.1f, emission: Color.white * 2.5f);
            // The district: facades, ground and street furniture.
            a.Brick = Lit("M_Brick", new Color(0.62f, 0.36f, 0.30f), smoothness: 0.15f, tex: brick);
            a.Stucco = Lit("M_Stucco", new Color(0.72f, 0.68f, 0.60f), smoothness: 0.1f, tex: stucco);
            a.MetalPanel = Lit("M_MetalPanel", new Color(0.55f, 0.57f, 0.60f), smoothness: 0.5f, metallic: 0.6f, tex: metalPanel);
            a.Pavers = Lit("M_Pavers", new Color(0.58f, 0.58f, 0.57f), smoothness: 0.12f, tex: pavers);
            a.Grass = Lit("M_Grass", new Color(0.30f, 0.42f, 0.20f), smoothness: 0.05f, tex: grass);
            a.Dirt = Lit("M_Dirt", new Color(0.36f, 0.30f, 0.24f), smoothness: 0.05f, tex: dirt);
            a.DarkGlass = Lit("M_DarkGlass", new Color(0.08f, 0.10f, 0.12f), smoothness: 0.92f, metallic: 0.3f);
            a.LitWindow = Lit("M_LitWindow", new Color(0.9f, 0.8f, 0.6f), smoothness: 0.6f, emission: new Color(1f, 0.85f, 0.6f) * 1.6f);
            a.NeonRed = Lit("M_NeonRed", new Color(0.9f, 0.15f, 0.1f), smoothness: 0.5f, emission: new Color(1f, 0.15f, 0.1f) * 3f);
            a.NeonBlue = Lit("M_NeonBlue", new Color(0.2f, 0.5f, 1f), smoothness: 0.5f, emission: new Color(0.2f, 0.5f, 1f) * 3f);
            a.NeonAmber = Lit("M_NeonAmber", new Color(1f, 0.7f, 0.2f), smoothness: 0.5f, emission: new Color(1f, 0.7f, 0.2f) * 3f);
            a.PaintWhite = Lit("M_PaintWhite", new Color(0.9f, 0.9f, 0.85f), smoothness: 0.2f);
            a.PaintYellow = Lit("M_PaintYellow", new Color(0.9f, 0.75f, 0.2f), smoothness: 0.2f);
            a.Fence = LitTransparent("M_Fence", new Color(0.5f, 0.5f, 0.52f, 0.35f), smoothness: 0.3f);
            a.LampHead = Lit("M_LampHead", new Color(0.9f, 0.85f, 0.7f), smoothness: 0.5f, emission: new Color(1f, 0.8f, 0.5f) * 3f);
            a.Awning = Lit("M_Awning", new Color(0.6f, 0.15f, 0.12f), smoothness: 0.05f, tex: fabric);
            // Cars: a handful of paints, glass, rubber, chrome, and the lamps.
            a.CarPaints = new[]
            {
                CarPaint("M_CarPaint_Red", new Color(0.62f, 0.06f, 0.06f)),
                CarPaint("M_CarPaint_Blue", new Color(0.08f, 0.16f, 0.45f)),
                CarPaint("M_CarPaint_White", new Color(0.88f, 0.88f, 0.86f)),
                CarPaint("M_CarPaint_Black", new Color(0.04f, 0.04f, 0.05f)),
                CarPaint("M_CarPaint_Silver", new Color(0.55f, 0.57f, 0.6f), metallic: 0.85f),
                CarPaint("M_CarPaint_Green", new Color(0.10f, 0.30f, 0.16f)),
            };
            a.CarTrim = Lit("M_CarTrim", new Color(0.06f, 0.06f, 0.065f), smoothness: 0.35f);
            a.CarUnderbody = Lit("M_CarUnderbody", new Color(0.025f, 0.025f, 0.028f), smoothness: 0.1f);
            a.Plate = Lit("M_Plate", new Color(0.92f, 0.92f, 0.88f), smoothness: 0.4f);
            a.CarGlass = LitTransparent("M_CarGlass", new Color(0.08f, 0.12f, 0.16f, 0.62f), smoothness: 0.97f);
            a.Tyre = Lit("M_Tyre", new Color(0.04f, 0.04f, 0.045f), smoothness: 0.3f);
            a.Chrome = Lit("M_Chrome", new Color(0.8f, 0.8f, 0.82f), smoothness: 0.85f, metallic: 0.95f);
            a.CarInterior = Lit("M_CarInterior", new Color(0.12f, 0.12f, 0.13f), smoothness: 0.2f);
            a.CarBodyPhysics = PhysicsMaterialAsset("PM_CarBody", friction: 0.25f, bounce: 0f);
            a.Headlight = Lit("M_Headlight", new Color(0.9f, 0.9f, 0.85f), smoothness: 0.8f, metallic: 0.2f, emission: new Color(1f, 0.95f, 0.8f) * 2.5f);
            a.Taillight = Lit("M_Taillight", new Color(0.6f, 0.05f, 0.05f), smoothness: 0.8f, metallic: 0.1f, emission: new Color(1f, 0.1f, 0.05f) * 2.5f);
            a.Skin = Lit("M_Skin", new Color(0.8f, 0.62f, 0.5f), smoothness: 0.35f);
            // Nature. One atlas, two winds: the street's, and the faint breath of the office air-conditioning.
            // Crowns, shrubs and hedges are hundreds of overlapping cards, so an edge-on card can fade out
            // without leaving a hole — and edge-on is exactly when a metre-wide card reads as a blade.
            // Ground cover keeps every card: leaf litter lies flat, so a player looking along the pavement
            // sees it near edge-on and a fade would erase it. Indoor plants are a few large single leaves.
            a.Foliage = Foliage("M_Foliage", foliage, windLean: 0.22f, windSpeed: 1.1f, flutter: 0.035f, translucency: 0.5f, grazingFade: 0f);
            a.FoliageCanopy = Foliage("M_FoliageCanopy", foliage, windLean: 0.22f, windSpeed: 1.1f, flutter: 0.035f, translucency: 0.5f, grazingFade: 0.6f);
            a.FoliageIndoor = Foliage("M_FoliageIndoor", foliage, windLean: 0.015f, windSpeed: 0.7f, flutter: 0.006f, translucency: 0.35f, grazingFade: 0f);
            a.Bark = Lit("M_Bark", new Color(0.55f, 0.48f, 0.40f), smoothness: 0.08f, tex: bark);
            a.Birch = Lit("M_Birch", new Color(0.92f, 0.90f, 0.86f), smoothness: 0.15f, tex: birch);
            a.Mulch = Lit("M_Mulch", new Color(0.50f, 0.44f, 0.36f), smoothness: 0.05f, tex: mulch);
            a.Ceramic = Lit("M_Ceramic", new Color(0.86f, 0.84f, 0.78f), smoothness: 0.78f);
            a.CeramicDark = Lit("M_CeramicDark", new Color(0.16f, 0.17f, 0.18f), smoothness: 0.72f);
            // Gunfire: additive sprites for the flash, the tracer core and sparks; alpha-blended smoke.
            Texture2D flashSprite = TextureFactory.MuzzleFlashSprite();
            Texture2D smokeSprite = TextureFactory.SmokeSprite();
            Texture2D sparkSprite = TextureFactory.SparkSprite();
            a.FlashSprite = ParticleTextured("M_MuzzleFlash", flashSprite, Color.white, additive: true);
            a.Smoke = ParticleTextured("M_Smoke", smokeSprite, new Color(0.6f, 0.6f, 0.62f, 0.55f), additive: false);
            a.SparkGlow = ParticleTextured("M_SparkGlow", sparkSprite, new Color(1f, 0.8f, 0.5f), additive: true);
            a.Tracer = ParticleTextured("M_Tracer", sparkSprite, new Color(1f, 0.85f, 0.55f), additive: true);
            a.Flash = Unlit("M_Flash", new Color(1f, 0.75f, 0.3f));
            a.Spark = ParticleTextured("M_Spark", sparkSprite, new Color(1f, 0.9f, 0.7f), additive: true);
            a.Blood = Particle("M_Blood", new Color(0.45f, 0.05f, 0.05f));

            return a;
        }

        // ------------------------------------------------------------------ helpers

        private static T Event<T>(string name, string description) where T : EventChannelBase
        {
            return GetOrCreate<T>($"{Paths.Events}/{name}.asset", e =>
            {
                var so = new SerializedObject(e);
                so.FindProperty("description").stringValue = description;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        /// <summary>Load the asset at <paramref name="path"/> or create it, then apply <paramref name="configure"/>.</summary>
        public static T GetOrCreate<T>(string path, Action<T> configure = null) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                ProjectBootstrap.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                AssetDatabase.CreateAsset(asset, path);
            }

            configure?.Invoke(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static PanelSettings CreatePanelSettings()
        {
            // UI Toolkit needs a theme; the default runtime theme is a one-line TSS import.
            string themeFullPath = Path.Combine(Directory.GetCurrentDirectory(), Paths.Theme);
            if (!File.Exists(themeFullPath))
            {
                File.WriteAllText(themeFullPath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(Paths.Theme, ImportAssetOptions.ForceSynchronousImport);
            }

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Paths.Theme);
            return GetOrCreate<PanelSettings>(Paths.PanelSettings, p =>
            {
                p.themeStyleSheet = theme;
                p.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                p.referenceResolution = new Vector2Int(1920, 1080);
                p.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                p.match = 0.5f;
                p.sortingOrder = 0;
            });
        }

        /// <summary>A physics material asset, created once and re-tuned on every regen.</summary>
        private static PhysicsMaterial PhysicsMaterialAsset(string name, float friction, float bounce)
        {
            string path = $"{Paths.Materials}/{name}.physicMaterial";
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m == null)
            {
                m = new PhysicsMaterial(name);
                ProjectBootstrap.EnsureFolder(Paths.Materials);
                AssetDatabase.CreateAsset(m, path);
            }

            m.dynamicFriction = friction;
            m.staticFriction = friction;
            m.bounciness = bounce;
            m.frictionCombine = PhysicsMaterialCombine.Minimum;
            m.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Lit(string name, Color baseColor, float smoothness = 0.3f, float metallic = 0f, Color? emission = null, TextureFactory.TextureSet tex = null)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (tex != null)
            {
                // UVs on building blocks are in metres (MeshLibrary); one repeat covers MetersPerTile of them.
                float tiling = 1f / tex.MetersPerTile;
                m.SetTexture("_BaseMap", tex.Albedo);
                m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                m.SetTexture("_BumpMap", tex.Normal);
                m.SetFloat("_BumpScale", tex.NormalStrength);
                m.EnableKeyword("_NORMALMAP");
            }
            else
            {
                m.SetTexture("_BaseMap", null);
                m.SetTexture("_BumpMap", null);
                m.DisableKeyword("_NORMALMAP");
            }

            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Vent/Foliage: cutout leaves from the atlas with wind, two-sided lighting and translucency (see the shader's header).</summary>
        private static Material Foliage(string name, FoliageTextureFactory.Result atlas, float windLean, float windSpeed, float flutter, float translucency, float grazingFade)
        {
            Material m = GetOrCreateMaterial(name, "Vent/Foliage");
            m.SetTexture("_BaseMap", atlas.Albedo);
            m.SetTexture("_BumpMap", atlas.Normal);
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_VariationColor", new Color(0.88f, 0.94f, 0.72f, 0.55f));
            m.SetFloat("_Cutoff", 0.45f);
            m.SetFloat("_GrazingFade", grazingFade);
            m.SetFloat("_Smoothness", 0.32f);
            m.SetFloat("_BumpScale", 1.0f);
            m.SetFloat("_OcclusionStrength", 1.0f);
            m.SetVector("_WindDirection", new Vector4(1f, 0f, 0.35f, 0f));
            m.SetFloat("_WindStrength", windLean);
            m.SetFloat("_WindSpeed", windSpeed);
            m.SetFloat("_WindGustScale", 0.12f);
            m.SetFloat("_FlutterStrength", flutter);
            m.SetFloat("_FlutterSpeed", 4.5f);
            m.SetColor("_TranslucencyColor", new Color(1f, 0.95f, 0.6f));
            m.SetFloat("_Translucency", translucency);
            m.SetFloat("_TranslucencyPower", 3f);
            m.SetFloat("_Wrap", 0.6f);
            m.SetFloat("_SkyFill", 0.6f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Unlit(string name, Color color)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Lit, alpha-blended: window glass.</summary>
        /// <summary>
        /// A lit material whose albedo is one image mapped 1:1 across the face — no world-scale
        /// tiling and no normal map, unlike <see cref="Lit"/>'s <c>TextureSet</c>. For the
        /// whiteboard, where the texture is a picture rather than a surface.
        /// </summary>
        private static Material LitImage(string name, Texture2D albedo, float smoothness)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetTexture("_BaseMap", albedo);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Car paint: a metallic base under a clear coat (URP Complex Lit), so the body carries a sharp sky reflection over a coloured sheen.</summary>
        private static Material CarPaint(string name, Color color, float metallic = 0.35f)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Complex Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.62f);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_ClearCoatMask", 1f);
            m.SetFloat("_ClearCoatSmoothness", 0.95f);
            m.EnableKeyword("_CLEARCOAT");
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material LitTransparent(string name, Color color, float smoothness)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0.1f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Procedural dusk sky; the sun disc follows <c>RenderSettings.sun</c>.</summary>
        private static Material Skybox(string name)
        {
            Material m = GetOrCreateMaterial(name, "Skybox/Procedural");
            m.SetFloat("_SunSize", 0.06f);
            m.SetFloat("_SunSizeConvergence", 4f);
            m.SetFloat("_AtmosphereThickness", 1.35f);
            m.SetColor("_SkyTint", new Color(0.55f, 0.4f, 0.5f));
            m.SetColor("_GroundColor", new Color(0.12f, 0.1f, 0.11f));
            m.SetFloat("_Exposure", 0.9f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Unlit with alpha blending: world-space UI such as health bars.</summary>
        private static Material UnlitTransparent(string name, Color color)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f); // transparent
            m.SetFloat("_Blend", 0f);   // alpha
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);    // both sides: the bar is a billboard
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Particles/Unlit with a sprite: additive (fire, glow) or alpha-blended (smoke). Double-sided, no depth write, soft against geometry.</summary>
        private static Material ParticleTextured(string name, Texture2D sprite, Color tint, bool additive)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", sprite);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_SoftParticlesEnabled", 1f);
            m.SetFloat("_SoftParticlesNearFadeDistance", 0f);
            m.SetFloat("_SoftParticlesFarFadeDistance", 0.3f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_SOFTPARTICLES_ON");
            m.SetInt("_SrcBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.SrcAlpha));
            m.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            if (additive) m.EnableKeyword("_ALPHAMODULATE_ON"); else m.DisableKeyword("_ALPHAMODULATE_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + (additive ? 10 : 0);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Particle(string name, Color color)
        {
            Material m = GetOrCreateMaterial(name, "Universal Render Pipeline/Particles/Unlit");
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material GetOrCreateMaterial(string name, string shaderName)
        {
            string path = $"{Paths.Materials}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader '{shaderName}' not found. Is URP installed?");
            }

            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }

            return m;
        }
    }
}
