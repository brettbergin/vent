using System;

namespace Vent.Core.Updates
{
    /// <summary>
    /// Where the updater looks, and what it is willing to download from. Pure, so the URL rules
    /// are unit tested rather than discovered in the field.
    /// </summary>
    public static class UpdateUrl
    {
        public const string Repo = "brettbergin/vent";

        /// <summary>
        /// The /releases/latest/download/ permalink, not the REST API: it is CDN-served with no
        /// rate limit (the API allows 60 unauthenticated requests an hour per IP), it redirects to
        /// whatever the newest release is, and it skips prereleases.
        /// </summary>
        public const string ManifestUrl = "https://github.com/" + Repo + "/releases/latest/download/latest.json";

        public const string ReleasesUrl = "https://github.com/" + Repo + "/releases/latest";

        private const string AssetPrefix = "https://github.com/" + Repo + "/releases/download/";

        /// <summary>
        /// A download must be a release asset of this repository over HTTPS.
        ///
        /// This is defence in depth, not the security control — the SHA-256 in the manifest is
        /// what actually decides whether a downloaded file gets installed. Note that the redirect
        /// target is deliberately not pinned: GitHub currently serves assets from
        /// release-assets.githubusercontent.com, it used to be objects.githubusercontent.com, and
        /// pinning a host that changes would break the updater in the field.
        /// </summary>
        public static bool IsTrusted(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string trimmed = url.Trim();

            // Ordinal, so a Unicode-confusable or differently-cased host cannot slip past.
            if (!trimmed.StartsWith(AssetPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            // No path traversal back out of the release into some other repository.
            return trimmed.IndexOf("..", StringComparison.Ordinal) < 0;
        }
    }
}
