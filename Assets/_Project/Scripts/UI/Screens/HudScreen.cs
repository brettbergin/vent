using UnityEngine;
using UnityEngine.UIElements;
using Vent.Core;
using System.Collections.Generic;
using System.Text;
using Vent.Core.Events;
using Vent.Core.Perks;
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
        [SerializeField] private PerkEventChannel perkCollected;
        [SerializeField, Tooltip("Interaction prompt text; empty hides it.")]
        private StringEventChannel prompt;
        [SerializeField, Tooltip("Banner text: \"TITLE\\nSUBTITLE\".")]
        private StringEventChannel announcement;
        [SerializeField, Tooltip("km/h while driving; negative hides the speedometer.")]
        private FloatEventChannel vehicleSpeed;

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float crosshairPixelsPerDegree = 22f;
        [SerializeField, Min(0f)] private float crosshairMinGap = 5f;

        private readonly HudViewModel model = new();

        private VisualElement ammo, hitmarker, banner, toast, promptElement, speedo;
        private VisualElement[] slots;

        private float vignetteFlash;
        private float healthNormalized = 1f;
        private float hitmarkerAlpha;
        private float damageAlpha;
        private Vector3 damageSourceDir;
        private int killsRequired = 1;
        private float spreadDegrees;
        private IVisualElementScheduledItem bannerHide, toastHide;
        private readonly Dictionary<PerkKind, float> perkEndTimes = new();
        private readonly StringBuilder perkBuilder = new();
        private string lastPerkText = string.Empty;

        /// <summary>The bound data source; exposed for tests.</summary>
        public HudViewModel Model => model;

        public void Configure(HealthEventChannel health, WeaponHudEventChannel weapon, WeaponLevelUpEventChannel weaponLevel,
            BoolEventChannel hit, LevelEventChannel level, IntEventChannel kills, PerkEventChannel perks,
            StringEventChannel promptChannel = null, StringEventChannel announcementChannel = null, FloatEventChannel speedChannel = null)
        {
            prompt = promptChannel;
            announcement = announcementChannel;
            vehicleSpeed = speedChannel;
            perkCollected = perks;
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
            perkCollected?.Subscribe(OnPerk);
            prompt?.Subscribe(OnPrompt);
            announcement?.Subscribe(OnAnnouncement);
            vehicleSpeed?.Subscribe(OnVehicleSpeed);
        }

        protected override void OnDisable()
        {
            healthChanged?.Unsubscribe(OnHealth);
            weaponChanged?.Unsubscribe(OnWeapon);
            weaponLeveled?.Unsubscribe(OnWeaponLeveled);
            hitConfirmed?.Unsubscribe(OnHit);
            levelChanged?.Unsubscribe(OnLevel);
            perkCollected?.Unsubscribe(OnPerk);
            killsThisLevelChanged?.Unsubscribe(OnKills);
            prompt?.Unsubscribe(OnPrompt);
            announcement?.Unsubscribe(OnAnnouncement);
            vehicleSpeed?.Unsubscribe(OnVehicleSpeed);
            base.OnDisable();
        }

        protected override void Bind(VisualElement r)
        {
            r.dataSource = model;

            ammo = r.Q<VisualElement>("ammo");
            hitmarker = r.Q<VisualElement>("hitmarker");
            banner = r.Q<VisualElement>("banner");
            toast = r.Q<VisualElement>("toast");
            promptElement = r.Q<VisualElement>("prompt");
            speedo = r.Q<VisualElement>("speedo");
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

            UpdatePerkText();

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
            ShowToast($"{info.WeaponName.ToUpperInvariant()} LEVEL {info.NewLevel}");
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

            if (info.Level > 1)
            {
                ShowBanner($"LEVEL {info.Level}", "AMMO RESTOCKED");
            }
        }

        private void OnPrompt(string text)
        {
            EnsureBound();
            model.PromptText = text ?? string.Empty;
            promptElement?.EnableInClassList("prompt--visible", !string.IsNullOrEmpty(text));
        }

        /// <summary>"TITLE\nSUBTITLE" from anywhere in the world (the front door unlocking).</summary>
        private void OnAnnouncement(string text)
        {
            EnsureBound();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int split = text.IndexOf('\n');
            string title = split < 0 ? text : text.Substring(0, split);
            string sub = split < 0 ? string.Empty : text.Substring(split + 1);
            ShowBanner(title, sub);
        }

        private void OnVehicleSpeed(float kmh)
        {
            EnsureBound();
            bool driving = kmh >= 0f;
            model.SpeedText = driving ? $"{Mathf.RoundToInt(kmh)} km/h" : string.Empty;
            speedo?.EnableInClassList("speedo--visible", driving);
        }

        private void ShowBanner(string title, string sub)
        {
            model.BannerTitle = title;
            model.BannerSub = sub;
            if (banner == null)
            {
                return;
            }

            banner.AddToClassList("banner--visible");
            bannerHide?.Pause();
            bannerHide = banner.schedule.Execute(() => banner.RemoveFromClassList("banner--visible")).StartingIn(2200);
        }

        private void OnKills(int kills)
        {
            EnsureBound();
            model.KillsText = $"{kills} / {killsRequired}";
        }

        private void OnPerk(PerkInfo perk)
        {
            EnsureBound();
            if (perk.IsTimed)
            {
                float end = Time.time + perk.Duration;
                perkEndTimes[perk.Kind] = perkEndTimes.TryGetValue(perk.Kind, out float existing) ? Mathf.Max(existing, end) : end;
            }

            ShowToast(PerkStyle.DisplayName(perk.Kind));
        }

        /// <summary>"INVULNERABLE 7.2s   ONE SHOT 3.9s" while timed perks run; cleared when they lapse.</summary>
        private void UpdatePerkText()
        {
            perkBuilder.Clear();
            float now = Time.time;
            foreach (KeyValuePair<PerkKind, float> entry in perkEndTimes)
            {
                float left = entry.Value - now;
                if (left <= 0f)
                {
                    continue;
                }

                if (perkBuilder.Length > 0)
                {
                    perkBuilder.Append("   ");
                }

                perkBuilder.Append(PerkStyle.DisplayName(entry.Key)).Append(' ').Append(left.ToString("0.0")).Append('s');
            }

            string text = perkBuilder.ToString();
            if (text != lastPerkText)
            {
                lastPerkText = text;
                model.PerkText = text;
            }
        }

        private void ShowToast(string text)
        {
            model.ToastText = text;
            if (toast == null)
            {
                return;
            }

            toast.AddToClassList("toast--visible");
            toastHide?.Pause();
            toastHide = toast.schedule.Execute(() => toast.RemoveFromClassList("toast--visible")).StartingIn(1800);
        }
    }
}
