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
