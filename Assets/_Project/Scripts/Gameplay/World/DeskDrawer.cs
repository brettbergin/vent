using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Interaction;
using Vent.Core.Utility;

namespace Vent.Gameplay.World
{
    /// <summary>
    /// The sliding bottom drawer of one office desk, and the owner of that desk's monitor.
    ///
    /// Every desk has one and every drawer opens whenever the player asks — pulling a drawer out
    /// is a small pleasure and there is no reason to refuse it. What decides the hunt is that the
    /// key only *exists* once the servers are patched, and only in the desk
    /// <see cref="KeyHuntDirector"/> rolled for this run, so opening all eighteen drawers early
    /// finds nothing but stationery.
    ///
    /// The component sits on the drawer, never on the desk: <c>PlayerInteractor</c> walks up from
    /// the collider it hit, so a look at the desktop, a leg or the monitor finds no interactable
    /// and only the drawer front prompts.
    /// </summary>
    public sealed class DeskDrawer : MonoBehaviour, IInteractable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private KeyHuntDirector hunt;
        [SerializeField, Tooltip("This desk's monitor screen. Dark all run except on the desk that hides the key.")]
        private Renderer screen;
        [SerializeField, Tooltip("A small point light at the screen, enabled only when it comes back up.")]
        private Light screenGlow;
        [SerializeField] private GameObject keyVisual;

        [Header("Tuning")]
        [SerializeField, Min(0.05f), Tooltip("How far the drawer pulls out, metres.")]
        private float slide = 0.3f;
        [SerializeField, Min(0.1f)] private float openSharpness = 8f;
        [SerializeField, Min(0f), Tooltip("Seconds the key stays visible in the open drawer before the player pockets it.")]
        private float keyDwell = 0.8f;

        [Header("Screen")]
        [SerializeField] private Color darkColor = new(0.02f, 0.025f, 0.03f);
        [SerializeField] private Color liveColor = new(0.05f, 0.12f, 0.20f);
        [SerializeField] private Color liveEmission = new(0.4f, 0.7f, 1f);

        private MaterialPropertyBlock block;
        private Vector3 closedLocal;
        private bool isOpen;
        private float openT;
        private float pocketKeyAt = -1f;

        /// <summary>0 shut .. 1 fully out, as animated.</summary>
        public float OpenAmount => openT;

        public bool IsOpen => isOpen;

        /// <summary>True while this desk's monitor is the one that came back up.</summary>
        public bool IsScreenLit { get; private set; }

        /// <summary>Editor-time wiring used by the building generator.</summary>
        public void Configure(KeyHuntDirector director, Renderer deskScreen, Light glow, GameObject key)
        {
            hunt = director;
            screen = deskScreen;
            screenGlow = glow;
            keyVisual = key;
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            closedLocal = transform.localPosition;
            ApplyShut();
        }

        // ----- IInteractable -----
        public string Prompt => isOpen ? string.Empty : "OPEN THE DRAWER";
        public bool IsAvailable => !isOpen;

        public void Interact()
        {
            if (isOpen || hunt == null)
            {
                return;
            }

            isOpen = true;
            SfxPlayer.TryPlayAt(SoundId.DrawerOpen, transform.position, 0.7f);

            if (hunt.TryOpenDrawer(this) != DrawerAction.KeyTaken)
            {
                return;
            }

            if (keyVisual != null)
            {
                keyVisual.SetActive(true);
                pocketKeyAt = Time.time + keyDwell;
            }

            SfxPlayer.TryPlayAt(SoundId.KeyPickup, transform.position, 0.9f);
        }

        private void Update()
        {
            openT = MathUtil.Damp(openT, isOpen ? 1f : 0f, openSharpness, Time.deltaTime);
            transform.localPosition = closedLocal + Vector3.forward * (slide * openT);

            if (pocketKeyAt >= 0f && Time.time >= pocketKeyAt)
            {
                pocketKeyAt = -1f;
                if (keyVisual != null)
                {
                    keyVisual.SetActive(false);
                }
            }
        }

        /// <summary>Shut, empty and dark: the state at the start of a run.</summary>
        public void ResetForNewRun()
        {
            isOpen = false;
            openT = 0f;
            ApplyShut();
            SetScreenLit(false);
        }

        /// <summary>
        /// Light this desk's monitor, or put it out. Written through a
        /// <see cref="MaterialPropertyBlock"/>: M_Screen is one shared asset that every monitor in
        /// the building draws with, so per-desk state cannot live on the material.
        /// </summary>
        public void SetScreenLit(bool lit)
        {
            IsScreenLit = lit;
            if (screenGlow != null)
            {
                screenGlow.enabled = lit;
            }

            if (screen == null)
            {
                return;
            }

            block ??= new MaterialPropertyBlock();
            screen.GetPropertyBlock(block);
            block.SetColor(BaseColorId, lit ? liveColor : darkColor);
            block.SetColor(EmissionColorId, lit ? liveEmission * 3f : Color.black);
            screen.SetPropertyBlock(block);
        }

        private void ApplyShut()
        {
            transform.localPosition = closedLocal;
            pocketKeyAt = -1f;
            if (keyVisual != null)
            {
                keyVisual.SetActive(false);
            }
        }
    }
}
