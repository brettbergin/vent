using UnityEngine;
using UnityEngine.UIElements;
using Vent.Core;
using Vent.Core.Events;
using Vent.Core.Utility;

namespace Vent.UI.Screens
{
    /// <summary>
    /// In-game overlay. Purely reactive: every element is driven by an event channel, and the
    /// few animated pieces (vignette, hit marker, damage direction) decay in Update.
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

        private Label healthLabel, weaponName, ammoMag, ammoReserve, reloading, weaponLevel, levelLabel, killsLabel, bannerTitle, bannerSub, toastText;
        private VisualElement healthFill, xpFill, vignette, hitmarker, damageIndicator, banner, toast, ammo;
        private VisualElement chTop, chBottom, chLeft, chRight;
        private VisualElement[] slots;

        private float vignetteFlash;
        private float healthNormalized = 1f;
        private float hitmarkerAlpha;
        private float damageAlpha;
        private Vector3 damageSourceDir;
        private int killsRequired = 1;
        private float spreadDegrees;
        private IVisualElementScheduledItem bannerHide, toastHide;

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
            healthLabel = r.Q<Label>("health-label");
            healthFill = r.Q<VisualElement>("health-fill");
            weaponName = r.Q<Label>("weapon-name");
            ammo = r.Q<VisualElement>("ammo");
            ammoMag = r.Q<Label>("ammo-mag");
            ammoReserve = r.Q<Label>("ammo-reserve");
            reloading = r.Q<Label>("reloading");
            weaponLevel = r.Q<Label>("weapon-level");
            xpFill = r.Q<VisualElement>("xp-fill");
            levelLabel = r.Q<Label>("level-label");
            killsLabel = r.Q<Label>("kills-label");
            vignette = r.Q<VisualElement>("vignette");
            hitmarker = r.Q<VisualElement>("hitmarker");
            damageIndicator = r.Q<VisualElement>("damage-indicator");
            banner = r.Q<VisualElement>("banner");
            bannerTitle = r.Q<Label>("banner-title");
            bannerSub = r.Q<Label>("banner-sub");
            toast = r.Q<VisualElement>("toast");
            toastText = r.Q<Label>("toast-text");
            chTop = r.Q<VisualElement>("ch-top");
            chBottom = r.Q<VisualElement>("ch-bottom");
            chLeft = r.Q<VisualElement>("ch-left");
            chRight = r.Q<VisualElement>("ch-right");
            slots = new[] { r.Q<VisualElement>("slot-0"), r.Q<VisualElement>("slot-1") };
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
            if (vignette != null)
            {
                vignette.style.opacity = Mathf.Clamp01(lowHealth + vignetteFlash);
            }

            hitmarkerAlpha = MathUtil.Damp(hitmarkerAlpha, 0f, 9f, dt);
            if (hitmarker != null)
            {
                hitmarker.style.opacity = hitmarkerAlpha;
            }

            damageAlpha = MathUtil.Damp(damageAlpha, 0f, 2.5f, dt);
            if (damageIndicator != null)
            {
                damageIndicator.style.opacity = damageAlpha;
                if (damageAlpha > 0.01f && Camera.main != null)
                {
                    Vector3 forward = Camera.main.transform.forward;
                    forward.y = 0f;
                    float angle = Vector3.SignedAngle(forward.normalized, damageSourceDir, Vector3.up);
                    damageIndicator.style.rotate = new Rotate(angle);
                }
            }

            float gap = crosshairMinGap + spreadDegrees * crosshairPixelsPerDegree;
            if (chTop != null)
            {
                chTop.style.top = -(gap + 10f);
                chBottom.style.top = gap;
                chLeft.style.left = -(gap + 10f);
                chRight.style.left = gap;
            }
        }

        // ---------------------------------------------------------------- handlers

        private void OnHealth(HealthInfo info)
        {
            EnsureBound();
            healthNormalized = info.Normalized;
            if (healthLabel != null)
            {
                healthLabel.text = Mathf.CeilToInt(info.Current).ToString();
            }

            if (healthFill != null)
            {
                healthFill.style.width = Length.Percent(info.Normalized * 100f);
            }

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
            if (weaponName != null) weaponName.text = info.Name.ToUpperInvariant();
            if (ammoMag != null) ammoMag.text = info.Magazine.ToString();
            if (ammoReserve != null) ammoReserve.text = $"/ {info.Reserve}";
            if (reloading != null) reloading.text = info.Reloading ? "RELOADING" : string.Empty;
            if (weaponLevel != null) weaponLevel.text = $"LV {info.Level}";
            if (xpFill != null) xpFill.style.width = Length.Percent(info.LevelProgress * 100f);
            ammo?.EnableInClassList("ammo--empty", info.Magazine == 0);
            spreadDegrees = info.Spread;

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
            if (toast == null)
            {
                return;
            }

            toastText.text = $"{info.WeaponName.ToUpperInvariant()} LEVEL {info.NewLevel}";
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
            if (levelLabel != null) levelLabel.text = $"LEVEL {info.Level}";
            if (killsLabel != null) killsLabel.text = $"0 / {killsRequired}";

            if (info.Level > 1 && banner != null)
            {
                bannerTitle.text = $"LEVEL {info.Level}";
                bannerSub.text = "AMMO RESTOCKED";
                banner.AddToClassList("banner--visible");
                bannerHide?.Pause();
                bannerHide = banner.schedule.Execute(() => banner.RemoveFromClassList("banner--visible")).StartingIn(2200);
            }
        }

        private void OnKills(int kills)
        {
            EnsureBound();
            if (killsLabel != null) killsLabel.text = $"{kills} / {killsRequired}";
        }
    }
}
