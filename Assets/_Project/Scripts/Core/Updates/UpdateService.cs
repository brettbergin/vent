using System;
using System.IO;
using System.Threading;
using UnityEngine;
using Vent.Core.Settings;

namespace Vent.Core.Updates
{
    public enum UpdateState
    {
        Idle = 0,
        Checking,
        UpToDate,
        Available,
        Downloading,
        ReadyToInstall,
        Installing,
        Failed,
    }

    /// <summary>
    /// Checks GitHub for a newer release, downloads it and swaps it in.
    ///
    /// Installs itself the way <see cref="Core.Diagnostics.FrameRateLog"/> does — a hidden
    /// DontDestroyOnLoad object created after the first scene loads — so it needs no wiring in the
    /// generated Boot scene and no regen. The UI reads it directly, following the
    /// <see cref="SettingsStore"/> precedent rather than adding an event channel.
    /// </summary>
    public sealed class UpdateService : MonoBehaviour
    {
        private const string StageFolder = "updates";

        public static UpdateService Instance { get; private set; }

        /// <summary>Raised on the main thread whenever <see cref="State"/> or <see cref="Progress"/> moves.</summary>
        public event Action Changed;

        public UpdateState State { get; private set; } = UpdateState.Idle;
        public UpdateDecision Decision { get; private set; } = UpdateDecision.None;
        public float Progress { get; private set; }

        /// <summary>Why we will not install for them, when we cannot. Empty when we can.</summary>
        public string Blocker { get; private set; } = string.Empty;

        public string CurrentVersion => Application.version;
        public bool HasUpdate => Decision.IsNewer;
        public string ReleaseUrl => string.IsNullOrEmpty(Decision.ReleaseUrl) ? UpdateUrl.ReleasesUrl : Decision.ReleaseUrl;

        private CancellationTokenSource cts;
        private string zipPath;
        private bool busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Nothing to update in the editor, and no player process to replace.
            if (Application.isEditor || Instance != null)
            {
                return;
            }

            var go = new GameObject("UpdateService") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<UpdateService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            if (SettingsStore.AutoCheckUpdates)
            {
                CheckAsync();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            cts?.Cancel();
            cts?.Dispose();
            cts = null;
        }

        private string WorkingDir => Path.Combine(Application.persistentDataPath, StageFolder);

        private static UpdatePlatform CurrentPlatform => Application.platform switch
        {
            RuntimePlatform.OSXPlayer => UpdatePlatform.MacOS,
            RuntimePlatform.WindowsPlayer => UpdatePlatform.Windows,
            _ => UpdatePlatform.Unsupported,
        };

        private void Set(UpdateState state)
        {
            State = state;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ check

        public async void CheckAsync()
        {
            if (busy)
            {
                return;
            }

            busy = true;
            try
            {
                cts?.Dispose();
                cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

                Set(UpdateState.Checking);
                UpdateManifest manifest = await UpdateCheck.FetchAsync(UpdateUrl.ManifestUrl, cts.Token);

                Decision = UpdateDecision.Evaluate(
                    manifest, Application.version, CurrentPlatform, SettingsStore.SkippedUpdateVersion);

                SettingsStore.LastUpdateCheckUtc = DateTime.UtcNow;

                if (!Decision.IsNewer)
                {
                    Debug.Log($"[Updater] {Application.version} is current");
                    Set(UpdateState.UpToDate);
                    return;
                }

                // Work out now, not after a 78 MB download, whether we could install it.
                Blocker = DescribeBlocker();
                Debug.Log($"[Updater] {Decision.Version} available (installable: {string.IsNullOrEmpty(Blocker)})");
                Set(UpdateState.Available);
            }
            catch (OperationCanceledException)
            {
                // Quitting; nothing to report.
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Updater] check failed: {e.Message}");
                Set(UpdateState.Failed);
            }
            finally
            {
                busy = false;
            }
        }

        private string DescribeBlocker()
        {
            if (!Decision.CanInstall)
            {
                return "Download this update from the releases page.";
            }

            InstallLocation location = ResolveLocation();
            if (!location.CanUpdate)
            {
                return location.BlockerMessage;
            }

            return UpdateInstaller.IsWritable(location)
                ? string.Empty
                : InstallLocation.MessageFor(InstallBlocker.NotWritable);
        }

        private InstallLocation ResolveLocation()
            => InstallLocation.Resolve(Application.dataPath, CurrentPlatform, Application.isEditor);

        // ------------------------------------------------------------------ download and install

        public async void DownloadAndInstallAsync()
        {
            if (busy || !Decision.CanInstall || !string.IsNullOrEmpty(Blocker))
            {
                return;
            }

            busy = true;
            try
            {
                cts?.Dispose();
                cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

                Directory.CreateDirectory(WorkingDir);
                zipPath = Path.Combine(WorkingDir, $"Vent-{Decision.Version}.zip");

                Progress = 0f;
                Set(UpdateState.Downloading);

                bool ok = await UpdateDownloader.DownloadAsync(
                    Decision.Asset.url,
                    zipPath,
                    Decision.Asset.sha256,
                    p => { Progress = p; Changed?.Invoke(); },
                    cts.Token);

                if (!ok)
                {
                    Blocker = "The download failed or did not match its checksum.";
                    Set(UpdateState.Failed);
                    return;
                }

                Set(UpdateState.ReadyToInstall);
                StartSwap();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Updater] install failed: {e.Message}");
                Set(UpdateState.Failed);
            }
            finally
            {
                busy = false;
            }
        }

        private void StartSwap()
        {
            InstallLocation location = ResolveLocation();
            Set(UpdateState.Installing);

            if (UpdateInstaller.LaunchAndQuit(location, zipPath, Decision.Asset.rootName, WorkingDir))
            {
                Application.Quit();
                return;
            }

            Blocker = "Vent could not replace itself. Download the update from the releases page.";
            Set(UpdateState.Failed);
        }

        // ------------------------------------------------------------------ dismissal

        /// <summary>Stop offering this exact version until a newer one appears.</summary>
        public void Skip()
        {
            if (!Decision.IsNewer)
            {
                return;
            }

            SettingsStore.SkippedUpdateVersion = Decision.Version.ToString();
            Decision = UpdateDecision.None;
            Set(UpdateState.UpToDate);
        }

        public void OpenReleasePage() => Application.OpenURL(ReleaseUrl);
    }
}
