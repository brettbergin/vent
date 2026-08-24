using UnityEngine;
using UnityEngine.UIElements;
using Vent.Core.Events;

namespace Vent.UI.Screens
{
    public sealed class MainMenuScreen : UIScreen
    {
        [Header("Requests out")]
        [SerializeField] private VoidEventChannel playRequested;
        [SerializeField] private VoidEventChannel quitRequested;

        [Header("Data in")]
        [SerializeField] private IntEventChannel bestLevelChanged;

        private Label bestLevel;
        private VisualElement settingsPanel;
        private VisualElement root;

        public void Configure(VoidEventChannel play, VoidEventChannel quit, IntEventChannel bestLevel)
        {
            playRequested = play;
            quitRequested = quit;
            bestLevelChanged = bestLevel;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            bestLevelChanged?.Subscribe(OnBestLevel);
        }

        protected override void OnDisable()
        {
            bestLevelChanged?.Unsubscribe(OnBestLevel);
            base.OnDisable();
        }

        protected override void Bind(VisualElement r)
        {
            root = r;
            bestLevel = r.Q<Label>("best-level");
            settingsPanel = r.Q<VisualElement>("settings-panel");
            SettingsPanelBinder.Bind(settingsPanel);

            Click(r.Q<Button>("play"), () => playRequested?.Raise());
            Click(r.Q<Button>("settings-toggle"), ToggleSettings);
            Click(r.Q<Button>("quit"), () => quitRequested?.Raise());
        }

        protected override void OnShown()
        {
            SetHidden(settingsPanel, true);
            SettingsPanelBinder.Refresh(settingsPanel);
            FocusFirst(root);
        }

        private void ToggleSettings()
        {
            if (settingsPanel == null)
            {
                return;
            }

            bool hidden = settingsPanel.ClassListContains("hidden");
            SetHidden(settingsPanel, !hidden);
        }

        private void OnBestLevel(int level)
        {
            if (bestLevel != null)
            {
                bestLevel.text = level > 0 ? $"BEST LEVEL: {level}" : "BEST LEVEL: —";
            }
        }
    }
}
