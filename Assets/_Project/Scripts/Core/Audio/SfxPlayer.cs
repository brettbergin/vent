using UnityEngine;
using UnityEngine.Audio;
using Vent.Core.Services;

namespace Vent.Core.Audio
{
    /// <summary>
    /// Fire-and-forget sound playback through a fixed ring of <see cref="AudioSource"/>s.
    /// Avoids <c>AudioSource.PlayClipAtPoint</c>, which allocates a GameObject per call.
    /// Registered in <see cref="GameServices"/> so any system can play a sound without a reference.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SfxPlayer : MonoBehaviour
    {
        [SerializeField, Range(1, 64)] private int voices = 24;
        [SerializeField] private AudioMixerGroup outputGroup;
        [SerializeField, Min(0f)] private float maxDistance = 40f;

        private AudioSource[] sources;
        private int next;

        /// <summary>Global volume multiplier (0..1), set from the settings screen.</summary>
        public float Volume { get; set; } = 1f;

        private void Awake()
        {
            sources = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject($"Voice_{i}");
                go.transform.SetParent(transform, false);
                AudioSource src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.outputAudioMixerGroup = outputGroup;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 1.5f;
                src.maxDistance = maxDistance;
                src.dopplerLevel = 0f;
                sources[i] = src;
            }
        }

        private void OnEnable() => GameServices.Register(this);
        private void OnDisable() => GameServices.Unregister(this);

        /// <summary>Play a 3D sound at a world position.</summary>
        public void PlayAt(SoundId id, Vector3 position, float volume = 1f, float pitch = 1f, float pitchJitter = 0.05f)
        {
            Play(id, position, 1f, volume, pitch, pitchJitter);
        }

        /// <summary>Play a 2D (non-spatial) sound: UI, player-local feedback.</summary>
        public void Play2D(SoundId id, float volume = 1f, float pitch = 1f, float pitchJitter = 0.03f)
        {
            Play(id, transform.position, 0f, volume, pitch, pitchJitter);
        }

        private void Play(SoundId id, Vector3 position, float spatialBlend, float volume, float pitch, float pitchJitter)
        {
            AudioClip clip = ProceduralSoundBank.Get(id);
            if (clip == null || sources == null)
            {
                return;
            }

            AudioSource src = sources[next];
            next = (next + 1) % sources.Length;

            src.transform.position = position;
            src.spatialBlend = spatialBlend;
            src.pitch = pitch + Random.Range(-pitchJitter, pitchJitter);
            src.volume = Mathf.Clamp01(volume * Volume);
            src.clip = clip;
            src.Play();
        }

        /// <summary>Convenience for callers that may run before the scene registers a player (tests, editor).</summary>
        public static void TryPlayAt(SoundId id, Vector3 position, float volume = 1f)
        {
            if (GameServices.TryGet(out SfxPlayer player))
            {
                player.PlayAt(id, position, volume);
            }
        }

        public static void TryPlay2D(SoundId id, float volume = 1f)
        {
            if (GameServices.TryGet(out SfxPlayer player))
            {
                player.Play2D(id, volume);
            }
        }
    }
}
