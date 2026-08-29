using System;

namespace Vent.Core.Updates
{
    public enum UpdateVerdict
    {
        /// <summary>Nothing newer, or the manifest was unusable.</summary>
        None = 0,

        /// <summary>A newer release exists and can be installed.</summary>
        Available = 1,

        /// <summary>Newer, but the player asked to skip this exact version.</summary>
        Skipped = 2,

        /// <summary>Newer, but written to a schema this build does not understand: link, do not install.</summary>
        SchemaTooNew = 3,
    }

    /// <summary>
    /// Whether a fetched manifest should turn into an offer. Pure and unit tested: this is the
    /// logic that decides to overwrite the player's game, so it is kept away from the engine.
    /// </summary>
    public readonly struct UpdateDecision
    {
        public readonly UpdateVerdict Verdict;
        public readonly SemVer Version;
        public readonly PlatformAsset Asset;
        public readonly string ReleaseUrl;
        public readonly string Notes;

        private UpdateDecision(UpdateVerdict verdict, SemVer version, PlatformAsset asset, string releaseUrl, string notes)
        {
            Verdict = verdict;
            Version = version;
            Asset = asset;
            ReleaseUrl = releaseUrl;
            Notes = notes;
        }

        public bool CanInstall => Verdict == UpdateVerdict.Available;

        /// <summary>Worth telling the player about, even if we cannot install it ourselves.</summary>
        public bool IsNewer => Verdict == UpdateVerdict.Available || Verdict == UpdateVerdict.SchemaTooNew;

        private static readonly UpdateDecision NoneResult =
            new UpdateDecision(UpdateVerdict.None, default, null, null, null);

        public static UpdateDecision None => NoneResult;

        /// <param name="currentVersion">Application.version of the running build.</param>
        /// <param name="skippedVersion">What the player last dismissed; empty for nothing.</param>
        public static UpdateDecision Evaluate(
            UpdateManifest manifest,
            string currentVersion,
            UpdatePlatform platform,
            string skippedVersion = null)
        {
            if (manifest == null || platform == UpdatePlatform.Unsupported)
            {
                return None;
            }

            if (!SemVer.TryParse(manifest.version, out SemVer offered))
            {
                return None;
            }

            // An unparseable local version would otherwise compare as 0.0.0 and make every
            // launch offer an update. Refuse instead.
            if (!SemVer.TryParse(currentVersion, out SemVer current))
            {
                return None;
            }

            // Only ever forwards. A rolled-back "latest" release must not downgrade anyone.
            if (offered <= current)
            {
                return None;
            }

            if (manifest.schema > UpdateManifest.SupportedSchema)
            {
                return new UpdateDecision(UpdateVerdict.SchemaTooNew, offered, null, manifest.releaseUrl, manifest.notes);
            }

            if (!string.IsNullOrEmpty(skippedVersion)
                && string.Equals(skippedVersion.Trim(), offered.ToString(), StringComparison.Ordinal))
            {
                return new UpdateDecision(UpdateVerdict.Skipped, offered, null, manifest.releaseUrl, manifest.notes);
            }

            PlatformAsset asset = manifest.AssetFor(platform);
            if (asset == null || !asset.IsComplete)
            {
                // A release that skipped this platform is still worth linking.
                return new UpdateDecision(UpdateVerdict.SchemaTooNew, offered, null, manifest.releaseUrl, manifest.notes);
            }

            if (!UpdateUrl.IsTrusted(asset.url))
            {
                return None;
            }

            return new UpdateDecision(UpdateVerdict.Available, offered, asset, manifest.releaseUrl, manifest.notes);
        }
    }
}
