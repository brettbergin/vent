using NUnit.Framework;
using Vent.Core.Updates;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// The comparison that decides whether to overwrite someone's install. Getting it wrong
    /// either hides every release or offers one on a loop.
    /// </summary>
    public sealed class SemVerTests
    {
        [TestCase("0.1.0", 0, 1, 0)]
        [TestCase("1.2.3", 1, 2, 3)]
        [TestCase("v2.0.11", 2, 0, 11)]
        [TestCase(" 1.0.0 ", 1, 0, 0)]
        public void ParsesWellFormedVersions(string text, int major, int minor, int patch)
        {
            Assert.IsTrue(SemVer.TryParse(text, out SemVer v), text);
            Assert.AreEqual(major, v.Major);
            Assert.AreEqual(minor, v.Minor);
            Assert.AreEqual(patch, v.Patch);
            Assert.IsFalse(v.IsPreRelease);
        }

        [Test]
        public void ParsesAPreReleaseTag()
        {
            Assert.IsTrue(SemVer.TryParse("1.0.0-rc1", out SemVer v));
            Assert.AreEqual("rc1", v.PreRelease);
            Assert.IsTrue(v.IsPreRelease);
        }

        [Test]
        public void DropsBuildMetadataWhichCarriesNoOrdering()
        {
            Assert.IsTrue(SemVer.TryParse("1.0.0-rc1+abc123", out SemVer v));
            Assert.AreEqual("rc1", v.PreRelease);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("1.0")]
        [TestCase("1.0.0.0")]
        [TestCase("banana")]
        [TestCase("1.x.0")]
        [TestCase("-1.0.0")]
        [TestCase("1.0.0-")]
        public void RejectsAnythingItCannotOrder(string text)
        {
            Assert.IsFalse(SemVer.TryParse(text, out _), text);
        }

        [Test]
        public void OrdersByMajorThenMinorThenPatch()
        {
            Assert.IsTrue(Parse("2.0.0") > Parse("1.9.9"));
            Assert.IsTrue(Parse("1.2.0") > Parse("1.1.9"));
            Assert.IsTrue(Parse("1.1.2") > Parse("1.1.1"));
            Assert.IsTrue(Parse("0.10.0") > Parse("0.9.0"), "10 must beat 9, not sort before it");
        }

        [Test]
        public void APreReleasePrecedesItsRelease()
        {
            Assert.IsTrue(Parse("1.0.0-rc1") < Parse("1.0.0"));
            Assert.IsTrue(Parse("1.0.0-rc1") < Parse("1.0.0-rc2"));
        }

        [Test]
        public void EqualVersionsCompareEqual()
        {
            Assert.AreEqual(Parse("1.2.3"), Parse("1.2.3"));
            Assert.IsTrue(Parse("1.2.3") == Parse("v1.2.3"));
            Assert.IsFalse(Parse("1.2.3") != Parse("1.2.3"));
        }

        [Test]
        public void RoundTripsThroughToString()
        {
            Assert.AreEqual("1.2.3", Parse("1.2.3").ToString());
            Assert.AreEqual("1.2.3-rc1", Parse("v1.2.3-rc1").ToString());
        }

        private static SemVer Parse(string text)
        {
            Assert.IsTrue(SemVer.TryParse(text, out SemVer v), text);
            return v;
        }
    }
}
