using UnityEngine;
using Vent.Core.Events;
using Vent.Core.Interaction;
using Vent.Core.Utility;
using Vent.Player.Input;

namespace Vent.Player.Interaction
{
    /// <summary>
    /// Looks along the camera for something that implements <see cref="IInteractable"/> within
    /// arm's reach, publishes a prompt for the HUD, and forwards the Interact key to it. A
    /// raycast rather than a trigger volume: the project has no trigger colliders, and "what am
    /// I looking at" is the question a first-person prompt should answer.
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InputReader input;
        [SerializeField] private UnityEngine.Camera viewCamera;

        [Header("Events out")]
        [SerializeField, Tooltip("The HUD prompt; an empty string hides it.")]
        private StringEventChannel prompt;

        [Header("Tuning")]
        [SerializeField, Min(0.5f), Tooltip("How far the player can reach, metres from the eye.")]
        private float reach = 2.5f;

        private PlayerCharacter character;
        private IInteractable current;
        private string lastPrompt = string.Empty;
        private int mask;

        /// <summary>What the player is looking at right now, or null.</summary>
        public IInteractable Current => current;

        /// <summary>Editor-time wiring used by the prefab factory.</summary>
        public void Configure(InputReader reader, UnityEngine.Camera cam, StringEventChannel promptChannel)
        {
            input = reader;
            viewCamera = cam;
            prompt = promptChannel;
        }

        private void Awake()
        {
            character = GetComponent<PlayerCharacter>();
            mask = Layers.InteractMask;
        }

        private void OnEnable()
        {
            if (input != null)
            {
                input.InteractPressed += OnInteractPressed;
            }
        }

        private void OnDisable()
        {
            if (input != null)
            {
                input.InteractPressed -= OnInteractPressed;
            }

            current = null;
            Publish(string.Empty);
        }

        private void Update()
        {
            current = Find();
            string text = string.Empty;
            if (current != null)
            {
                string hint = current.Prompt;
                if (!string.IsNullOrEmpty(hint))
                {
                    text = current.IsAvailable ? $"[{Key}]  {hint}" : hint;
                }
            }

            Publish(text);
        }

        /// <summary>Act on whatever is in reach. Public so tests can interact without the Input System.</summary>
        public bool TryInteract()
        {
            if (current == null || (character != null && !character.IsAlive))
            {
                return false;
            }

            current.Interact();
            return true;
        }

        private string Key => input != null && input.UsingGamepad ? "B" : "E";

        private IInteractable Find()
        {
            if (viewCamera == null)
            {
                return null;
            }

            Transform eye = viewCamera.transform;
            if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, reach, mask, QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            return hit.collider.GetComponentInParent<IInteractable>();
        }

        private void OnInteractPressed() => TryInteract();

        private void Publish(string text)
        {
            if (text == lastPrompt)
            {
                return;
            }

            lastPrompt = text;
            prompt?.Raise(text);
        }
    }
}
