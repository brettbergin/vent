using UnityEngine;
using UnityEngine.UIElements;
using Vent.Core.Events;
using Vent.Core.Updates;

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

        private VisualElement updatePanel;
        private Label updateStatus;
        private Button updateAction;
        private Button updateDismiss;

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

            Label version = r.Q<Label>("version-label");
            if (version != null)
            {
                version.text = $"v{Application.version}";
            }

            BindUpdates(r);
        }

        // The updater is a Vent.Core service the UI reads directly, the same way
        // SettingsPanelBinder reads SettingsStore. It only exists in a player, so every
        // reference here has to tolerate a null Instance in the editor and in tests.
        private void BindUpdates(VisualElement r)
        {
            updatePanel = r.Q<VisualElement>("update-panel");
            updateStatus = r.Q<Label>("update-status");
            updateAction = r.Q<Button>("update-action");
            updateDismiss = r.Q<Button>("update-dismiss");

            Click(updateAction, OnUpdateAction);
            Click(updateDismiss, () => UpdateService.Instance?.Skip());

            if (UpdateService.Instance != null)
            {
                UpdateService.Instance.Changed += RefreshUpdate;
            }

            RefreshUpdate();
        }

        protected override void Unbind()
        {
            if (UpdateService.Instance != null)
            {
                UpdateService.Instance.Changed -= RefreshUpdate;
            }

            base.Unbind();
        }

        private void OnUpdateAction()
        {
            UpdateService service = UpdateService.Instance;
            if (service == null)
            {
                return;
            }

            // When we cannot swap the install ourselves — read-only folder, translocated bundle,
            // a manifest schema this build predates — send them to the release page instead.
            if (service.Decision.CanInstall && string.IsNullOrEmpty(service.Blocker))
            {
                service.DownloadAndInstallAsync();
            }
            else
            {
                service.OpenReleasePage();
            }
        }

        private void RefreshUpdate()
        {
            if (updatePanel == null)
            {
                return;
            }

            UpdateService service = UpdateService.Instance;
            if (service == null || !service.HasUpdate)
            {
                SetHidden(updatePanel, true);
                return;
            }

            SetHidden(updatePanel, false);
            bool installable = service.Decision.CanInstall && string.IsNullOrEmpty(service.Blocker);

            switch (service.State)
            {
                case UpdateState.Downloading:
                    SetUpdateText($"DOWNLOADING {service.Progress * 100f:F0}%", muted: true);
                    SetUpdateButtons(actionText: null, dismissVisible: false);
                    break;

                case UpdateState.ReadyToInstall:
                case UpdateState.Installing:
                    SetUpdateText("RESTARTING TO INSTALL…", muted: true);
                    SetUpdateButtons(actionText: null, dismissVisible: false);
                    break;

                case UpdateState.Failed:
                    SetUpdateText(service.Blocker, muted: true);
                    SetUpdateButtons("GET IT", dismissVisible: true);
                    break;

                default:
                    string line = $"VERSION {service.Decision.Version} IS AVAILABLE";
                    SetUpdateText(installable ? line : $"{line} — {service.Blocker}", muted: !installable);
                    SetUpdateButtons(installable ? "UPDATE AND RESTART" : "GET IT", dismissVisible: true);
                    break;
            }
        }

        private void SetUpdateText(string text, bool muted)
        {
            if (updateStatus == null)
            {
                return;
            }

            updateStatus.text = string.IsNullOrEmpty(text) ? string.Empty : text;
            updateStatus.EnableInClassList("update__status--muted", muted);
        }

        private void SetUpdateButtons(string actionText, bool dismissVisible)
        {
            if (updateAction != null)
            {
                SetHidden(updateAction, actionText == null);
                if (actionText != null)
                {
                    updateAction.text = actionText;
                }
            }

            SetHidden(updateDismiss, !dismissVisible);
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
