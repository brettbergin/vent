using System;
using UnityEngine;

namespace Vent.Core.Settings
{
    /// <summary>
    /// Player-facing settings backed by PlayerPrefs. Static so both the UI (writer) and the
    /// player/audio systems (readers) reach it without a scene reference; <see cref="Changed"/>
    /// lets readers react immediately.
    /// </summary>
    public static class SettingsStore
    {
        private const string SensitivityKey = "vent.sensitivity";
        private const string InvertYKey = "vent.invertY";
        private const string VolumeKey = "vent.volume";
        private const string AutoCheckUpdatesKey = "vent.updates.autoCheck";
        private const string SkippedUpdateVersionKey = "vent.updates.skippedVersion";
        private const string LastUpdateCheckKey = "vent.updates.lastCheckUtc";

        public const float DefaultSensitivity = 1f;
        public const float MinSensitivity = 0.2f;
        public const float MaxSensitivity = 3f;

        private static float sensitivity = PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity);
        private static bool invertY = PlayerPrefs.GetInt(InvertYKey, 0) == 1;
        private static float volume = PlayerPrefs.GetFloat(VolumeKey, 0.8f);

        /// <summary>Raised after any setting changes.</summary>
        public static event Action Changed;

        public static float Sensitivity
        {
            get => sensitivity;
            set => Set(ref sensitivity, Mathf.Clamp(value, MinSensitivity, MaxSensitivity), SensitivityKey);
        }

        public static bool InvertY
        {
            get => invertY;
            set
            {
                if (invertY == value)
                {
                    return;
                }

                invertY = value;
                PlayerPrefs.SetInt(InvertYKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static float Volume
        {
            get => volume;
            set => Set(ref volume, Mathf.Clamp01(value), VolumeKey);
        }

        // --- updates ----------------------------------------------------------
        // These are read by UpdateService rather than the UI, so they do not raise Changed:
        // nothing re-reads them mid-session, and GameManager subscribes to Changed for audio
        // and sensitivity, which an update check has no business touching.

        /// <summary>Look for a new release on launch. On by default.</summary>
        public static bool AutoCheckUpdates
        {
            get => PlayerPrefs.GetInt(AutoCheckUpdatesKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(AutoCheckUpdatesKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>A version the player dismissed; empty for none. Only ever an exact match.</summary>
        public static string SkippedUpdateVersion
        {
            get => PlayerPrefs.GetString(SkippedUpdateVersionKey, string.Empty);
            set
            {
                PlayerPrefs.SetString(SkippedUpdateVersionKey, value ?? string.Empty);
                PlayerPrefs.Save();
            }
        }

        /// <summary>When the last check ran, so a relaunch loop cannot hammer GitHub.</summary>
        public static DateTime LastUpdateCheckUtc
        {
            get => long.TryParse(PlayerPrefs.GetString(LastUpdateCheckKey, string.Empty), out long ticks)
                ? new DateTime(ticks, DateTimeKind.Utc)
                : DateTime.MinValue;
            set
            {
                PlayerPrefs.SetString(LastUpdateCheckKey, value.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                PlayerPrefs.Save();
            }
        }

        private static void Set(ref float field, float value, string key)
        {
            if (Mathf.Approximately(field, value))
            {
                return;
            }

            field = value;
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
