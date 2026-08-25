using UnityEngine;
using UnityEngine.UIElements;
using Vent.Core;
using Vent.Core.Events;
using Vent.Core.Utility;

namespace Vent.UI.Screens
{
    /// <summary>
    /// In-game overlay. Purely reactive: every event channel writes into a <see cref="HudViewModel"/>
    /// and the document's runtime data bindings (see <c>Hud.uxml</c>) push the values to the elements.
    /// The few animated pieces (vignette, hit marker, damage direction, crosshair spread) decay in
    /// Update and are written to the same model. Only class toggles, which have no binding, touch
    /// elements directly.
    /// </summary>
    public sealed class HudScreen : UIScreen
    {
        [Header("Events in")]
        [SerializeField] private HealthEventChannel healthChanged;
        [SerializeField] private WeaponHudEventChannel weaponChanged;
        [SerializeField] private WeaponLevelUpEventChannel weaponLeveled;
        [SerializeField] private BoolEventChannel hitConfirmed;
        [SerializeField] private LevelEventChannel levelChanged;
        [SerializeField] private IntEventChannel killsThisLevelChanged;

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float crosshairPixelsPerDegree = 22f;
        [SerializeField, Min(0f)] private float crosshairMinGap = 5f;

        private readonly HudViewModel model = new();

        private VisualElement ammo, hitmarker, banner, toast;
        private VisualElement[] slots;

        private float vignetteFlash;
        private float healthNormalized = 1f;
        private float hitmarkerAlpha;
        private float damageAlpha;
        private Vector3 damageSourceDir;
        private int killsRequired = 1;
        private float spreadDegrees;
        private IVisualElementScheduledItem bannerHide, toastHide;

        /// <summary>The bound data source; exposed for tests.</summary>
        public HudViewModel Model => model;

        public void Configure(HealthEventChannel health, WeaponHudEventChannel weapon, WeaponLevelUpEventChannel weaponLevel,
            BoolEventChannel hit, LevelEventChannel level, IntEventChannel kills)
        {
            healthChanged = health;
            weaponChanged = weapon;
            weaponLeveled = weaponLevel;
            hitConfirmed = hit;
            levelChanged = level;
            killsThisLevelChanged = kills;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            healthChanged?.Subscribe(OnHealth);
            weaponChanged?.Subscribe(OnWeapon);
            weaponLeveled?.Subscribe(OnWeaponLeveled);
            hitConfirmed?.Subscribe(OnHit);
            levelChanged?.Subscribe(OnLevel);
            killsThisLevelChanged?.Subscribe(OnKills);
        }

        protected override void OnDisable()
        {
            healthChanged?.Unsubscribe(OnHealth);
            weaponChanged?.Unsubscribe(OnWeapon);
            weaponLeveled?.Unsubscribe(OnWeaponLeveled);
            hitConfirmed?.Unsubscribe(OnHit);
            levelChanged?.Unsubscribe(OnLevel);
            killsThisLevelChanged?.Unsubscribe(OnKills);
            base.OnDisable();
        }

        protected override void Bind(VisualElement r)
        {
            r.dataSource = model;

            ammo = r.Q<VisualElement>("ammo");
            hitmarker = r.Q<VisualElement>("hitmarker");
            banner = r.Q<VisualElement>("banner");
            toast = r.Q<VisualElement>("toast");
            slots = new[] { r.Q<VisualElement>("slot-0"), r.Q<VisualElement>("slot-1") };
        }

        protected override void Unbind()
        {
            if (Root != null)
            {
                Root.dataSource = null;
            }
        }

        protected override void OnShown()
        {
            if (CurrentState == GameState.Playing)
            {
                hitmarkerAlpha = 0f;
                damageAlpha = 0f;
            }
        }

        private void Update()
        {
            if (!IsVisible)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;

            vignetteFlash = MathUtil.Damp(vignetteFlash, 0f, 4f, dt);
            float lowHealth = Mathf.Pow(1f - healthNormalized, 2f) * 0.55f;
            model.VignetteOpacity = Mathf.Clamp01(lowHealth + vignetteFlash);

            hitmarkerAlpha = MathUtil.Damp(hitmarkerAlpha, 0f, 9f, dt);
            model.HitmarkerOpacity = hitmarkerAlpha;

            damageAlpha = MathUtil.Damp(damageAlpha, 0f, 2.5f, dt);
            model.DamageIndicatorOpacity = damageAlpha;
            if (damageAlpha > 0.01f && Camera.main != null)
            {
                Vector3 forward = Camera.main.transform.forward;
                forward.y = 0f;
                float angle = Vector3.SignedAngle(forward.normalized, damageSourceDir, Vector3.up);
                model.DamageIndicatorRotation = new Rotate(angle);
            }

            float gap = crosshairMinGap + spreadDegrees * crosshairPixelsPerDegree;
            model.CrosshairNear = -(gap + 10f);
            model.CrosshairFar = gap;
        }

        // ---------------------------------------------------------------- handlers

        private void OnHealth(HealthInfo info)
        {
            EnsureBound();
            healthNormalized = info.Normalized;
            model.HealthText = Mathf.CeilToInt(info.Current).ToString();
            model.HealthFillWidth = Length.Percent(info.Normalized * 100f);

            if (info.Delta < 0f)
            {
                vignetteFlash = Mathf.Min(1f, vignetteFlash + (-info.Delta / info.Max) * 2.5f);
                if (info.SourceDirection.sqrMagnitude > 0.01f)
                {
                    damageSourceDir = info.SourceDirection;
                    damageAlpha = 1f;
                }
            }
        }

        private void OnWeapon(WeaponHudInfo info)
        {
            EnsureBound();
            model.WeaponName = info.Name.ToUpperInvariant();
            model.AmmoMagText = info.Magazine.ToString();
            model.AmmoReserveText = $"/ {info.Reserve}";
            model.ReloadingText = info.Reloading ? "RELOADING" : string.Empty;
            model.WeaponLevelText = $"LV {info.Level}";
            model.XpFillWidth = Length.Percent(info.LevelProgress * 100f);
            spreadDegrees = info.Spread;

            ammo?.EnableInClassList("ammo--empty", info.Magazine == 0);
            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    slots[i]?.EnableInClassList("slot--active", i == info.SlotIndex);
                }
            }
        }

        private void OnWeaponLeveled(WeaponLevelUpInfo info)
        {
            EnsureBound();
            model.ToastText = $"{info.WeaponName.ToUpperInvariant()} LEVEL {info.NewLevel}";
            if (toast == null)
            {
                return;
            }

            toast.AddToClassList("toast--visible");
            toastHide?.Pause();
            toastHide = toast.schedule.Execute(() => toast.RemoveFromClassList("toast--visible")).StartingIn(1800);
        }

        private void OnHit(bool headshot)
        {
            EnsureBound();
            hitmarkerAlpha = 1f;
            hitmarker?.EnableInClassList("hitmarker--head", headshot);
        }

        private void OnLevel(LevelInfo info)
        {
            EnsureBound();
            killsRequired = info.KillsRequired;
            model.LevelText = $"LEVEL {info.Level}";
            model.KillsText = $"0 / {killsRequired}";

            if (info.Level > 1 && banner != null)
            {
                model.BannerTitle = $"LEVEL {info.Level}";
                model.BannerSub = "AMMO RESTOCKED";
                banner.AddToClassList("banner--visible");
                bannerHide?.Pause();
                bannerHide = banner.schedule.Execute(() => banner.RemoveFromClassList("banner--visible")).StartingIn(2200);
            }
        }

        private void OnKills(int kills)
        {
            EnsureBound();
            model.KillsText = $"{kills} / {killsRequired}";
        }
    }
}
