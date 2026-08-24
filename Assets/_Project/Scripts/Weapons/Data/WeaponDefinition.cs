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
        [SerializeField, Min(0.1f)] private float reloadSeconds = 1.8f;
        [SerializeField, Min(0.05f)] private float drawSeconds = 0.35f;

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

        [Header("Presentation")]
        [SerializeField] private GameObject viewModelPrefab;
        [SerializeField] private GameObject muzzleFlashPrefab;
        [SerializeField] private GameObject tracerPrefab;
        [SerializeField] private GameObject impactPrefab;
        [SerializeField] private GameObject bloodImpactPrefab;
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
        public float DrawSeconds => drawSeconds;
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

        public void SetPresentation(GameObject viewModel, GameObject muzzleFlash, GameObject tracer, GameObject impact, GameObject bloodImpact)
        {
            viewModelPrefab = viewModel;
            muzzleFlashPrefab = muzzleFlash;
            tracerPrefab = tracer;
            impactPrefab = impact;
            bloodImpactPrefab = bloodImpact;
        }
    }
}
