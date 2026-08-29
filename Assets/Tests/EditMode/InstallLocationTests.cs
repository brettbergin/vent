using NUnit.Framework;
using Vent.Core.Updates;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// Where an update would write. The resolver takes dataPath as a parameter precisely so this
    /// can be pinned without a player: a mistake here hands the wrong directory to `rm -rf` or
    /// `robocopy /MIR`.
    /// </summary>
    public sealed class InstallLocationTests
    {
        private static InstallLocation Mac(string dataPath)
            => InstallLocation.Resolve(dataPath, UpdatePlatform.MacOS, isEditor: false);

        private static InstallLocation Win(string dataPath)
            => InstallLocation.Resolve(dataPath, UpdatePlatform.Windows, isEditor: false);

        [Test]
        public void ResolvesTheMacBundleFromTheRealDataPath()
        {
            // What a macOS player actually reports: Application.dataPath is the bundle's
            // Contents folder. Assuming Contents/Resources/Data made every macOS update fall
            // back to "download it manually" — caught only by installing a release and watching.
            InstallLocation loc = Mac("/Applications/Vent.app/Contents");
            Assert.IsTrue(loc.CanUpdate, "Contents is the layout a real player reports");
            Assert.AreEqual("/Applications/Vent.app", loc.Root);
            Assert.AreEqual("/Applications/Vent.app", loc.LaunchTarget);
        }

        [Test]
        public void AlsoAcceptsTheDataFolderLayout()
        {
            InstallLocation loc = Mac("/Applications/Vent.app/Contents/Resources/Data");
            Assert.IsTrue(loc.CanUpdate);
            Assert.AreEqual("/Applications/Vent.app", loc.Root);
        }

        [Test]
        public void HandlesAMacPathWithSpaces()
        {
            InstallLocation loc = Mac("/Users/sam/My Games/Vent.app/Contents");
            Assert.IsTrue(loc.CanUpdate);
            Assert.AreEqual("/Users/sam/My Games/Vent.app", loc.Root);
        }

        [Test]
        public void RefusesAContentsFolderThatIsNotInsideAnAppBundle()
        {
            Assert.IsFalse(Mac("/Users/sam/Downloads/Contents").CanUpdate);
        }

        [Test]
        public void RefusesATranslocatedBundle()
        {
            // Gatekeeper runs a quarantined app from a random read-only mount; swapping there
            // would write to the wrong place and leave the real copy stale.
            InstallLocation loc = Mac(
                "/private/var/folders/x4/abc/T/AppTranslocation/1E2D-44/d/Vent.app/Contents");
            Assert.IsFalse(loc.CanUpdate);
            Assert.AreEqual(InstallBlocker.Translocated, loc.Blocker);
            Assert.IsTrue(loc.BlockerMessage.Contains("Applications"));
        }

        [Test]
        public void ResolvesTheWindowsInstallFolder()
        {
            InstallLocation loc = Win(@"C:\Games\Vent-0.2.0-Windows-x64\Vent_Data");
            Assert.IsTrue(loc.CanUpdate);
            Assert.AreEqual("C:/Games/Vent-0.2.0-Windows-x64", loc.Root);
            Assert.AreEqual("C:/Games/Vent-0.2.0-Windows-x64/Vent.exe", loc.LaunchTarget);
        }

        [Test]
        public void HandlesAWindowsPathWithSpaces()
        {
            InstallLocation loc = Win(@"C:\Program Files\Vent\Vent_Data");
            Assert.IsTrue(loc.CanUpdate);
            Assert.AreEqual("C:/Program Files/Vent", loc.Root);
        }

        [Test]
        public void RefusesADriveRootWhichWouldMirrorOverTheWholeDisk()
        {
            Assert.AreEqual(InstallBlocker.Unrecognised, Win(@"C:\Vent_Data").Blocker);
        }

        [TestCase("/Applications/Vent.app/Contents/Resources")]
        [TestCase("/Users/sam/somewhere/else")]
        [TestCase("")]
        [TestCase(null)]
        public void RefusesAPathThatIsNotAMacInstall(string dataPath)
        {
            Assert.IsFalse(Mac(dataPath).CanUpdate, dataPath ?? "null");
        }

        [TestCase(@"C:\Games\Vent\Other_Data")]
        [TestCase("/opt/vent")]
        public void RefusesAPathThatIsNotAWindowsInstall(string dataPath)
        {
            Assert.IsFalse(Win(dataPath).CanUpdate, dataPath);
        }

        [Test]
        public void RefusesToRunFromTheEditor()
        {
            InstallLocation loc = InstallLocation.Resolve(
                "/Applications/Vent.app/Contents", UpdatePlatform.MacOS, isEditor: true);
            Assert.AreEqual(InstallBlocker.Editor, loc.Blocker);
        }

        [Test]
        public void RefusesAnUnsupportedPlatform()
        {
            InstallLocation loc = InstallLocation.Resolve("/whatever", UpdatePlatform.Unsupported, isEditor: false);
            Assert.AreEqual(InstallBlocker.UnsupportedPlatform, loc.Blocker);
        }

        [Test]
        public void EveryBlockerHasSomethingToTellThePlayer()
        {
            foreach (InstallBlocker blocker in System.Enum.GetValues(typeof(InstallBlocker)))
            {
                if (blocker == InstallBlocker.None)
                {
                    continue;
                }

                Assert.IsNotEmpty(InstallLocation.MessageFor(blocker), blocker.ToString());
            }
        }
    }
}
