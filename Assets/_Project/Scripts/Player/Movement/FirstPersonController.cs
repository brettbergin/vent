using UnityEngine;
using Vent.Core.Utility;
using Vent.Player.Input;

namespace Vent.Player.Movement
{
    /// <summary>
    /// Kinematic first-person locomotion on a <see cref="CharacterController"/>.
    ///
    /// Design notes:
    ///  - Runs in <c>Update</c> (not FixedUpdate). Character controllers are not rigidbodies;
    ///    per-frame movement gives the lowest input latency, which matters more in an FPS than
    ///    physics determinism.
    ///  - Horizontal velocity is smoothed with exponential damping so acceleration is
    ///    frame-rate independent.
    ///  - Jumping has a small coyote window (jump allowed briefly after leaving a ledge) and an
    ///    input buffer (jump pressed slightly before landing still fires). Both are standard
    ///    game-feel affordances.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputReader input;

        [Header("Speeds (m/s)")]
        [SerializeField, Min(0f)] private float walkSpeed = 4.5f;
        [SerializeField, Min(0f)] private float sprintSpeed = 7f;
        [SerializeField, Tooltip("Sprinting is only allowed while moving mostly forward.")]
        private float sprintForwardDotThreshold = 0.3f;

        [Header("Responsiveness")]
        [SerializeField, Min(0f), Tooltip("Ground acceleration sharpness (per second).")]
        private float groundSharpness = 14f;
        [SerializeField, Min(0f), Tooltip("Air control sharpness (per second).")]
        private float airSharpness = 3f;

        [Header("Jump & Gravity")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.1f;
        [SerializeField] private float gravity = -22f;
        [SerializeField, Min(0f)] private float coyoteTime = 0.12f;
        [SerializeField, Min(0f)] private float jumpBufferTime = 0.12f;
        [SerializeField, Tooltip("Small downward force while grounded keeps the controller snapped to slopes and stairs.")]
        private float groundedStickVelocity = -2f;

        private CharacterController controller;
        private Vector3 horizontalVelocity;
        private float verticalVelocity;
        private float lastGroundedTime = float.NegativeInfinity;
        private float jumpRequestedTime = float.NegativeInfinity;
        private bool movementEnabled = true;

        /// <summary>World-space velocity from the last move, including vertical.</summary>
        public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;

        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }

        /// <summary>Planar speed normalised to sprint speed (0..1). Drives head-bob and weapon spread.</summary>
        public float Speed01 => sprintSpeed > 0f ? Mathf.Clamp01(new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude / sprintSpeed) : 0f;

        public InputReader Input
        {
            get => input;
            set => input = value;
        }

        private void Awake() => controller = GetComponent<CharacterController>();

        private void OnEnable()
        {
            if (input != null)
            {
                input.JumpPressed += OnJumpPressed;
            }
        }

        private void OnDisable()
        {
            if (input != null)
            {
                input.JumpPressed -= OnJumpPressed;
            }
        }

        /// <summary>Freeze locomotion (death, menus) without disabling the component so gravity still settles.</summary>
        public void SetMovementEnabled(bool enabled)
        {
            movementEnabled = enabled;
            if (!enabled)
            {
                horizontalVelocity = Vector3.zero;
            }
        }

        /// <summary>Teleport safely: CharacterController ignores transform writes while enabled.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
        }

        private void OnJumpPressed() => jumpRequestedTime = Time.time;

        private void Update()
        {
            float dt = Time.deltaTime;
            float now = Time.time;

            IsGrounded = controller.isGrounded;
            if (IsGrounded)
            {
                lastGroundedTime = now;
            }

            // --- Horizontal ---
            Vector2 moveInput = (movementEnabled && input != null) ? Vector2.ClampMagnitude(input.Move, 1f) : Vector2.zero;
            Vector3 wishDir = transform.right * moveInput.x + transform.forward * moveInput.y;

            bool wantsSprint = movementEnabled && input != null && input.SprintHeld && moveInput.y > sprintForwardDotThreshold;
            IsSprinting = wantsSprint && IsGrounded;
            float targetSpeed = IsSprinting ? sprintSpeed : walkSpeed;

            Vector3 targetVelocity = wishDir * targetSpeed;
            float sharpness = IsGrounded ? groundSharpness : airSharpness;
            horizontalVelocity = MathUtil.Damp(horizontalVelocity, targetVelocity, sharpness, dt);

            // --- Vertical ---
            bool canCoyoteJump = now - lastGroundedTime <= coyoteTime;
            bool jumpBuffered = now - jumpRequestedTime <= jumpBufferTime;
            if (movementEnabled && jumpBuffered && canCoyoteJump && verticalVelocity <= 0.01f)
            {
                // v = sqrt(2gh): the launch velocity that reaches jumpHeight under this gravity.
                verticalVelocity = Mathf.Sqrt(2f * -gravity * jumpHeight);
                jumpRequestedTime = float.NegativeInfinity;
                lastGroundedTime = float.NegativeInfinity;
            }
            else if (IsGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = groundedStickVelocity;
            }
            else
            {
                verticalVelocity += gravity * dt;
            }

            controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
        }
    }
}
