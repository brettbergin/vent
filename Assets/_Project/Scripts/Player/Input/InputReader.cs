using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Vent.Player.Input
{
    /// <summary>
    /// The single bridge between the Input System and gameplay code.
    ///
    /// It is a ScriptableObject so the player, the game manager and tests can all share one
    /// asset without a scene dependency. Continuous values (move, look) are polled via
    /// properties; discrete presses are exposed as C# events. Callers never see
    /// <see cref="InputAction"/> types, so rebinding the underlying asset never ripples out.
    ///
    /// Action lookup is by name rather than a generated wrapper class: it keeps the repository
    /// free of generated code and fails loudly at startup if an action is renamed.
    /// </summary>
    [CreateAssetMenu(menuName = "Vent/Input/Input Reader", fileName = "InputReader")]
    public sealed class InputReader : ScriptableObject
    {
        [SerializeField] private InputActionAsset actions;

        // --- Gameplay (polled) ---
        public Vector2 Move { get; private set; }
        /// <summary>Mouse delta this frame, in pixels. Already frame-relative; do not multiply by deltaTime.</summary>
        public Vector2 LookDelta { get; private set; }
        /// <summary>Right-stick axis in -1..1. Multiply by deltaTime and a rate to get degrees.</summary>
        public Vector2 LookAnalog { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool FireHeld { get; private set; }
        public bool AimHeld { get; private set; }
        /// <summary>Jump held down; on foot it is a press, in a car it is the handbrake.</summary>
        public bool JumpHeld { get; private set; }

        // --- Gameplay (events) ---
        public event Action JumpPressed;
        public event Action FirePressed;
        public event Action FireReleased;
        public event Action ReloadPressed;
        /// <summary>Direct slot selection, zero-based.</summary>
        public event Action<int> WeaponSlotSelected;
        /// <summary>+1 = next, -1 = previous (mouse wheel).</summary>
        public event Action<int> WeaponCycled;
        public event Action WeaponSwapPressed;
        public event Action PausePressed;
        /// <summary>Use what you are looking at: open the door, get in or out of a car.</summary>
        public event Action InteractPressed;

        // --- UI (events) ---
        public event Action UnpausePressed;

        private InputActionMap gameplay;
        private InputActionMap ui;

        private InputAction move, look, lookAnalog, jump, sprint, fire, aim, reload, weapon1, weapon2, cycle, swap, pause, interact, unpause;

        private const float ScrollNotch = 1f;
        private const float ScrollCooldown = 0.15f;
        private float scrollAccumulator;
        private float nextScrollTime;

        /// <summary>True if the last input that changed a gameplay value came from a gamepad; drives look scaling & UI hints.</summary>
        public bool UsingGamepad { get; private set; }

        public InputActionAsset Actions => actions;

        private void OnEnable()
        {
            if (actions == null)
            {
                return;
            }

            gameplay = actions.FindActionMap("Gameplay", throwIfNotFound: true);
            ui = actions.FindActionMap("UI", throwIfNotFound: true);

            move = gameplay.FindAction("Move", true);
            look = gameplay.FindAction("Look", true);
            lookAnalog = gameplay.FindAction("LookAnalog", true);
            jump = gameplay.FindAction("Jump", true);
            sprint = gameplay.FindAction("Sprint", true);
            fire = gameplay.FindAction("Fire", true);
            aim = gameplay.FindAction("Aim", true);
            reload = gameplay.FindAction("Reload", true);
            weapon1 = gameplay.FindAction("Weapon1", true);
            weapon2 = gameplay.FindAction("Weapon2", true);
            cycle = gameplay.FindAction("CycleWeapon", true);
            swap = gameplay.FindAction("SwapWeapon", true);
            pause = gameplay.FindAction("Pause", true);
            interact = gameplay.FindAction("Interact", true);
            unpause = ui.FindAction("Unpause", true);

            move.performed += OnMove;
            move.canceled += OnMove;
            look.performed += OnLook;
            look.canceled += OnLook;
            lookAnalog.performed += OnLookAnalog;
            lookAnalog.canceled += OnLookAnalog;
            jump.performed += OnJump;
            jump.canceled += OnJumpReleased;
            sprint.performed += OnSprint;
            sprint.canceled += OnSprint;
            fire.performed += OnFire;
            fire.canceled += OnFire;
            aim.performed += OnAim;
            aim.canceled += OnAim;
            reload.performed += OnReload;
            weapon1.performed += OnWeapon1;
            weapon2.performed += OnWeapon2;
            cycle.performed += OnCycle;
            swap.performed += OnSwap;
            pause.performed += OnPause;
            interact.performed += OnInteract;
            unpause.performed += OnUnpause;
        }

        private void OnDisable()
        {
            if (gameplay == null)
            {
                return;
            }

            move.performed -= OnMove;
            move.canceled -= OnMove;
            look.performed -= OnLook;
            look.canceled -= OnLook;
            lookAnalog.performed -= OnLookAnalog;
            lookAnalog.canceled -= OnLookAnalog;
            jump.performed -= OnJump;
            jump.canceled -= OnJumpReleased;
            sprint.performed -= OnSprint;
            sprint.canceled -= OnSprint;
            fire.performed -= OnFire;
            fire.canceled -= OnFire;
            aim.performed -= OnAim;
            aim.canceled -= OnAim;
            reload.performed -= OnReload;
            weapon1.performed -= OnWeapon1;
            weapon2.performed -= OnWeapon2;
            cycle.performed -= OnCycle;
            swap.performed -= OnSwap;
            pause.performed -= OnPause;
            interact.performed -= OnInteract;
            unpause.performed -= OnUnpause;

            DisableAll();
            gameplay = null;
            ui = null;
        }

        /// <summary>Gameplay map on, UI map off. Cursor is handled by the game manager, not here.</summary>
        public void EnableGameplay()
        {
            ui?.Disable();
            gameplay?.Enable();
        }

        /// <summary>UI map on, gameplay map off. Clears held state so nothing "sticks" when a menu opens mid-fire.</summary>
        public void EnableUI()
        {
            gameplay?.Disable();
            ui?.Enable();
            ClearHeldState();
        }

        public void DisableAll()
        {
            gameplay?.Disable();
            ui?.Disable();
            ClearHeldState();
        }

        private void ClearHeldState()
        {
            scrollAccumulator = 0f;
            Move = Vector2.zero;
            LookDelta = Vector2.zero;
            LookAnalog = Vector2.zero;
            SprintHeld = false;
            AimHeld = false;
            JumpHeld = false;
            if (FireHeld)
            {
                FireHeld = false;
                FireReleased?.Invoke();
            }
        }

        private void TrackDevice(InputAction.CallbackContext ctx)
        {
            UsingGamepad = ctx.control?.device is Gamepad;
        }

        private void OnMove(InputAction.CallbackContext ctx)
        {
            TrackDevice(ctx);
            Move = ctx.ReadValue<Vector2>();
        }

        private void OnLook(InputAction.CallbackContext ctx) => LookDelta = ctx.ReadValue<Vector2>();

        private void OnLookAnalog(InputAction.CallbackContext ctx)
        {
            TrackDevice(ctx);
            LookAnalog = ctx.ReadValue<Vector2>();
        }

        private void OnJump(InputAction.CallbackContext ctx)
        {
            JumpHeld = true;
            JumpPressed?.Invoke();
        }

        private void OnJumpReleased(InputAction.CallbackContext ctx) => JumpHeld = false;
        private void OnSprint(InputAction.CallbackContext ctx) => SprintHeld = ctx.ReadValueAsButton();

        private void OnFire(InputAction.CallbackContext ctx)
        {
            bool held = ctx.ReadValueAsButton();
            if (held == FireHeld)
            {
                return;
            }

            FireHeld = held;
            if (held)
            {
                FirePressed?.Invoke();
            }
            else
            {
                FireReleased?.Invoke();
            }
        }

        private void OnAim(InputAction.CallbackContext ctx) => AimHeld = ctx.ReadValueAsButton();
        private void OnReload(InputAction.CallbackContext ctx) => ReloadPressed?.Invoke();
        private void OnWeapon1(InputAction.CallbackContext ctx) => WeaponSlotSelected?.Invoke(0);
        private void OnWeapon2(InputAction.CallbackContext ctx) => WeaponSlotSelected?.Invoke(1);

        /// <summary>
        /// Mouse wheels report ±1 per notch, trackpads a stream of fractional deltas. Accumulate to a
        /// notch and rate-limit so a flick never flips weapons several times.
        /// </summary>
        private void OnCycle(InputAction.CallbackContext ctx)
        {
            scrollAccumulator += ctx.ReadValue<float>();
            if (Mathf.Abs(scrollAccumulator) < ScrollNotch || Time.unscaledTime < nextScrollTime)
            {
                return;
            }

            int direction = scrollAccumulator > 0f ? -1 : 1;
            scrollAccumulator = 0f;
            nextScrollTime = Time.unscaledTime + ScrollCooldown;
            WeaponCycled?.Invoke(direction);
        }

        private void OnSwap(InputAction.CallbackContext ctx) => WeaponSwapPressed?.Invoke();
        private void OnPause(InputAction.CallbackContext ctx) => PausePressed?.Invoke();
        private void OnInteract(InputAction.CallbackContext ctx) => InteractPressed?.Invoke();
        private void OnUnpause(InputAction.CallbackContext ctx) => UnpausePressed?.Invoke();

        /// <summary>
        /// Mouse deltas are "consumed" per frame: after the player has read them, zero them so a
        /// frame without mouse events does not reuse the previous delta.
        /// </summary>
        public void ConsumeLookDelta() => LookDelta = Vector2.zero;
    }
}
