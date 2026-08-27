using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Services;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// The car's voice: a synthesised engine loop whose pitch follows the engine speed (so every
    /// gear change is heard) and whose volume follows the throttle, a tyre loop that swells as the
    /// tyres slide, a door and a starter when the driver gets in, and a crunch on impacts. The two
    /// loops must be seamless, so they live on their own looping AudioSources rather than the
    /// one-shot voice ring.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource engine;
        [SerializeField] private AudioSource tyres;
        [SerializeField, Range(0f, 1f)] private float skidVolume = 0.5f;

        private VehicleController controller;
        private float fade;
        private float skid;

        public void Configure(AudioSource engineSource, AudioSource tyreSource)
        {
            engine = engineSource;
            tyres = tyreSource;
        }

        private void Awake()
        {
            controller = GetComponent<VehicleController>();
            if (engine != null)
            {
                engine.loop = true;
                engine.playOnAwake = false;
            }

            if (tyres != null)
            {
                tyres.loop = true;
                tyres.playOnAwake = false;
            }
        }

        private void OnEnable()
        {
            controller.OccupiedChanged += OnOccupied;
            controller.Impact += OnImpact;
        }

        private void OnDisable()
        {
            controller.OccupiedChanged -= OnOccupied;
            controller.Impact -= OnImpact;
        }

        private void OnOccupied(bool occupied)
        {
            Vector3 at = transform.position + Vector3.up;
            SfxPlayer.TryPlayAt(SoundId.CarDoor, at, 0.9f);
            if (!occupied)
            {
                return;
            }

            SfxPlayer.TryPlayAt(SoundId.CarStart, at, 0.8f);
            if (engine != null)
            {
                if (engine.clip == null)
                {
                    engine.clip = ProceduralSoundBank.Get(SoundId.EngineLoop);
                }

                fade = 0f;
                engine.Play();
            }

            if (tyres != null)
            {
                if (tyres.clip == null)
                {
                    tyres.clip = ProceduralSoundBank.Get(SoundId.TyreSkid);
                }

                tyres.volume = 0f;
                tyres.Play();
            }
        }

        private void OnImpact(float speed)
        {
            SfxPlayer.TryPlayAt(SoundId.CarImpact, transform.position + Vector3.up * 0.5f, Mathf.Clamp01(speed / 15f));
        }

        private void Update()
        {
            if (controller.Definition == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            bool running = controller.IsOccupied;
            float master = GameServices.TryGet(out SfxPlayer sfx) ? sfx.Volume : 1f;
            fade = Mathf.MoveTowards(fade, running ? 1f : 0f, dt / 0.4f);

            if (engine != null && engine.isPlaying)
            {
                float rpm = controller.Rpm01;
                float load = Mathf.Max(rpm * 0.6f, controller.Throttle01);
                engine.pitch = Mathf.Lerp(controller.Definition.EngineMinPitch, controller.Definition.EngineMaxPitch, rpm);
                engine.volume = Mathf.Lerp(controller.Definition.EngineMinVolume, controller.Definition.EngineMaxVolume, load) * fade * master;
                if (!running && fade <= 0f)
                {
                    engine.Stop();
                }
            }

            if (tyres != null && tyres.isPlaying)
            {
                float target = running ? controller.SkidIntensity : 0f;
                skid = Mathf.MoveTowards(skid, target, dt / 0.12f);
                tyres.volume = skid * skidVolume * master;
                tyres.pitch = Mathf.Lerp(0.9f, 1.15f, controller.Speed01);
                if (!running && skid <= 0f)
                {
                    tyres.Stop();
                }
            }
        }
    }
}
