using System;
using NUnit.Framework;
using Vent.Core.Updates;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// The helper scripts delete and overwrite directories. A path with a space that is not
    /// quoted correctly is the one bug in this feature that destroys someone's files, so the
    /// generated text is pinned rather than eyeballed.
    /// </summary>
    public sealed class UpdateScriptsTests
    {
        [Test]
        public void ShellQuoteWrapsInSingleQuotes()
        {
            Assert.AreEqual("'/Applications/Vent.app'", UpdateScripts.ShellQuote("/Applications/Vent.app"));
            Assert.AreEqual("'/Users/sam/My Games/Vent.app'", UpdateScripts.ShellQuote("/Users/sam/My Games/Vent.app"));
        }

        [Test]
        public void ShellQuoteEscapesAnEmbeddedSingleQuote()
        {
            // sam's Games → 'sam'\''s Games'
            Assert.AreEqual(@"'/Users/sam'\''s Games/Vent.app'", UpdateScripts.ShellQuote("/Users/sam's Games/Vent.app"));
        }

        [Test]
        public void ShellQuoteNeutralisesShellMetacharacters()
        {
            string quoted = UpdateScripts.ShellQuote("/tmp/a b; rm -rf ~");
            Assert.AreEqual("'/tmp/a b; rm -rf ~'", quoted);
        }

        [Test]
        public void CmdAssignQuotesTheWholeAssignmentNotTheValue()
        {
            // set "VAR=C:\x", not set VAR="C:\x": the latter stores the quote characters and
            // breaks both %VAR% in a quoted argument and $env:VAR in the PowerShell call.
            Assert.AreEqual("set \"ZIP=C:\\Program Files\\Vent\\a.zip\"",
                            UpdateScripts.CmdAssign("ZIP", "C:\\Program Files\\Vent\\a.zip"));
        }

        [TestCase("C:\\a\"b")]
        [TestCase("C:\\%PATH%\\x")]
        public void CmdAssignRefusesWhatBatchCannotExpress(string value)
        {
            Assert.Throws<ArgumentException>(() => UpdateScripts.CmdAssign("ZIP", value));
        }

        [Test]
        public void TheMacScriptQuotesEveryPathAndRollsBack()
        {
            string script = UpdateScripts.MacOs(
                1234,
                "/Users/sam/Library/Application Support/Vent Studio/Vent/updates/Vent-0.2.0.zip",
                "/Users/sam/Library/Application Support/Vent Studio/Vent/updates/stage",
                "/Applications/My Games/Vent.app",
                "/Users/sam/Library/Application Support/Vent Studio/Vent/updates/update.log");

            StringAssert.Contains("APP='/Applications/My Games/Vent.app'", script);
            StringAssert.Contains("PID=1234", script);

            // ditto, never System.IO.Compression: a zip round-trip through ZipFile drops the
            // executable bit and the bundle will not launch.
            StringAssert.Contains("ditto -x -k", script);
            StringAssert.Contains("mv \"$APP\" \"$APP.old\"", script);
            StringAssert.Contains("mv \"$APP.old\" \"$APP\"", script, "a failed copy must roll back");
            StringAssert.Contains("xattr -dr com.apple.quarantine", script);
            StringAssert.Contains("open \"$APP\"", script);

            // Every path must reach the shell through a variable, never interpolated bare.
            StringAssert.DoesNotContain("/Applications/My Games/Vent.app/Contents", script);
        }

        [Test]
        public void TheMacScriptGivesUpRatherThanWaitingForever()
        {
            string script = UpdateScripts.MacOs(1, "/a.zip", "/stage", "/Vent.app", "/log");
            StringAssert.Contains("aborting", script);
        }

        [Test]
        public void TheWindowsScriptQuotesEveryPathAndChecksRobocopy()
        {
            string script = UpdateScripts.Windows(
                4321,
                @"C:\Users\sam\AppData\LocalLow\Vent Studio\Vent\updates\Vent-0.2.0.zip",
                @"C:\Users\sam\AppData\LocalLow\Vent Studio\Vent\updates\stage",
                @"C:\Program Files\Vent",
                @"C:\Program Files\Vent\Vent.exe",
                "Vent-0.2.0-Windows-x64",
                @"C:\Users\sam\AppData\LocalLow\Vent Studio\Vent\updates\update.log");

            StringAssert.Contains("set \"INSTALL=C:\\Program Files\\Vent\"", script);
            StringAssert.Contains("set \"NEW=C:\\Users\\sam\\AppData\\LocalLow\\Vent Studio\\Vent\\updates\\stage\\Vent-0.2.0-Windows-x64\"", script);
            StringAssert.Contains("robocopy \"%NEW%\" \"%INSTALL%\" /MIR", script);

            // robocopy reports 0-7 for success; treating any non-zero as failure would break
            // every normal run.
            StringAssert.Contains("if %ERRORLEVEL% GEQ 8", script);
            StringAssert.Contains("if not exist \"%NEW%\\Vent.exe\"", script, "never mirror an archive that is not Vent");
            StringAssert.Contains("start \"\" \"%EXE%\"", script);
        }

        [Test]
        public void TheWindowsScriptUsesCrLfSoBatchCanReadIt()
        {
            string script = UpdateScripts.Windows(1, "a.zip", "s", "i", "i\\Vent.exe", "root", "l.log");
            StringAssert.Contains("\r\n", script);
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(script, "(?<!\r)\n"),
                           "a bare LF in a .cmd breaks older cmd.exe parsing");
        }
    }
}
