using NUnit.Framework;
using Vent.Core.Updates;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// latest.json round-trips through JsonUtility, and anything malformed degrades to "no
    /// update" rather than throwing into the menu.
    /// </summary>
    public sealed class UpdateManifestTests
    {
        private const string Json = @"{
          ""schema"": 1,
          ""version"": ""0.2.0"",
          ""releaseUrl"": ""https://github.com/brettbergin/vent/releases/tag/v0.2.0"",
          ""notes"": ""Adds the updater."",
          ""macos"":   { ""url"": ""https://github.com/brettbergin/vent/releases/download/v0.2.0/Vent-0.2.0-macOS.zip"",
                         ""sha256"": ""aaa"", ""sizeBytes"": 78301473, ""rootName"": ""Vent.app"" },
          ""windows"": { ""url"": ""https://github.com/brettbergin/vent/releases/download/v0.2.0/Vent-0.2.0-Windows-x64.zip"",
                         ""sha256"": ""bbb"", ""sizeBytes"": 38992527, ""rootName"": ""Vent-0.2.0-Windows-x64"" }
        }";

        [Test]
        public void ParsesTheShapeThatManifestShWrites()
        {
            UpdateManifest m = UpdateManifest.Parse(Json);
            Assert.IsNotNull(m);
            Assert.AreEqual(1, m.schema);
            Assert.AreEqual("0.2.0", m.version);
            Assert.AreEqual("Adds the updater.", m.notes);
            Assert.AreEqual(78301473L, m.macos.sizeBytes);
            Assert.AreEqual("Vent-0.2.0-Windows-x64", m.windows.rootName);
            Assert.IsTrue(m.macos.IsComplete);
        }

        [Test]
        public void SelectsThePlatformAsset()
        {
            UpdateManifest m = UpdateManifest.Parse(Json);
            Assert.AreEqual("aaa", m.AssetFor(UpdatePlatform.MacOS).sha256);
            Assert.AreEqual("bbb", m.AssetFor(UpdatePlatform.Windows).sha256);
            Assert.IsNull(m.AssetFor(UpdatePlatform.Unsupported));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("   ")]
        [TestCase("not json at all")]
        [TestCase("{\"schema\": 1}")]
        public void MalformedInputMeansNoUpdateRatherThanAnException(string json)
        {
            Assert.IsNull(UpdateManifest.Parse(json), json ?? "null");
        }

        [Test]
        public void ATruncatedFileDoesNotThrow()
        {
            Assert.DoesNotThrow(() => UpdateManifest.Parse(Json.Substring(0, 60)));
        }

        [Test]
        public void AnAssetMissingItsChecksumIsIncomplete()
        {
            var asset = new PlatformAsset { url = "https://x", sha256 = "", rootName = "Vent.app" };
            Assert.IsFalse(asset.IsComplete);
        }
    }
}
