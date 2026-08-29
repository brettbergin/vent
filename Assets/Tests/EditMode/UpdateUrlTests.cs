using NUnit.Framework;
using Vent.Core.Updates;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// The URL rules. Defence in depth behind the SHA-256 check, but cheap to pin.
    /// </summary>
    public sealed class UpdateUrlTests
    {
        [Test]
        public void AcceptsAReleaseAssetOfThisRepository()
        {
            Assert.IsTrue(UpdateUrl.IsTrusted(
                "https://github.com/brettbergin/vent/releases/download/v0.2.0/Vent-0.2.0-macOS.zip"));
        }

        [TestCase("https://example.com/evil.zip")]
        [TestCase("http://github.com/brettbergin/vent/releases/download/v0.2.0/x.zip")]
        [TestCase("https://github.com/someone/else/releases/download/v0.2.0/x.zip")]
        [TestCase("https://github.com.evil.test/brettbergin/vent/releases/download/v0.2.0/x.zip")]
        [TestCase("https://github.com/brettbergin/vent/releases/download/../../../x.zip")]
        [TestCase("")]
        [TestCase(null)]
        public void RejectsAnythingElse(string url)
        {
            Assert.IsFalse(UpdateUrl.IsTrusted(url), url ?? "null");
        }

        [Test]
        public void TheManifestPermalinkPointsAtTheLatestRelease()
        {
            // The permalink, not api.github.com: it is CDN-served with no rate limit and skips
            // prereleases.
            Assert.AreEqual(
                "https://github.com/brettbergin/vent/releases/latest/download/latest.json",
                UpdateUrl.ManifestUrl);
        }
    }
}
