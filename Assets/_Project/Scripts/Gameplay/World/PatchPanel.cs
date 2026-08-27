using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Interaction;
using Vent.Core.Utility;

namespace Vent.Gameplay.World
{
    /// <summary>
    /// The patch panel on a server rack: plug in the cables and the floor's power comes back, and
    /// with it one desk's monitor. Lives on its own child of the rack, never on the rack root —
    /// <c>PlayerInteractor</c> resolves an interactable by walking up from whatever the ray hit,
    /// so a component on the root would turn a two-metre rack into one big button.
    ///
    /// Every rack has one; <see cref="KeyHuntDirector"/> shows exactly one per run, so the player
    /// is looking for a specific rack rather than mashing Interact down the aisle.
    /// </summary>
    public sealed class PatchPanel : MonoBehaviour, IInteractable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private KeyHuntDirector hunt;
        [SerializeField, Tooltip("The port lamps: amber while the floor is dead, green once it is patched.")]
        private Renderer[] leds = System.Array.Empty<Renderer>();
        [SerializeField, Tooltip("The beacon that picks this rack out from the twelve blade LEDs beside it.")]
        private Light beacon;
        [SerializeField] private Color deadColor = new(1f, 0.6f, 0.1f);
        [SerializeField] private Color liveColor = new(0.2f, 1f, 0.3f);
        [SerializeField] private string readyPrompt = "PLUG IN THE PATCH CABLES";
        [SerializeField] private string shortPrompt = "PATCH PANEL  -  NEED {0} MORE CABLE(S)";

        private MaterialPropertyBlock block;
        private Cooldown denied;
        private bool restored;

        public bool IsRestored => restored;

        /// <summary>Editor-time wiring used by the building generator.</summary>
        public void Configure(KeyHuntDirector director, Renderer[] portLeds, Light glow)
        {
            hunt = director;
            leds = portLeds;
            beacon = glow;
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            SetLeds(false);
        }

        // ----- IInteractable -----
        public string Prompt
        {
            get
            {
                if (restored || hunt == null)
                {
                    return string.Empty;
                }

                KeyHuntState state = hunt.State;
                int missing = state.CablesRequired - state.CablesHeld;
                return missing <= 0 ? readyPrompt : string.Format(shortPrompt, missing);
            }
        }

        public bool IsAvailable => !restored && hunt != null && hunt.State.CanPatch;

        public void Interact()
        {
            if (hunt == null)
            {
                return;
            }

            // Interact() arrives even when IsAvailable is false, so the empty-handed case has to be
            // an answer rather than a no-op: the panel clicks back, on a cooldown so holding the
            // key down does not machine-gun it.
            switch (hunt.TryRestorePower(this))
            {
                case PanelAction.NotEnoughCables:
                    if (denied.TryConsume(Time.time, 0.5f))
                    {
                        SfxPlayer.TryPlayAt(SoundId.PanelDenied, transform.position, 0.7f);
                    }

                    break;
                case PanelAction.Restored:
                    restored = true;
                    SetLeds(true);
                    SfxPlayer.TryPlayAt(SoundId.PowerRestore, transform.position, 1f);
                    break;
            }
        }

        /// <summary>Amber and unpatched again: the state at the start of a run.</summary>
        public void ResetForNewRun()
        {
            restored = false;
            denied.Reset();
            SetLeds(false);
        }

        private void SetLeds(bool live)
        {
            if (leds == null)
            {
                return;
            }

            Color color = live ? liveColor : deadColor;
            if (beacon != null)
            {
                beacon.color = color;
            }

            block ??= new MaterialPropertyBlock();
            foreach (Renderer led in leds)
            {
                if (led == null)
                {
                    continue;
                }

                // A property block, never the shared material: M_LedAmber is one asset every rack
                // in the building draws with, and assigning to it would dirty it on disk.
                led.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(EmissionColorId, color * 2.5f);
                led.SetPropertyBlock(block);
            }
        }
    }
}
