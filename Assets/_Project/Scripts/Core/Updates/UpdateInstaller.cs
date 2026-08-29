using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Vent.Core.Updates
{
    /// <summary>
    /// Hands a verified zip to a detached helper script, then quits so the helper can replace the
    /// files the running process is holding open.
    ///
    /// The extract and the swap live in the script rather than here on purpose: on macOS the
    /// archive has to be unpacked with <c>ditto</c> to keep the executable bit and the symlinks
    /// inside the bundle, and nothing in-process can replace a running player anyway.
    /// </summary>
    public static class UpdateInstaller
    {
        /// <summary>
        /// Writes the helper, starts it detached and returns true if the caller should now quit.
        /// Returns false — having changed nothing — if anything looks wrong.
        /// </summary>
        public static bool LaunchAndQuit(InstallLocation location, string zipPath, string rootName, string workingDir)
        {
            if (!location.CanUpdate)
            {
                Debug.LogWarning($"[Updater] refusing to install: {location.Blocker}");
                return false;
            }

            if (!File.Exists(zipPath))
            {
                Debug.LogWarning("[Updater] refusing to install: the downloaded archive is gone");
                return false;
            }

            // Last line of defence before handing a path to `rm -rf` / `robocopy /MIR`.
            if (!LooksLikeVent(location))
            {
                Debug.LogWarning($"[Updater] refusing to install: {location.Root} does not look like a Vent install");
                return false;
            }

            try
            {
                int pid = Process.GetCurrentProcess().Id;
                string stage = Path.Combine(workingDir, "stage");
                string log = Path.Combine(workingDir, "update.log");

                if (location.Platform == UpdatePlatform.MacOS)
                {
                    string script = Path.Combine(workingDir, "update.sh");
                    File.WriteAllText(script, UpdateScripts.MacOs(pid, zipPath, stage, location.Root, log));
                    Chmod(script);
                    StartDetached("/bin/bash", $"-c {UpdateScripts.ShellQuote($"nohup {UpdateScripts.ShellQuote(script)} >/dev/null 2>&1 &")}");
                }
                else
                {
                    string script = Path.Combine(workingDir, "update.cmd");
                    File.WriteAllText(script,
                        UpdateScripts.Windows(pid, zipPath, stage, location.Root, location.LaunchTarget, rootName, log));
                    StartDetached("cmd.exe", $"/c start \"Vent updater\" /min \"{script}\"");
                }

                Debug.Log($"[Updater] helper started; quitting to let it replace {location.Root}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Updater] could not start the helper: {e.Message}");
                return false;
            }
        }

        private static bool LooksLikeVent(InstallLocation location)
        {
            if (string.IsNullOrEmpty(location.Root))
            {
                return false;
            }

            return location.Platform switch
            {
                UpdatePlatform.MacOS => Directory.Exists(location.Root)
                                        && location.Root.EndsWith(".app", StringComparison.Ordinal)
                                        && File.Exists(Path.Combine(location.Root, "Contents", "MacOS", "Vent")),
                UpdatePlatform.Windows => Directory.Exists(location.Root)
                                          && File.Exists(Path.Combine(location.Root, "Vent.exe"))
                                          && Directory.Exists(Path.Combine(location.Root, "Vent_Data")),
                _ => false,
            };
        }

        /// <summary>Can the game actually replace its own install, or is it somewhere read-only?</summary>
        public static bool IsWritable(InstallLocation location)
        {
            try
            {
                string parent = Path.GetDirectoryName(location.Root);
                if (string.IsNullOrEmpty(parent))
                {
                    return false;
                }

                string probe = Path.Combine(parent, $".vent-write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Chmod(string path)
        {
            using var chmod = Process.Start(new ProcessStartInfo("/bin/chmod", $"+x {UpdateScripts.ShellQuote(path)}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            chmod?.WaitForExit(5000);
        }

        private static void StartDetached(string fileName, string arguments)
        {
            Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
    }
}
