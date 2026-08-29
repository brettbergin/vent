using NUnit.Framework;
using Vent.Core.Updates;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// Whether a fetched manifest becomes an offer. Every branch here either overwrites the
    /// player's install or declines to, so all of them are pinned.
    /// </summary>
    public sealed class UpdateDecisionTests
    {
        private const string Url =
            "https://github.com/brettbergin/vent/releases/download/v0.2.0/Vent-0.2.0-macOS.zip";

        private static UpdateManifest Manifest(string version = "0.2.0", int schema = 1, string url = Url)
        {
            return new UpdateManifest
            {
                schema = schema,
                version = version,
                releaseUrl = "https://github.com/brettbergin/vent/releases/tag/v" + version,
                notes = "notes",
                macos = new PlatformAsset { url = url, sha256 = "abc", sizeBytes = 10, rootName = "Vent.app" },
                windows = new PlatformAsset
                {
                    url = "https://github.com/brettbergin/vent/releases/download/v0.2.0/Vent-0.2.0-Windows-x64.zip",
                    sha256 = "def",
                    sizeBytes = 10,
                    rootName = "Vent-0.2.0-Windows-x64",
                },
            };
        }

        [Test]
        public void OffersANewerRelease()
        {
            UpdateDecision d = UpdateDecision.Evaluate(Manifest(), "0.1.0", UpdatePlatform.MacOS);
            Assert.AreEqual(UpdateVerdict.Available, d.Verdict);
            Assert.IsTrue(d.CanInstall);
            Assert.AreEqual("0.2.0", d.Version.ToString());
            Assert.AreEqual("Vent.app", d.Asset.rootName);
        }

        [Test]
        public void PicksTheAssetForTheRunningPlatform()
        {
            UpdateDecision d = UpdateDecision.Evaluate(Manifest(), "0.1.0", UpdatePlatform.Windows);
            Assert.AreEqual("Vent-0.2.0-Windows-x64", d.Asset.rootName);
        }

        [Test]
        public void IgnoresTheSameVersion()
        {
            UpdateDecision d = UpdateDecision.Evaluate(Manifest("0.1.0"), "0.1.0", UpdatePlatform.MacOS);
            Assert.AreEqual(UpdateVerdict.None, d.Verdict);
        }

        [Test]
        public void NeverDowngrades()
        {
            // A release rolled back to "latest" must not drag everyone backwards.
            UpdateDecision d = UpdateDecision.Evaluate(Manifest("0.1.0"), "0.3.0", UpdatePlatform.MacOS);
            Assert.AreEqual(UpdateVerdict.None, d.Verdict);
        }

        [Test]
        public void HonoursASkippedVersion()
        {
            UpdateDecision d = UpdateDecision.Evaluate(Manifest(), "0.1.0", UpdatePlatform.MacOS, "0.2.0");
            Assert.AreEqual(UpdateVerdict.Skipped, d.Verdict);
            Assert.IsFalse(d.CanInstall);
        }

        [Test]
        public void ASkipDoesNotSuppressALaterVersion()
        {
            UpdateDecision d = UpdateDecision.Evaluate(Manifest("0.3.0"), "0.1.0", UpdatePlatform.MacOS, "0.2.0");
            Assert.AreEqual(UpdateVerdict.Available, d.Verdict);
        }

        [Test]
        public void ANewerSchemaIsLinkedButNeverInstalled()
        {
            UpdateDecision d = UpdateDecision.Evaluate(Manifest(schema: 99), "0.1.0", UpdatePlatform.MacOS);
            Assert.AreEqual(UpdateVerdict.SchemaTooNew, d.Verdict);
            Assert.IsFalse(d.CanInstall, "an unknown schema must not be installed");
            Assert.IsTrue(d.IsNewer, "but the player should still be told it exists");
        }

        [Test]
        public void RefusesAnAssetHostedSomewhereElse()
        {
            UpdateDecision d = UpdateDecision.Evaluate(
                Manifest(url: "https://example.com/evil.zip"), "0.1.0", UpdatePlatform.MacOS);
            Assert.AreEqual(UpdateVerdict.None, d.Verdict);
        }

        [Test]
        public void AMissingPlatformAssetIsLinkedNotInstalled()
        {
            UpdateManifest m = Manifest();
            m.macos = null;
            UpdateDecision d = UpdateDecision.Evaluate(m, "0.1.0", UpdatePlatform.MacOS);
            Assert.IsFalse(d.CanInstall);
            Assert.IsTrue(d.IsNewer);
        }

        [Test]
        public void AnIncompleteAssetIsNotInstalled()
        {
            UpdateManifest m = Manifest();
            m.macos.sha256 = "";
            UpdateDecision d = UpdateDecision.Evaluate(m, "0.1.0", UpdatePlatform.MacOS);
            Assert.IsFalse(d.CanInstall, "no checksum means no install");
        }

        [Test]
        public void AnUnreadableLocalVersionOffersNothing()
        {
            // Otherwise an unparseable Application.version would read as 0.0.0 and offer
            // an update on every single launch.
            UpdateDecision d = UpdateDecision.Evaluate(Manifest(), "not-a-version", UpdatePlatform.MacOS);
            Assert.AreEqual(UpdateVerdict.None, d.Verdict);
        }

        [Test]
        public void HandlesANullManifestAndAnUnsupportedPlatform()
        {
            Assert.AreEqual(UpdateVerdict.None, UpdateDecision.Evaluate(null, "0.1.0", UpdatePlatform.MacOS).Verdict);
            Assert.AreEqual(UpdateVerdict.None, UpdateDecision.Evaluate(Manifest(), "0.1.0", UpdatePlatform.Unsupported).Verdict);
        }
    }
}
