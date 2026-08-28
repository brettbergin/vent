using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vent.Core.Audio
{
    /// <summary>
    /// Synthesises every <see cref="SoundId"/> into an <see cref="AudioClip"/> the first time it
    /// is requested, then caches it. This keeps the repository free of binary audio assets while
    /// still giving the game distinct, readable feedback for every event.
    ///
    /// The recipes are intentionally simple DSP: noise bursts with exponential envelopes,
    /// one-pole filters, and a few sine/saw tones. Each recipe is deterministic (fixed seed), so
    /// the same sound plays on every machine.
    /// </summary>
    public static class ProceduralSoundBank
    {
        public const int SampleRate = 22050;

        private static readonly Dictionary<SoundId, AudioClip> Cache = new();

        /// <summary>Get (or synthesise) the clip for a sound.</summary>
        public static AudioClip Get(SoundId id)
        {
            if (id == SoundId.None)
            {
                return null;
            }

            if (Cache.TryGetValue(id, out AudioClip cached) && cached != null)
            {
                return cached;
            }

            float[] samples = Synthesize(id);
            var clip = AudioClip.Create($"Sfx_{id}", samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            Cache[id] = clip;
            return clip;
        }

        /// <summary>Synthesise every clip up front (call from a loading screen).</summary>
        public static void WarmUp()
        {
            foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
            {
                Get(id);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Cache.Clear();

        private static float[] Synthesize(SoundId id)
        {
            var rng = new System.Random((int)id * 7919 + 13);
            switch (id)
            {
                // Gunshots are three layers: a hard high crack (the report), a low thump (the
                // pressure wave against the chest) and a longer, darker tail (the room answering).
                case SoundId.PistolShot:
                    return Mix(
                        Highpass(NoiseBurst(rng, 0.03f, 160f), 2500f, 0.9f),
                        Lowpass(NoiseBurst(rng, 0.16f, 34f), 4200f),
                        Tone(0.2f, 110f, 38f, 26f, 0.8f),
                        Lowpass(NoiseBurst(rng, 0.42f, 9f, 0.35f), 900f));
                case SoundId.SmgShot:
                    return Mix(
                        Highpass(NoiseBurst(rng, 0.025f, 200f), 3000f, 0.8f),
                        Lowpass(NoiseBurst(rng, 0.11f, 48f), 5200f),
                        Tone(0.13f, 140f, 55f, 40f, 0.55f),
                        Lowpass(NoiseBurst(rng, 0.3f, 12f, 0.3f), 1000f));
                case SoundId.DryFire:
                    return Highpass(NoiseBurst(rng, 0.035f, 120f), 2500f, 0.6f);
                case SoundId.ReloadStart:
                    return Mix(Highpass(NoiseBurst(rng, 0.05f, 90f), 1800f, 0.5f), Tone(0.06f, 1400f, 900f, 60f, 0.25f));
                case SoundId.ReloadMagIn:
                    // Magazine seating: a firm plastic-on-metal clack with a short body.
                    return Mix(Lowpass(NoiseBurst(rng, 0.05f, 110f), 2600f, 0.8f), Tone(0.07f, 520f, 300f, 60f, 0.4f));
                case SoundId.ReloadRack:
                    // Slide/bolt release: a bright metallic snap followed by the action slamming home.
                    return Mix(Highpass(NoiseBurst(rng, 0.03f, 140f), 3200f, 0.6f), Tone(0.04f, 1900f, 1200f, 90f, 0.3f),
                        Lowpass(NoiseBurst(rng, 0.09f, 60f), 1800f, 0.7f), Tone(0.1f, 300f, 180f, 45f, 0.45f));
                case SoundId.SlideLock:
                    return Mix(Highpass(NoiseBurst(rng, 0.03f, 150f), 2800f, 0.4f), Tone(0.05f, 1500f, 1100f, 80f, 0.25f));
                case SoundId.ReloadEnd:
                    return Mix(Lowpass(NoiseBurst(rng, 0.06f, 80f), 3000f, 0.5f), Tone(0.06f, 700f, 450f, 60f, 0.25f));
                case SoundId.WeaponDraw:
                    return Lowpass(NoiseBurst(rng, 0.12f, 30f), 2200f, 0.35f);
                case SoundId.HitMarker:
                    return Tone(0.05f, 2200f, 2200f, 80f, 0.4f);
                case SoundId.HeadshotMarker:
                    return Mix(Tone(0.06f, 2600f, 2600f, 70f, 0.4f), Tone(0.09f, 3900f, 3900f, 60f, 0.3f));
                case SoundId.ImpactConcrete:
                    return Lowpass(NoiseBurst(rng, 0.08f, 60f), 3500f, 0.5f);
                case SoundId.ImpactFlesh:
                    return Lowpass(NoiseBurst(rng, 0.1f, 45f), 900f, 0.6f);
                case SoundId.ZombieGrowl:
                    return Growl(rng, 0.9f, 70f, 95f, 0.6f);
                case SoundId.ZombieAttack:
                    return Growl(rng, 0.45f, 110f, 160f, 0.8f);
                case SoundId.ZombieHurt:
                    return Mix(Growl(rng, 0.25f, 160f, 120f, 0.5f), Lowpass(NoiseBurst(rng, 0.12f, 30f), 1200f, 0.4f));
                case SoundId.ZombieDeath:
                    return Mix(Growl(rng, 0.7f, 140f, 50f, 0.7f), Lowpass(NoiseBurst(rng, 0.4f, 8f), 800f, 0.35f));
                case SoundId.VentRattle:
                    return Rattle(rng, 0.8f);
                case SoundId.PlayerHurt:
                    return Mix(Tone(0.2f, 140f, 60f, 18f, 0.7f), Lowpass(NoiseBurst(rng, 0.15f, 25f), 600f, 0.4f));
                case SoundId.PlayerDeath:
                    return Mix(Tone(0.9f, 220f, 40f, 4f, 0.6f), Lowpass(NoiseBurst(rng, 0.8f, 5f), 500f, 0.4f));
                case SoundId.LevelUp:
                    return Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.14f, 0.5f);
                case SoundId.WeaponLevelUp:
                    return Arpeggio(new[] { 880f, 1318.5f }, 0.1f, 0.45f);
                case SoundId.Footstep:
                    return Lowpass(NoiseBurst(rng, 0.09f, 55f), 1000f, 0.3f);
                case SoundId.UiClick:
                    return Tone(0.04f, 1800f, 1800f, 90f, 0.3f);
                case SoundId.UiConfirm:
                    return Arpeggio(new[] { 659.25f, 987.77f }, 0.08f, 0.35f);
                case SoundId.PerkDrop:
                    // A soft bell: the orb landing.
                    return Mix(Tone(0.45f, 1318.5f, 1318.5f, 7f, 0.25f), Tone(0.45f, 1975.5f, 1975.5f, 9f, 0.12f));
                case SoundId.PerkPickup:
                    return Arpeggio(new[] { 659.25f, 880f, 1108.7f, 1318.5f, 1760f }, 0.07f, 0.45f);
                case SoundId.PerkNuke:
                    // A deep boom with a long rumble; the whole building answers.
                    return Mix(Tone(1.4f, 110f, 28f, 2.5f, 0.9f), Lowpass(NoiseBurst(rng, 1.2f, 3.5f), 420f, 0.7f), Highpass(NoiseBurst(rng, 0.12f, 30f), 2000f, 0.5f));
                case SoundId.DoorLocked:
                    // Push bar shoved against a dead bolt: the bar rattles, the frame answers once.
                    return Mix(Rattle(rng, 0.3f), Lowpass(NoiseBurst(rng, 0.08f, 70f), 1500f, 0.5f));
                case SoundId.DoorUnlock:
                    // Electric strike releasing, then a two-note chime from the card reader.
                    return Mix(Lowpass(NoiseBurst(rng, 0.06f, 90f), 1200f, 0.7f), Tone(0.12f, 180f, 120f, 30f, 0.5f), Arpeggio(new[] { 659.25f, 987.77f }, 0.09f, 0.25f));
                case SoundId.DoorOpen:
                    // A slow hinge: a falling tone with a breath of air and the closer's click at the end.
                    return Mix(Tone(0.7f, 320f, 210f, 3f, 0.18f), Lowpass(NoiseBurst(rng, 0.5f, 6f, 0.3f), 700f), Lowpass(NoiseBurst(rng, 0.05f, 120f), 2000f, 0.4f));
                case SoundId.EngineLoop:
                    return Engine(rng, seconds: 2f, baseHz: 55f);
                case SoundId.TyreSkid:
                    return Skid(rng, seconds: 1.5f);
                case SoundId.CarStart:
                    // Starter motor winding, then the engine catching.
                    return Mix(Tone(0.5f, 40f, 55f, 3f, 0.6f), Lowpass(NoiseBurst(rng, 0.45f, 6f), 700f, 0.5f), Tone(0.35f, 30f, 60f, 4f, 0.4f));
                case SoundId.CarDoor:
                    // A car door: a dull thump with a latch click on top.
                    return Mix(Lowpass(NoiseBurst(rng, 0.06f, 70f), 1500f, 0.8f), Tone(0.12f, 180f, 90f, 30f, 0.6f), Highpass(NoiseBurst(rng, 0.02f, 200f), 3000f, 0.3f));
                case SoundId.CarImpact:
                    // Sheet metal: a crunch, a low body thud and a bright sliver of glass.
                    return Mix(Lowpass(NoiseBurst(rng, 0.25f, 14f), 1200f, 0.9f), Tone(0.3f, 90f, 40f, 12f, 0.8f), Highpass(NoiseBurst(rng, 0.05f, 90f), 3000f, 0.4f));
                case SoundId.Roadkill:
                    // A body against a bumper: a wet thud and a cut-off growl.
                    return Mix(Lowpass(NoiseBurst(rng, 0.12f, 30f), 700f, 0.9f), Tone(0.18f, 120f, 50f, 20f, 0.7f), Growl(rng, 0.3f, 150f, 90f, 0.4f));
                case SoundId.CablePickup:
                    // A coil lifted off a shelf: nylon rustle with the connector clicking home.
                    return Mix(Highpass(NoiseBurst(rng, 0.07f, 65f), 2200f, 0.5f), Tone(0.05f, 900f, 620f, 70f, 0.25f));
                case SoundId.PanelDenied:
                    // A dead port: two flat clicks going nowhere.
                    return Mix(Arpeggio(new[] { 330f, 262f }, 0.05f, 0.22f), Lowpass(NoiseBurst(rng, 0.03f, 150f), 1600f, 0.35f));
                case SoundId.PowerRestore:
                    // Relays clunk, twelve racks spin their fans up, and the floor answers with a chime.
                    return Mix(Lowpass(NoiseBurst(rng, 0.12f, 55f), 900f, 0.8f), Tone(1.3f, 55f, 190f, 1.2f, 0.5f),
                        Lowpass(NoiseBurst(rng, 1.5f, 2.2f, 0.28f), 1400f), Arpeggio(new[] { 523.25f, 659.25f, 783.99f }, 0.12f, 0.28f));
                case SoundId.DrawerOpen:
                    // A pedestal drawer on steel runners, ending against its stop.
                    return Mix(Lowpass(NoiseBurst(rng, 0.35f, 11f, 0.4f), 1600f), Tone(0.3f, 220f, 155f, 8f, 0.2f),
                        Lowpass(NoiseBurst(rng, 0.05f, 95f), 1200f, 0.4f));
                case SoundId.ItemPickup:
                    // Paper lifted off a desk, and the thing on it settling into a pocket.
                    return Mix(Highpass(NoiseBurst(rng, 0.14f, 35f), 1800f, 0.45f), Lowpass(NoiseBurst(rng, 0.04f, 130f), 2600f, 0.45f), Tone(0.09f, 1500f, 950f, 45f, 0.18f));
                case SoundId.KeyPickup:
                    // Small brass: two bright partials and a scrape.
                    return Mix(Tone(0.12f, 2600f, 2600f, 40f, 0.25f), Tone(0.15f, 3400f, 3400f, 30f, 0.16f),
                        Highpass(NoiseBurst(rng, 0.05f, 120f), 4000f, 0.35f));
                default:
                    return new float[SampleRate / 10];
            }
        }

        /// <summary>
        /// A seamless engine loop: a saw at the base frequency with harmonics and a sub-octave, a slow
        /// amplitude wobble, and some breath. Every component completes a whole number of cycles in
        /// <paramref name="seconds"/> (55 Hz × 2 s = 110, the sub-octave 55, the wobble 22), so the
        /// buffer loops without a click; the noise tail is cross-faded into the head for the same reason.
        /// </summary>
        private static float[] Engine(System.Random rng, float seconds, float baseHz)
        {
            int n = Mathf.RoundToInt(seconds * SampleRate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float cycle = t * baseHz;
                float saw = 2f * (cycle - Mathf.Floor(cycle)) - 1f;
                float v = saw * 0.5f
                          + Mathf.Sin(2f * Mathf.PI * baseHz * 2f * t) * 0.25f
                          + Mathf.Sin(2f * Mathf.PI * baseHz * 3f * t) * 0.15f
                          + Mathf.Sin(2f * Mathf.PI * baseHz * 4f * t) * 0.08f
                          + Mathf.Sin(2f * Mathf.PI * baseHz * 0.5f * t) * 0.2f;
                float wobble = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 11f * t);
                float breath = ((float)rng.NextDouble() * 2f - 1f) * 0.15f;
                s[i] = v * wobble + breath;
            }

            Lowpass(s, 1800f);
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(s[i]));
            }

            float k = peak > 0f ? 0.8f / peak : 1f;
            for (int i = 0; i < n; i++)
            {
                s[i] *= k;
            }

            const int fade = 128;
            for (int i = 0; i < fade; i++)
            {
                float u = i / (float)fade;
                s[n - fade + i] = Mathf.Lerp(s[n - fade + i], s[i], u);
            }

            return s;
        }

        // ------------------------------------------------------------------ recipes

        /// <summary>White noise with an exponential decay envelope.</summary>
        /// <summary>Rubber dragged over asphalt: band-limited noise with the chatter of a juddering tyre, looped seamlessly.</summary>
        private static float[] Skid(System.Random rng, float seconds)
        {
            int n = Mathf.RoundToInt(seconds * SampleRate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                s[i] = (float)rng.NextDouble() * 2f - 1f;
            }

            Highpass(s, 900f);
            Lowpass(s, 2600f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float chatter = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 27f * t) * Mathf.Sin(2f * Mathf.PI * 3.1f * t);
                s[i] *= chatter;
            }

            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(s[i]));
            }

            float k = peak > 0f ? 0.8f / peak : 1f;
            for (int i = 0; i < n; i++)
            {
                s[i] *= k;
            }

            const int fade = 256;
            for (int i = 0; i < fade; i++)
            {
                float u = i / (float)fade;
                s[n - fade + i] = Mathf.Lerp(s[n - fade + i], s[i], u);
            }

            return s;
        }

        private static float[] NoiseBurst(System.Random rng, float seconds, float decayRate, float gain = 1f)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-decayRate * t);
                s[i] = ((float)rng.NextDouble() * 2f - 1f) * env * gain;
            }

            return s;
        }

        /// <summary>Sine sweep from <paramref name="startHz"/> to <paramref name="endHz"/> with exponential decay.</summary>
        private static float[] Tone(float seconds, float startHz, float endHz, float decayRate, float gain)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float u = n > 1 ? i / (float)(n - 1) : 0f;
                float hz = Mathf.Lerp(startHz, endHz, u);
                phase += 2f * Mathf.PI * hz / SampleRate;
                s[i] = Mathf.Sin(phase) * Mathf.Exp(-decayRate * t) * gain;
            }

            return s;
        }

        /// <summary>A rough voiced growl: saw wave with vibrato and breath noise, attack/decay envelope.</summary>
        private static float[] Growl(System.Random rng, float seconds, float startHz, float endHz, float gain)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float u = n > 1 ? i / (float)(n - 1) : 0f;
                float vibrato = 1f + 0.06f * Mathf.Sin(2f * Mathf.PI * 9f * t);
                float hz = Mathf.Lerp(startHz, endHz, u) * vibrato;
                phase += hz / SampleRate;
                phase -= Mathf.Floor(phase);
                float saw = phase * 2f - 1f;
                float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.35f;
                float env = Mathf.Min(1f, t * 25f) * Mathf.Exp(-3f * u);
                s[i] = (saw * 0.7f + noise) * env * gain;
            }

            return Lowpass(s, 1400f);
        }

        /// <summary>Metallic rattle: inharmonic partials with fast tremolo, like a loose grate.</summary>
        private static float[] Rattle(System.Random rng, float seconds)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var s = new float[n];
            float[] partials = { 310f, 473f, 812f, 1290f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float trem = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 14f * t);
                float v = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    v += Mathf.Sin(2f * Mathf.PI * partials[p] * t) / (p + 1f);
                }

                float env = Mathf.Exp(-2.5f * t);
                float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.15f;
                s[i] = (v * 0.35f * trem + noise) * env * 0.6f;
            }

            return s;
        }

        /// <summary>Sequence of pure tones, each fading into the next.</summary>
        private static float[] Arpeggio(float[] notesHz, float noteSeconds, float gain)
        {
            int per = Mathf.CeilToInt(noteSeconds * SampleRate);
            int tail = SampleRate / 4;
            var s = new float[per * notesHz.Length + tail];
            for (int k = 0; k < notesHz.Length; k++)
            {
                int start = k * per;
                int len = per + tail;
                for (int i = 0; i < len && start + i < s.Length; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Min(1f, t * 200f) * Mathf.Exp(-9f * t);
                    s[start + i] += Mathf.Sin(2f * Mathf.PI * notesHz[k] * t) * env * gain;
                }
            }

            return s;
        }

        // ------------------------------------------------------------------ filters & mixing

        private static float[] Lowpass(float[] s, float cutoffHz, float gain = 1f)
        {
            float rc = 1f / (2f * Mathf.PI * cutoffHz);
            float dt = 1f / SampleRate;
            float a = dt / (rc + dt);
            float y = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                y += a * (s[i] - y);
                s[i] = y * gain;
            }

            return s;
        }

        private static float[] Highpass(float[] s, float cutoffHz, float gain = 1f)
        {
            float rc = 1f / (2f * Mathf.PI * cutoffHz);
            float dt = 1f / SampleRate;
            float a = rc / (rc + dt);
            float prevX = 0f, prevY = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float x = s[i];
                float y = a * (prevY + x - prevX);
                prevX = x;
                prevY = y;
                s[i] = y * gain;
            }

            return s;
        }

        /// <summary>Sum buffers, then normalise to -1..1 with a little headroom.</summary>
        private static float[] Mix(params float[][] parts)
        {
            int n = 0;
            foreach (float[] p in parts)
            {
                n = Mathf.Max(n, p.Length);
            }

            var s = new float[n];
            foreach (float[] p in parts)
            {
                for (int i = 0; i < p.Length; i++)
                {
                    s[i] += p[i];
                }
            }

            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(s[i]));
            }

            if (peak > 0.9f)
            {
                float k = 0.9f / peak;
                for (int i = 0; i < n; i++)
                {
                    s[i] *= k;
                }
            }

            return s;
        }
    }
}
