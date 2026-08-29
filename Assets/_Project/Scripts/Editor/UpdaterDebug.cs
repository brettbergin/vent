using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Vent.Core.Updates;

namespace Vent.Editor
{
    /// <summary>
    /// Writes the updater's helper script to disk so it can be exercised for real against a
    /// throwaway install, rather than trusting a copy of it pasted into a shell test.
    ///
    /// Editor-only, so none of this reaches a player. Driven by tools/test-update-swap.sh:
    ///   VENT_DUMP_* env vars in, one script file out.
    /// </summary>
    public static class UpdaterDebug
    {
        public static void DumpMacScript()
        {
            string outPath = Env("VENT_DUMP_OUT");
            string script = UpdateScripts.MacOs(
                int.Parse(Env("VENT_DUMP_PID")),
                Env("VENT_DUMP_ZIP"),
                Env("VENT_DUMP_STAGE"),
                Env("VENT_DUMP_APP"),
                Env("VENT_DUMP_LOG"));

            File.WriteAllText(outPath, script);
            Debug.Log($"[Vent] wrote {outPath}");

            // No EditorApplication.Exit(0) here: -quit already shuts the editor down, and
            // calling both races the shutdown into an illegal instruction.
        }

        private static string Env(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value))
            {
                Debug.LogError($"[Vent] {name} is not set");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                throw new InvalidOperationException(name);
            }

            return value;
        }
    }
}
