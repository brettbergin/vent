using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Damage;
using Vent.Core.Events;
using Vent.Core.Perks;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Player.Camera;
using Vent.Player.Health;
using Vent.Player.Input;
using Vent.Player.Movement;
using Vent.Weapons;
using Vent.Weapons.Runtime;

namespace Vent.Player
{
    /// <summary>
    /// The player prefab's root component and composition point.
    ///
    /// It wires input events to the systems that consume them, registers the enemy-facing
    /// <see cref="IPlayerTarget"/> service, and implements <see cref="IWeaponHolder"/> so the
    /// weapons assembly can ask "where am I aiming, how fast am I moving" without knowing what
    /// a player is.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    [RequireComponent(typeof(PlayerLook))]
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerCharacter : MonoBehaviour, IPlayerTarget, IWeaponHolder
    {
        [Header("References")]
        [SerializeField] private InputReader input;
        [SerializeField, Tooltip("Raised on the map key; the HUD decides whether there is a map to show.")]
        private VoidEventChannel mapToggled;
        [SerializeField] private UnityEngine.Camera viewCamera;
        [SerializeField, Tooltip("The overlay camera that draws the first-person guns; off while driving (the pistol is out of the window instead).")]
        private UnityEngine.Camera weaponCamera;
        [SerializeField] private WeaponInventory inventory;
        [SerializeField] private CameraMotion cameraMotion;
        [SerializeField, Tooltip("Height above the feet that enemies aim at.")]
        private float aimPointHeight = 1.35f;

        [Header("Events")]
        [SerializeField, Tooltip("Level transitions refill ammo and heal; the director never touches the player directly.")]
        private LevelEventChannel levelChanged;
        [SerializeField, Range(0f, 1f)] private float healFractionOnLevelUp = 0.5f;
        [SerializeField, Tooltip("Perk pickups. The player applies the ones that concern it (ammo, invulnerability, one-shot).")]
        private PerkEventChannel perkCollected;

        private FirstPersonController controller;
        private PlayerLook look;
        private PlayerHealth health;
        private Vector2 lookDeltaThisFrame;
        private Vector3 lastPosition;
        private bool controlsEnabled = true;
        private float seatedMotion;

        public FirstPersonController Controller => controller;
        public PlayerLook Look => look;
        public PlayerHealth Health => health;
        public WeaponInventory Inventory => inventory;
        public UnityEngine.Camera ViewCamera => viewCamera;
        public CameraMotion Motion => cameraMotion;
        /// <summary>The camera's rest parent (pitch pivot); the chase rig returns the camera here.</summary>
        public Transform CameraPivot => look != null ? look.PitchPivot : null;

        /// <summary>True while sitting in a car: locomotion, containment and look are handed to the driver.</summary>
        public bool IsSeated { get; private set; }

        /// <summary>Editor-time wiring used by the prefab factory.</summary>
        public void Configure(InputReader reader, UnityEngine.Camera cam, WeaponInventory weapons, CameraMotion motion, LevelEventChannel levelEvent, PerkEventChannel perkEvent,
            UnityEngine.Camera overlayCamera = null)
        {
            perkCollected = perkEvent;
            input = reader;
            viewCamera = cam;
            weaponCamera = overlayCamera;
            inventory = weapons;
            cameraMotion = motion;
            levelChanged = levelEvent;
        }

        /// <summary>
        /// Sit down in (or stand up from) a car. Keeps this component enabled and registered so zombies
        /// still have a target; only the parts that assume feet on a floor are switched off.
        /// </summary>
        public void SetSeated(bool seated, IRecoilReceiver seatedRecoil)
        {
            IsSeated = seated;
            controller.SetSeated(seated);
            look.SeatedRecoil = seatedRecoil;
            look.SetSeated(seated);
            inventory?.SetSlotLock(seated);
            if (weaponCamera != null)
            {
                weaponCamera.enabled = !seated;
            }

            if (!seated)
            {
                seatedMotion = 0f;
            }
        }

        /// <summary>How fast the car is going (0..1): drives weapon spread while seated the way running does on foot.</summary>
        public void SetSeatedMotion(float factor01) => seatedMotion = Mathf.Clamp01(factor01);

        // ----- IPlayerTarget -----
        Transform IPlayerTarget.Transform => transform;
        public Vector3 Position => transform.position;
        public Vector3 AimPoint => transform.position + Vector3.up * aimPointHeight;
        public bool IsAlive => health != null && health.IsAlive;
        public IDamageable Damageable => health;

        // ----- IWeaponHolder -----
        public Ray AimRay => viewCamera != null
            ? new Ray(viewCamera.transform.position, viewCamera.transform.forward)
            : new Ray(AimPoint, transform.forward);
        public float MovementFactor => IsSeated ? seatedMotion : controller != null ? (controller.IsGrounded ? controller.Speed01 : 1f) : 0f;
        public bool IsAiming => controlsEnabled && input != null && input.AimHeld;
        public bool IsGrounded => IsSeated || controller == null || controller.IsGrounded;
        public Vector2 LookDelta => lookDeltaThisFrame;
        public Vector3 Velocity => controller != null && !IsSeated ? controller.Velocity : Vector3.zero;
        /// <summary>Bullets never hit the car the player is sitting in: the ray starts inside it.</summary>
        public int ShootMask => IsSeated ? Layers.ShootableMask & ~(1 << Layers.VehicleIndex) : Layers.ShootableMask;

        private void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            look = GetComponent<PlayerLook>();
            health = GetComponent<PlayerHealth>();

            if (viewCamera == null)
            {
                viewCamera = GetComponentInChildren<UnityEngine.Camera>();
            }

            if (inventory != null)
            {
                inventory.Initialize(this, look);
            }
        }

        private void OnEnable()
        {
            GameServices.Register<IPlayerTarget>(this);
            GameServices.Register(this);
            levelChanged?.Subscribe(OnLevelChanged);
            perkCollected?.Subscribe(OnPerkCollected);
            if (health != null && health.HealthChanged != null)
            {
                health.HealthChanged.Subscribe(OnHealthChanged);
            }

            if (input != null)
            {
                input.FirePressed += OnFirePressed;
                input.FireReleased += OnFireReleased;
                input.ReloadPressed += OnReloadPressed;
                input.WeaponSlotSelected += OnWeaponSlotSelected;
                input.WeaponCycled += OnWeaponCycled;
                input.WeaponSwapPressed += OnWeaponSwapPressed;
                input.MapPressed += OnMapPressed;
            }

            if (health != null && health.Died != null)
            {
                health.Died.Subscribe(OnDied);
            }
        }

        private void OnDisable()
        {
            GameServices.Unregister<IPlayerTarget>(this);
            GameServices.Unregister(this);
            levelChanged?.Unsubscribe(OnLevelChanged);
            perkCollected?.Unsubscribe(OnPerkCollected);
            if (health != null && health.HealthChanged != null)
            {
                health.HealthChanged.Unsubscribe(OnHealthChanged);
            }

            if (input != null)
            {
                input.FirePressed -= OnFirePressed;
                input.FireReleased -= OnFireReleased;
                input.ReloadPressed -= OnReloadPressed;
                input.WeaponSlotSelected -= OnWeaponSlotSelected;
                input.WeaponCycled -= OnWeaponCycled;
                input.WeaponSwapPressed -= OnWeaponSwapPressed;
                input.MapPressed -= OnMapPressed;
            }

            if (health != null && health.Died != null)
            {
                health.Died.Unsubscribe(OnDied);
            }
        }

        private void Update()
        {
            // Capture look delta for view-model sway before PlayerLook consumes it in LateUpdate.
            lookDeltaThisFrame = input != null ? input.LookDelta + input.LookAnalog * (Time.deltaTime * 60f) : Vector2.zero;

            if (inventory != null)
            {
                inventory.SetAiming(IsAiming);
            }

            if (look != null)
            {
                look.SetAiming(IsAiming);
            }

            cameraMotion?.SetAiming(IsAiming);
        }

        /// <summary>Enable or disable all player control (used by menus, death and level transitions).</summary>
        public void SetControlsEnabled(bool enabled)
        {
            controlsEnabled = enabled;
            controller.SetMovementEnabled(enabled);
            look.SetLookEnabled(enabled);
            if (inventory != null)
            {
                inventory.SetWeaponsActive(enabled);
            }
        }

        /// <summary>Put the player back into a fresh state at the given spawn pose.</summary>
        public void ResetForNewRun(Vector3 position, float yawDegrees)
        {
            if (IsSeated)
            {
                // Scenes reload between runs, so this is a belt-and-braces path; never spawn inside a car.
                transform.SetParent(null, true);
                SetSeated(false, null);
            }

            controller.Teleport(position, Quaternion.Euler(0f, yawDegrees, 0f));
            look.SetRotation(yawDegrees, 0f);
            health.ResetToFull();
            inventory?.ResetForNewRun();
            SetControlsEnabled(true);
        }

        /// <summary>Feedback hook for the damage system: shake the camera in proportion to the hit.</summary>
        public void OnDamageFeedback(float normalizedDamage)
        {
            cameraMotion?.Shake(0.02f + 0.06f * Mathf.Clamp01(normalizedDamage));
        }

        private void OnFirePressed()
        {
            if (controlsEnabled)
            {
                inventory?.PullTrigger();
            }
        }

        private void OnFireReleased() => inventory?.ReleaseTrigger();

        private void OnReloadPressed()
        {
            if (controlsEnabled)
            {
                inventory?.Reload();
            }
        }

        private void OnWeaponSlotSelected(int slot)
        {
            if (controlsEnabled)
            {
                inventory?.SelectSlot(slot);
            }
        }

        private void OnWeaponCycled(int direction)
        {
            if (controlsEnabled)
            {
                inventory?.Cycle(direction);
            }
        }

        private void OnMapPressed()

        {

            if (IsAlive)

            {

                mapToggled?.Raise();

            }

        }


        private void OnWeaponSwapPressed()
        {
            if (controlsEnabled)
            {
                inventory?.Cycle(1);
            }
        }

        private void OnDied()
        {
            SetControlsEnabled(false);
        }

        /// <summary>Level 2+ is a checkpoint: full ammo and a partial heal, so the loop never stalls on dry guns.</summary>
        private void OnLevelChanged(LevelInfo info)
        {
            if (info.Level <= 1)
            {
                return;
            }

            inventory?.RefillAllAmmo();
            health.Heal(health.Max * healFractionOnLevelUp);
        }

        /// <summary>Perks the player owns. Nuke is handled by the scene's PerkSystem, which can see the zombies.</summary>
        private void OnPerkCollected(PerkInfo perk)
        {
            switch (perk.Kind)
            {
                case PerkKind.InstantReload:
                    inventory?.InstantReloadAll();
                    break;
                case PerkKind.Invulnerable:
                    health.GrantInvulnerability(perk.Duration);
                    break;
                case PerkKind.OneShot:
                    inventory?.GrantOneShot(perk.Duration);
                    break;
            }
        }

        private void OnHealthChanged(HealthInfo info)
        {
            if (info.Delta >= 0f)
            {
                return;
            }

            OnDamageFeedback(-info.Delta / info.Max);
            SfxPlayer.TryPlay2D(SoundId.PlayerHurt, 0.8f);
        }
    }
}
