using System;

namespace Vent.Core.Updates
{
    /// <summary>Why an installed copy cannot replace itself.</summary>
    public enum InstallBlocker
    {
        None = 0,

        /// <summary>Running in the editor; there is no player to swap.</summary>
        Editor,

        /// <summary>Not a platform the updater can install on.</summary>
        UnsupportedPlatform,

        /// <summary>The path does not look like a Vent install; refuse to touch it.</summary>
        Unrecognised,

        /// <summary>
        /// macOS ran the app from a randomised read-only mount (Gatekeeper Path Randomisation).
        /// Happens when a quarantined app is launched straight out of Downloads. Moving the app
        /// in Finder is what clears it.
        /// </summary>
        Translocated,

        /// <summary>The install directory is not writable — Program Files without elevation, say.</summary>
        NotWritable,
    }

    /// <summary>
    /// Works out what an update would actually overwrite, from <c>Application.dataPath</c>.
    ///
    /// Deliberately pure — it takes the data path as a parameter rather than reading
    /// <c>Application.dataPath</c> itself — because a mistake here hands a wrong directory to
    /// <c>rm -rf</c> or <c>robocopy /MIR</c>. Writability is checked separately by the caller,
    /// since that needs the file system.
    /// </summary>
    public readonly struct InstallLocation
    {
        /// <summary>What gets replaced: the .app bundle on macOS, the install folder on Windows.</summary>
        public readonly string Root;

        /// <summary>What to launch afterwards.</summary>
        public readonly string LaunchTarget;

        public readonly UpdatePlatform Platform;
        public readonly InstallBlocker Blocker;

        private InstallLocation(string root, string launchTarget, UpdatePlatform platform, InstallBlocker blocker)
        {
            Root = root;
            LaunchTarget = launchTarget;
            Platform = platform;
            Blocker = blocker;
        }

        public bool CanUpdate => Blocker == InstallBlocker.None;

        private static InstallLocation Blocked(InstallBlocker blocker, UpdatePlatform platform = UpdatePlatform.Unsupported)
            => new InstallLocation(null, null, platform, blocker);

        /// <param name="dataPath">
        /// Application.dataPath. macOS: "…/Vent.app/Contents/Resources/Data".
        /// Windows: "…/Vent-0.1.0-Windows-x64/Vent_Data".
        /// </param>
        public static InstallLocation Resolve(string dataPath, UpdatePlatform platform, bool isEditor)
        {
            if (isEditor)
            {
                return Blocked(InstallBlocker.Editor, platform);
            }

            if (string.IsNullOrWhiteSpace(dataPath))
            {
                return Blocked(InstallBlocker.Unrecognised, platform);
            }

            string path = dataPath.Replace('\\', '/').TrimEnd('/');

            return platform switch
            {
                UpdatePlatform.MacOS => ResolveMacOS(path),
                UpdatePlatform.Windows => ResolveWindows(path),
                _ => Blocked(InstallBlocker.UnsupportedPlatform, platform),
            };
        }

        private static InstallLocation ResolveMacOS(string path)
        {
            // Gatekeeper Path Randomisation mounts the bundle read-only under a random
            // /private/var/folders/…/AppTranslocation/<uuid>/d/ path. Swapping there would write
            // to the wrong place, on a read-only volume, and leave the real copy stale.
            if (path.IndexOf("/AppTranslocation/", StringComparison.Ordinal) >= 0)
            {
                return Blocked(InstallBlocker.Translocated, UpdatePlatform.MacOS);
            }

            // Application.dataPath on a macOS player is the bundle's Contents folder — NOT
            // Contents/Resources/Data, which is where the data actually lives. Both are accepted
            // so a change in either direction cannot silently stop every update again.
            string bundle = null;
            foreach (string suffix in new[] { "/Contents/Resources/Data", "/Contents" })
            {
                if (path.EndsWith(suffix, StringComparison.Ordinal))
                {
                    bundle = path.Substring(0, path.Length - suffix.Length);
                    break;
                }
            }

            if (string.IsNullOrEmpty(bundle) || bundle == "/" || !bundle.EndsWith(".app", StringComparison.Ordinal))
            {
                return Blocked(InstallBlocker.Unrecognised, UpdatePlatform.MacOS);
            }

            return new InstallLocation(bundle, bundle, UpdatePlatform.MacOS, InstallBlocker.None);
        }

        private static InstallLocation ResolveWindows(string path)
        {
            // …/<install dir>/Vent_Data → the install dir is what robocopy /MIR mirrors over.
            const string suffix = "/Vent_Data";
            if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return Blocked(InstallBlocker.Unrecognised, UpdatePlatform.Windows);
            }

            string dir = path.Substring(0, path.Length - suffix.Length);

            // A drive root would mean mirroring over the whole disk. Never.
            if (dir.Length == 0 || dir.EndsWith(":", StringComparison.Ordinal))
            {
                return Blocked(InstallBlocker.Unrecognised, UpdatePlatform.Windows);
            }

            return new InstallLocation(dir, dir + "/Vent.exe", UpdatePlatform.Windows, InstallBlocker.None);
        }

        /// <summary>What to tell the player when we will not install for them.</summary>
        public string BlockerMessage => MessageFor(Blocker);

        public static string MessageFor(InstallBlocker blocker) => blocker switch
        {
            InstallBlocker.None => string.Empty,
            InstallBlocker.Editor => "Updates do not install from the editor.",
            InstallBlocker.UnsupportedPlatform => "Automatic updates are not available on this platform.",
            InstallBlocker.Translocated => "Move Vent to your Applications folder, then check again.",
            InstallBlocker.NotWritable => "Vent cannot write to its own folder. Move it somewhere writable, or download manually.",
            _ => "Vent could not work out where it is installed. Download the update manually.",
        };
    }
}
