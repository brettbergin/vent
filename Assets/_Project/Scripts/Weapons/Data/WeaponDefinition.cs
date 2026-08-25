using UnityEngine;
using Vent.Core.Audio;

namespace Vent.Weapons.Data
{
    public enum WeaponSlot
    {
        Primary = 0,
        Secondary = 1,
    }

    public enum FireMode
    {
        /// <summary>Fires continuously while the trigger is held.</summary>
        Automatic,
        /// <summary>Fires once per trigger pull.</summary>
        SemiAutomatic,
    }

    /// <summary>
    /// Static, designer-authored description of a weapon: everything about it that does not
    /// change during a run. Runtime state (ammo, level, heat) lives in <see cref="Runtime.Weapon"/>.
    /// Level scaling is described by the referenced <see cref="WeaponLevelCurve"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Vent/Weapons/Weapon Definition", fileName = "Weapon_")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Weapon";
        [SerializeField] private WeaponSlot slot = WeaponSlot.Primary;
        [SerializeField] private FireMode fireMode = FireMode.Automatic;

        [Header("Ballistics (base, before level scaling)")]
        [SerializeField, Min(0f)] private float damage = 20f;
        [SerializeField, Min(1f)] private float roundsPerMinute = 600f;
        [SerializeField, Min(1f)] private float range = 80f;

        [Header("Ammo")]
        [SerializeField, Min(1)] private int magazineSize = 30;
        [SerializeField, Min(0)] private int startingReserve = 120;
        [SerializeField, Min(0)] private int maxReserve = 240;
        [SerializeField, Min(0.1f), Tooltip("Tactical reload (a round still chambered): swap the magazine.")]
        private float reloadSeconds = 1.8f;
        [SerializeField, Min(0.1f), Tooltip("Empty reload: swap the magazine, then rack the action.")]
        private float emptyReloadSeconds = 2.4f;
        [SerializeField, Min(0.05f)] private float drawSeconds = 0.35f;

        [Header("Damage falloff (metres)")]
        [SerializeField, Min(0f)] private float falloffStart = 18f;
        [SerializeField, Min(0f)] private float falloffEnd = 45f;
        [SerializeField, Range(0.05f, 1f)] private float minDamageScale = 0.55f;

        [Header("Spread (degrees)")]
        [SerializeField, Min(0f)] private float baseSpread = 0.6f;
        [SerializeField, Min(0f), Tooltip("Extra spread at full movement factor.")]
        private float movementSpread = 2.5f;
        [SerializeField, Min(0f), Tooltip("Bloom added per shot.")]
        private float spreadPerShot = 0.35f;
        [SerializeField, Min(0f)] private float maxBloom = 4f;
        [SerializeField, Min(0f), Tooltip("Bloom recovery sharpness (per second).")]
        private float spreadRecovery = 9f;
        [SerializeField, Range(0.05f, 1f), Tooltip("Spread multiplier while aiming.")]
        private float aimSpreadScale = 0.35f;

        [Header("Recoil (degrees per shot)")]
        [SerializeField] private Vector2 verticalKickRange = new(0.6f, 1.0f);
        [SerializeField] private Vector2 horizontalKickRange = new(-0.35f, 0.35f);
        [SerializeField, Range(0.05f, 1f)] private float aimRecoilScale = 0.6f;
        [SerializeField, Min(1), Tooltip("Shots of sustained fire over which recoil climbs to its maximum.")]
        private int recoilRampShots = 8;
        [SerializeField, Min(1f), Tooltip("Recoil multiplier once the ramp is complete.")]
        private float recoilRampMultiplier = 1.8f;
        [SerializeField, Min(0f), Tooltip("Seconds without firing before the ramp resets.")]
        private float recoilRampReset = 0.3f;

        [Header("Presentation")]
        [SerializeField] private GameObject viewModelPrefab;
        [SerializeField] private GameObject muzzleFlashPrefab;
        [SerializeField] private GameObject tracerPrefab;
        [SerializeField] private GameObject impactPrefab;
        [SerializeField] private GameObject bloodImpactPrefab;
        [SerializeField] private GameObject shellCasingPrefab;
        [SerializeField, Min(0.1f)] private float muzzleFlashScale = 1f;
        [SerializeField] private SoundId fireSound = SoundId.SmgShot;
        [SerializeField, Range(0f, 1f)] private float fireVolume = 0.8f;

        [Header("Progression")]
        [SerializeField] private WeaponLevelCurve levelCurve;

        public string DisplayName => displayName;
        public WeaponSlot Slot => slot;
        public FireMode FireMode => fireMode;
        public float Damage => damage;
        public float RoundsPerMinute => roundsPerMinute;
        public float Range => range;
        public int MagazineSize => magazineSize;
        public int StartingReserve => startingReserve;
        public int MaxReserve => maxReserve;
        public float ReloadSeconds => reloadSeconds;
        public float EmptyReloadSeconds => Mathf.Max(reloadSeconds, emptyReloadSeconds);
        public float DrawSeconds => drawSeconds;
        public float FalloffStart => falloffStart;
        public float FalloffEnd => falloffEnd;
        public float MinDamageScale => minDamageScale;
        public int RecoilRampShots => recoilRampShots;
        public float RecoilRampMultiplier => recoilRampMultiplier;
        public float RecoilRampReset => recoilRampReset;
        public float BaseSpread => baseSpread;
        public float MovementSpread => movementSpread;
        public float SpreadPerShot => spreadPerShot;
        public float MaxBloom => maxBloom;
        public float SpreadRecovery => spreadRecovery;
        public float AimSpreadScale => aimSpreadScale;
        public Vector2 VerticalKickRange => verticalKickRange;
        public Vector2 HorizontalKickRange => horizontalKickRange;
        public float AimRecoilScale => aimRecoilScale;
        public GameObject ViewModelPrefab => viewModelPrefab;
        public GameObject MuzzleFlashPrefab => muzzleFlashPrefab;
        public GameObject TracerPrefab => tracerPrefab;
        public GameObject ImpactPrefab => impactPrefab;
        public GameObject BloodImpactPrefab => bloodImpactPrefab;
        public GameObject ShellCasingPrefab => shellCasingPrefab;
        public float MuzzleFlashScale => muzzleFlashScale;
        public SoundId FireSound => fireSound;
        public float FireVolume => fireVolume;
        public WeaponLevelCurve LevelCurve => levelCurve;

        /// <summary>
        /// Bulk configuration used by the editor asset factory. Runtime code never calls this;
        /// it exists so the shipped weapons are defined in code and regenerated deterministically.
        /// </summary>
        public void Configure(
            string name, WeaponSlot weaponSlot, FireMode mode,
            float baseDamage, float rpm, int magSize, int reserve, int reserveCap, float reload, float draw,
            float spreadBase, float spreadMove, float spreadShot, float bloomMax, float recovery, float aimScale,
            Vector2 vKick, Vector2 hKick, SoundId sound, WeaponLevelCurve curve, float weaponRange = 80f)
        {
            displayName = name;
            slot = weaponSlot;
            fireMode = mode;
            damage = baseDamage;
            roundsPerMinute = rpm;
            magazineSize = magSize;
            startingReserve = reserve;
            maxReserve = reserveCap;
            reloadSeconds = reload;
            drawSeconds = draw;
            baseSpread = spreadBase;
            movementSpread = spreadMove;
            spreadPerShot = spreadShot;
            maxBloom = bloomMax;
            spreadRecovery = recovery;
            aimSpreadScale = aimScale;
            verticalKickRange = vKick;
            horizontalKickRange = hKick;
            fireSound = sound;
            levelCurve = curve;
            range = weaponRange;
        }

        /// <summary>Handling feel: reload timing, falloff, recoil climb, flash size. Editor factory only.</summary>
        public void ConfigureHandling(float emptyReload, float falloffStartMetres, float falloffEndMetres, float minDamage,
            int rampShots, float rampMultiplier, float flashScale)
        {
            emptyReloadSeconds = emptyReload;
            falloffStart = falloffStartMetres;
            falloffEnd = falloffEndMetres;
            minDamageScale = minDamage;
            recoilRampShots = rampShots;
            recoilRampMultiplier = rampMultiplier;
            muzzleFlashScale = flashScale;
        }

        public void SetPresentation(GameObject viewModel, GameObject muzzleFlash, GameObject tracer, GameObject impact, GameObject bloodImpact,
            GameObject shellCasing = null)
        {
            viewModelPrefab = viewModel;
            muzzleFlashPrefab = muzzleFlash;
            tracerPrefab = tracer;
            impactPrefab = impact;
            bloodImpactPrefab = bloodImpact;
            shellCasingPrefab = shellCasing;
        }
    }
}
