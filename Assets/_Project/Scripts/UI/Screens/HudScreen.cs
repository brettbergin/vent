using UnityEngine;
using UnityEngine.UIElements;
using Vent.Core;
using System.Collections.Generic;
using System.Text;
using Vent.Core.Events;
using Vent.Core.Items;
using Vent.Core.Services;
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
        [SerializeField, Tooltip("The key hunt's current step; empty hides the line.")]
        private StringEventChannel objective;
        [SerializeField, Tooltip("A map or a mirror was picked up; the map arrives with its image.")]
        private OfficeItemEventChannel itemCollected;
        [SerializeField, Tooltip("The map key. Only does anything once a map has been found.")]
        private VoidEventChannel mapToggled;

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float crosshairPixelsPerDegree = 22f;
        [SerializeField, Min(0f)] private float crosshairMinGap = 5f;

        private readonly HudViewModel model = new();

        private VisualElement ammo, hitmarker, banner, toast, promptElement, objectiveElement, speedo;
        private VisualElement mapElement, mapImage, mapMarker, mirrorElement, mirrorView;
        private Rect mapWorld;
        private RenderTexture mirrorTexture;
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

        /// <summary>The player is carrying the building map.</summary>
        public bool HasMap { get; private set; }

        /// <summary>The map overlay is up.</summary>
        public bool IsMapVisible { get; private set; }

        /// <summary>The player is carrying the rear-view mirror.</summary>
        public bool HasMirror { get; private set; }

        public void Configure(HealthEventChannel health, WeaponHudEventChannel weapon, WeaponLevelUpEventChannel weaponLevel,
            BoolEventChannel hit, LevelEventChannel level, IntEventChannel kills, PerkEventChannel perks,
            StringEventChannel promptChannel = null, StringEventChannel announcementChannel = null, FloatEventChannel speedChannel = null,
            StringEventChannel objectiveChannel = null, OfficeItemEventChannel itemChannel = null, VoidEventChannel mapToggleChannel = null)
        {
            itemCollected = itemChannel;
            mapToggled = mapToggleChannel;
            objective = objectiveChannel;
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
            objective?.Subscribe(OnObjective);
            itemCollected?.Subscribe(OnItem);
            mapToggled?.Subscribe(OnMapToggled);
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
            objective?.Unsubscribe(OnObjective);
            itemCollected?.Unsubscribe(OnItem);
            mapToggled?.Unsubscribe(OnMapToggled);
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
            objectiveElement = r.Q<VisualElement>("objective");
            speedo = r.Q<VisualElement>("speedo");
            mapElement = r.Q<VisualElement>("map");
            mapImage = r.Q<VisualElement>("map-image");
            mapMarker = r.Q<VisualElement>("map-marker");
            mirrorElement = r.Q<VisualElement>("mirror");
            mirrorView = r.Q<VisualElement>("mirror-view");
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
            UpdateItems();
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
            if (info.Level <= 1)
            {
                DropItems(); // a new run starts at level 1 with empty pockets
            }

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

        /// <summary>
        /// The key hunt's standing objective. Unlike the prompt it is not about what the player is
        /// looking at, and unlike the banner it does not time out: it sits there until the step changes.
        /// </summary>
        private void OnObjective(string text)
        {
            EnsureBound();
            model.ObjectiveText = text ?? string.Empty;
            objectiveElement?.EnableInClassList("objective--visible", !string.IsNullOrEmpty(text));
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

        // ------------------------------------------------------------------ office items

        private void OnItem(OfficeItemInfo info)
        {
            switch (info.Kind)
            {
                case OfficeItem.BuildingMap:
                    HasMap = info.Map != null;
                    mapWorld = info.WorldRect;
                    if (mapImage != null && info.Map != null)
                    {
                        mapImage.style.backgroundImage = new StyleBackground(info.Map);
                    }

                    break;
                case OfficeItem.RearViewMirror:
                    HasMirror = true;
                    break;
            }
        }

        private void OnMapToggled()
        {
            if (!HasMap)
            {
                return;
            }

            IsMapVisible = !IsMapVisible;
            mapElement?.EnableInClassList("map--visible", IsMapVisible);
        }

        private void DropItems()
        {
            HasMap = false;
            HasMirror = false;
            IsMapVisible = false;
            mapElement?.EnableInClassList("map--visible", false);
            mirrorElement?.EnableInClassList("mirror--visible", false);
            Root?.EnableInClassList("hud--mirror", false);
            mirrorTexture = null;
        }

        /// <summary>The player's dot on the map and the live rear view: polled, since both change every frame.</summary>
        private void UpdateItems()
        {
            if (IsMapVisible && mapMarker != null && mapImage != null && GameServices.TryGet(out IPlayerTarget target) && mapWorld.width > 0f && mapWorld.height > 0f)
            {
                Vector3 p = target.Position;
                float u = (p.x - mapWorld.xMin) / mapWorld.width;
                float v = (p.z - mapWorld.yMin) / mapWorld.height;
                float w = mapImage.resolvedStyle.width, h = mapImage.resolvedStyle.height;
                float size = mapMarker.resolvedStyle.width;
                mapMarker.style.left = Mathf.Clamp01(u) * w - size / 2f;
                mapMarker.style.top = (1f - Mathf.Clamp01(v)) * h - size / 2f;
                Vector3 forward = target.Transform != null ? target.Transform.forward : Vector3.forward;
                mapMarker.style.rotate = new Rotate(Angle.Degrees(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg));
            }

            IRearViewSource rear = null;
            bool mirrorOn = HasMirror && GameServices.TryGet(out rear) && rear.IsActive && rear.View != null;
            if (mirrorOn && mirrorView != null && rear.View != mirrorTexture)
            {
                mirrorTexture = rear.View;
                mirrorView.style.backgroundImage = Background.FromRenderTexture(mirrorTexture);
            }

            mirrorElement?.EnableInClassList("mirror--visible", mirrorOn);
            Root?.EnableInClassList("hud--mirror", mirrorOn);
        }
    }
}
