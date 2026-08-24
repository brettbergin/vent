using UnityEngine.UIElements;
using Vent.Core.Settings;

namespace Vent.UI.Screens
{
    /// <summary>Two-way binds the shared SettingsPanel template to <see cref="SettingsStore"/>.</summary>
    public static class SettingsPanelBinder
    {
        public static void Bind(VisualElement panel)
        {
            if (panel == null)
            {
                return;
            }

            var sensitivity = panel.Q<Slider>("sensitivity");
            var invertY = panel.Q<Toggle>("invert-y");
            var volume = panel.Q<Slider>("volume");

            if (sensitivity != null)
            {
                sensitivity.SetValueWithoutNotify(SettingsStore.Sensitivity);
                sensitivity.RegisterValueChangedCallback(e => SettingsStore.Sensitivity = e.newValue);
            }

            if (invertY != null)
            {
                invertY.SetValueWithoutNotify(SettingsStore.InvertY);
                invertY.RegisterValueChangedCallback(e => SettingsStore.InvertY = e.newValue);
            }

            if (volume != null)
            {
                volume.SetValueWithoutNotify(SettingsStore.Volume);
                volume.RegisterValueChangedCallback(e => SettingsStore.Volume = e.newValue);
            }
        }

        /// <summary>Refresh displayed values (settings may change from another screen).</summary>
        public static void Refresh(VisualElement panel)
        {
            panel?.Q<Slider>("sensitivity")?.SetValueWithoutNotify(SettingsStore.Sensitivity);
            panel?.Q<Toggle>("invert-y")?.SetValueWithoutNotify(SettingsStore.InvertY);
            panel?.Q<Slider>("volume")?.SetValueWithoutNotify(SettingsStore.Volume);
        }
    }
}
