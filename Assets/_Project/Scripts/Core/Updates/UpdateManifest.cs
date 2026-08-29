using System;

namespace Vent.Core.Updates
{
    /// <summary>One platform's download in <see cref="UpdateManifest"/>.</summary>
    [Serializable]
    public sealed class PlatformAsset
    {
        public string url;
        public string sha256;
        public long sizeBytes;

        /// <summary>What the zip contains at its top level: "Vent.app", or the Windows folder name.</summary>
        public string rootName;

        public bool IsComplete =>
            !string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(sha256) && !string.IsNullOrEmpty(rootName);
    }

    /// <summary>
    /// latest.json, published as a release asset and fetched from the /releases/latest/download/
    /// permalink. JsonUtility cannot deserialise dictionaries, so the platforms are named fields
    /// rather than a map.
    /// </summary>
    [Serializable]
    public sealed class UpdateManifest
    {
        /// <summary>The schema this file is written in. See <see cref="SupportedSchema"/>.</summary>
        public int schema;

        public string version;
        public string releaseUrl;
        public string notes;

        public PlatformAsset macos;
        public PlatformAsset windows;

        /// <summary>
        /// The highest schema this build understands. A manifest above it is not an error: the
        /// updater still shows the banner and links the release page, it just will not try to
        /// install something written to rules it does not know. That keeps a future schema change
        /// from bricking already-installed copies.
        /// </summary>
        public const int SupportedSchema = 1;

        public static UpdateManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                UpdateManifest manifest = UnityEngine.JsonUtility.FromJson<UpdateManifest>(json);
                return manifest != null && !string.IsNullOrEmpty(manifest.version) ? manifest : null;
            }
            catch (Exception)
            {
                // Malformed JSON means no update, never an exception into the caller.
                return null;
            }
        }

        public PlatformAsset AssetFor(UpdatePlatform platform) => platform switch
        {
            UpdatePlatform.MacOS => macos,
            UpdatePlatform.Windows => windows,
            _ => null,
        };
    }

    public enum UpdatePlatform
    {
        Unsupported = 0,
        MacOS = 1,
        Windows = 2,
    }
}
