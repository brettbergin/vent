using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine.UIElements;

namespace Vent.UI.Screens
{
    /// <summary>
    /// Data source for <c>Hud.uxml</c>. Every value the HUD shows is a bindable property here;
    /// the document binds to them declaratively (<c>&lt;Bindings&gt;</c> in the UXML) through
    /// UI Toolkit runtime data binding, so <see cref="HudScreen"/> never touches labels or styles
    /// for plain values — it only writes this object.
    ///
    /// Style-typed properties (<see cref="StyleLength"/>, <see cref="StyleFloat"/>, ...) bind to
    /// <c>style.*</c> without converters. Plain C# class on purpose: no engine lifetime, trivially
    /// testable.
    /// </summary>
    public sealed class HudViewModel : INotifyBindablePropertyChanged
    {
        public event System.EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        private string healthText = "100";
        private StyleLength healthFillWidth = Length.Percent(100f);
        private string weaponName = "SMG";
        private string ammoMagText = "30";
        private string ammoReserveText = "/ 120";
        private string reloadingText = string.Empty;
        private string weaponLevelText = "LV 1";
        private StyleLength xpFillWidth = Length.Percent(0f);
        private string levelText = "LEVEL 1";
        private string killsText = "0 / 1";
        private string bannerTitle = string.Empty;
        private string bannerSub = string.Empty;
        private string toastText = string.Empty;
        private string perkText = string.Empty;
        private string promptText = string.Empty;
        private string objectiveText = string.Empty;
        private string speedText = string.Empty;
        private StyleFloat vignetteOpacity = 0f;
        private StyleFloat hitmarkerOpacity = 0f;
        private StyleFloat damageIndicatorOpacity = 0f;
        private StyleRotate damageIndicatorRotation = new Rotate(0f);
        private StyleLength crosshairNear = 0f;
        private StyleLength crosshairFar = 0f;

        [CreateProperty] public string HealthText { get => healthText; set => Set(ref healthText, value); }
        [CreateProperty] public StyleLength HealthFillWidth { get => healthFillWidth; set => Set(ref healthFillWidth, value); }
        [CreateProperty] public string WeaponName { get => weaponName; set => Set(ref weaponName, value); }
        [CreateProperty] public string AmmoMagText { get => ammoMagText; set => Set(ref ammoMagText, value); }
        [CreateProperty] public string AmmoReserveText { get => ammoReserveText; set => Set(ref ammoReserveText, value); }
        [CreateProperty] public string ReloadingText { get => reloadingText; set => Set(ref reloadingText, value); }
        [CreateProperty] public string WeaponLevelText { get => weaponLevelText; set => Set(ref weaponLevelText, value); }
        [CreateProperty] public StyleLength XpFillWidth { get => xpFillWidth; set => Set(ref xpFillWidth, value); }
        [CreateProperty] public string LevelText { get => levelText; set => Set(ref levelText, value); }
        [CreateProperty] public string KillsText { get => killsText; set => Set(ref killsText, value); }
        [CreateProperty] public string BannerTitle { get => bannerTitle; set => Set(ref bannerTitle, value); }
        [CreateProperty] public string BannerSub { get => bannerSub; set => Set(ref bannerSub, value); }
        [CreateProperty] public string ToastText { get => toastText; set => Set(ref toastText, value); }

        /// <summary>Active timed perks with their countdowns; empty when none.</summary>
        [CreateProperty] public string PerkText { get => perkText; set => Set(ref perkText, value); }

        /// <summary>"[E]  OPEN DOOR" while looking at something usable; empty otherwise.</summary>
        [CreateProperty] public string PromptText { get => promptText; set => Set(ref promptText, value); }

        /// <summary>The key hunt's current step, standing on screen until it changes; empty when idle.</summary>
        [CreateProperty] public string ObjectiveText { get => objectiveText; set => Set(ref objectiveText, value); }

        /// <summary>Speedometer while driving; empty on foot.</summary>
        [CreateProperty] public string SpeedText { get => speedText; set => Set(ref speedText, value); }
        [CreateProperty] public StyleFloat VignetteOpacity { get => vignetteOpacity; set => Set(ref vignetteOpacity, value); }
        [CreateProperty] public StyleFloat HitmarkerOpacity { get => hitmarkerOpacity; set => Set(ref hitmarkerOpacity, value); }
        [CreateProperty] public StyleFloat DamageIndicatorOpacity { get => damageIndicatorOpacity; set => Set(ref damageIndicatorOpacity, value); }
        [CreateProperty] public StyleRotate DamageIndicatorRotation { get => damageIndicatorRotation; set => Set(ref damageIndicatorRotation, value); }

        /// <summary>Offset of the top/left crosshair bars (negative: gap plus bar length).</summary>
        [CreateProperty] public StyleLength CrosshairNear { get => crosshairNear; set => Set(ref crosshairNear, value); }

        /// <summary>Offset of the bottom/right crosshair bars (the gap).</summary>
        [CreateProperty] public StyleLength CrosshairFar { get => crosshairFar; set => Set(ref crosshairFar, value); }

        private void Set<T>(ref T field, T value, [CallerMemberName] string property = "")
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
        }
    }
}
