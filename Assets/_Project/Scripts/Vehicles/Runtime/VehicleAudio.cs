using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Services;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// The car's voice: a synthesised engine loop whose pitch and volume follow engine load, a door
    /// and a starter when the driver gets in, and a crunch on impacts. The loop is the one sound in
    /// the game that must be seamless, so it lives on its own looping AudioSource rather than the
    /// one-shot voice ring.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource engine;

        private VehicleController controller;
        private float fade;

        public void Configure(AudioSource source) => engine = source;

        private void Awake()
        {
            controller = GetComponent<VehicleController>();
            if (engine != null)
            {
                engine.loop = true;
                engine.playOnAwake = false;
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
            if (!occupied || engine == null)
            {
                return;
            }

            SfxPlayer.TryPlayAt(SoundId.CarStart, at, 0.8f);
            if (engine.clip == null)
            {
                engine.clip = ProceduralSoundBank.Get(SoundId.EngineLoop);
            }

            fade = 0f;
            engine.Play();
        }

        private void OnImpact(float speed)
        {
            SfxPlayer.TryPlayAt(SoundId.CarImpact, transform.position + Vector3.up * 0.5f, Mathf.Clamp01(speed / 15f));
        }

        private void Update()
        {
            if (engine == null || !engine.isPlaying || controller.Definition == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            bool running = controller.IsOccupied;
            fade = Mathf.MoveTowards(fade, running ? 1f : 0f, dt / 0.4f);
            float master = GameServices.TryGet(out SfxPlayer sfx) ? sfx.Volume : 1f;
            float rpm = controller.Rpm01;
            engine.pitch = Mathf.Lerp(controller.Definition.EngineMinPitch, controller.Definition.EngineMaxPitch, rpm);
            engine.volume = Mathf.Lerp(controller.Definition.EngineMinVolume, controller.Definition.EngineMaxVolume, rpm) * fade * master;
            if (!running && fade <= 0f)
            {
                engine.Stop();
            }
        }
    }
}
