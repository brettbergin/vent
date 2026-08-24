using UnityEngine;
using UnityEngine.UIElements;
using Vent.Core.Events;

namespace Vent.UI.Screens
{
    public sealed class PauseScreen : UIScreen
    {
        [SerializeField] private VoidEventChannel resumeRequested;
        [SerializeField] private VoidEventChannel restartRequested;
        [SerializeField] private VoidEventChannel menuRequested;

        private VisualElement settingsPanel;
        private VisualElement root;

        public void Configure(VoidEventChannel resume, VoidEventChannel restart, VoidEventChannel menu)
        {
            resumeRequested = resume;
            restartRequested = restart;
            menuRequested = menu;
        }

        protected override void Bind(VisualElement r)
        {
            root = r;
            settingsPanel = r.Q<VisualElement>("settings-panel");
            SettingsPanelBinder.Bind(settingsPanel);

            Click(r.Q<Button>("resume"), () => resumeRequested?.Raise());
            Click(r.Q<Button>("restart"), () => restartRequested?.Raise());
            Click(r.Q<Button>("settings-toggle"), () => SetHidden(settingsPanel, !settingsPanel.ClassListContains("hidden")));
            Click(r.Q<Button>("menu"), () => menuRequested?.Raise());
        }

        protected override void OnShown()
        {
            SetHidden(settingsPanel, true);
            SettingsPanelBinder.Refresh(settingsPanel);
            FocusFirst(root);
        }
    }
}
