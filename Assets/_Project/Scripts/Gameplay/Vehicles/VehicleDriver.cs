using UnityEngine;
using UnityEngine.AI;
using Vent.Core.Events;
using Vent.Core.Services;
using Vent.Player;
using Vent.Player.Input;
using Vent.Player.Interaction;
using Vent.Vehicles.Runtime;
using Vent.Weapons;
using Vent.Weapons.Runtime;

namespace Vent.Gameplay.Vehicles
{
    /// <summary>
    /// The hand-off between the player on foot and a car. The only class that sees both
    /// <see cref="PlayerCharacter"/> and <see cref="VehicleController"/>: getting in parents the
    /// (renderer-less) player root to the seat so zombies keep chasing a moving target, hands the
    /// camera to the chase rig, swaps the guns for the pistol out of the window and feeds the car
    /// the controls each frame. Getting out reverses all of it and puts the player on the nearest
    /// bit of NavMesh beside the car. Registered as the scene's <see cref="IVehicleOccupant"/>, which
    /// is how a seat finds it without the vehicles assembly knowing what a player is.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleDriver : MonoBehaviour, IVehicleOccupant, IRecoilReceiver
    {
        [Header("Scene")]
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private VehicleChaseCamera chase;

        [Header("Input")]
        [SerializeField] private InputReader input;

        [Header("Events")]
        [SerializeField, Tooltip("km/h while driving; -1 hides the speedometer.")]
        private FloatEventChannel vehicleSpeed;
        [SerializeField] private StringEventChannel prompt;
        [SerializeField] private VoidEventChannel playerDied;
        [SerializeField] private LevelEventChannel levelChanged;

        [Header("Feel")]
        [SerializeField, Min(0f), Tooltip("Extra field of view at top speed.")]
        private float fovBoostDegrees = 10f;
        [SerializeField, Range(0f, 1f), Tooltip("How much of the pistol's recoil reaches the chase camera.")]
        private float recoilToCamera = 0.4f;
        [SerializeField, Min(1f)] private float speedPublishHz = 15f;
        [SerializeField] private string exitPrompt = "EXIT CAR";

        private VehicleSeat seat;
        private VehicleController vehicle;
        private VehicleRoadkill roadkill;
        private Weapon pistol;
        private PlayerInteractor interactor;
        private float nextPublish;
        private float previousDamageScale = 1f;

        public bool IsDriving => seat != null;
        public VehicleController Vehicle => vehicle;

        public void Configure(PlayerCharacter character, InputReader reader, VehicleChaseCamera rig, FloatEventChannel speed,
            StringEventChannel promptChannel, VoidEventChannel died, LevelEventChannel level)
        {
            player = character;
            input = reader;
            chase = rig;
            vehicleSpeed = speed;
            prompt = promptChannel;
            playerDied = died;
            levelChanged = level;
        }

        private void OnEnable()
        {
            GameServices.Register<IVehicleOccupant>(this);
            playerDied?.Subscribe(OnPlayerDied);
            levelChanged?.Subscribe(OnLevelChanged);
        }

        private void OnDisable()
        {
            if (IsDriving)
            {
                Exit();
            }

            playerDied?.Unsubscribe(OnPlayerDied);
            levelChanged?.Unsubscribe(OnLevelChanged);
            GameServices.Unregister<IVehicleOccupant>(this);
        }

        // ------------------------------------------------------------------ enter / exit

        public bool TryEnter(VehicleSeat target)
        {
            if (IsDriving || target == null || player == null || !player.IsAlive || target.Controller.IsOccupied || chase == null)
            {
                return false;
            }

            seat = target;
            vehicle = seat.Controller;
            roadkill = vehicle.GetComponent<VehicleRoadkill>();
            interactor = player.GetComponent<PlayerInteractor>();

            vehicle.SetOccupied(true);
            player.SetSeated(true, this);

            // The root has no renderers; parenting it to the seat keeps IPlayerTarget.Position on the move.
            player.transform.SetParent(seat.Anchor, false);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.identity;

            player.Inventory?.EnterVehicleMode(seat.MuzzleOut, seat.PortOut);
            pistol = player.Inventory != null ? player.Inventory.Current : null;
            if (seat.Arm != null)
            {
                seat.Arm.gameObject.SetActive(true);
            }

            chase.Attach(seat.CameraTarget, vehicle.transform, player.ViewCamera.transform);
            Physics.SyncTransforms();

            player.Look.SeatedLook += chase.AddOrbit;
            if (interactor != null)
            {
                interactor.enabled = false; // the seat is the only interactable now, and Interact means "out"
            }

            if (input != null)
            {
                input.InteractPressed += OnInteractPressed;
            }

            if (roadkill != null)
            {
                roadkill.Hit += OnRoadkill;
            }

            vehicle.Impact += OnImpact;
            if (pistol != null)
            {
                pistol.Fired += OnFired;
            }

            previousDamageScale = player.Health.DamageScale;
            player.Health.DamageScale = vehicle.Definition != null ? vehicle.Definition.OccupantDamageFactor : 1f;
            prompt?.Raise($"[{Key}]  {exitPrompt}");
            vehicleSpeed?.Raise(0f);
            return true;
        }

        public void Exit()
        {
            if (!IsDriving)
            {
                return;
            }

            vehicle.SetInput(default);
            Vector3 exitPosition = FindExit();
            float yaw = chase.Yaw;

            // Order matters: the camera goes home before PlayerLook may rotate it again, the root is
            // unparented before Teleport writes a world pose, and the controller is re-enabled by
            // SetSeated before Teleport toggles it.
            chase.Detach();
            if (pistol != null)
            {
                pistol.Fired -= OnFired;
            }

            player.Inventory?.ExitVehicleMode();
            if (seat.Arm != null)
            {
                seat.Arm.gameObject.SetActive(false);
            }

            player.transform.SetParent(null, true);
            player.SetSeated(false, null);
            player.Controller.Teleport(exitPosition, Quaternion.Euler(0f, yaw, 0f));
            player.Look.SetRotation(yaw, 0f);
            Physics.SyncTransforms();

            player.Look.SeatedLook -= chase.AddOrbit;
            if (input != null)
            {
                input.InteractPressed -= OnInteractPressed;
            }

            if (roadkill != null)
            {
                roadkill.Hit -= OnRoadkill;
            }

            vehicle.Impact -= OnImpact;
            vehicle.SetOccupied(false);

            player.Health.DamageScale = previousDamageScale;
            player.SetSeatedMotion(0f);
            if (player.Motion != null)
            {
                player.Motion.FovBoost = 0f;
            }

            if (interactor != null)
            {
                interactor.enabled = true;
            }

            prompt?.Raise(string.Empty);
            vehicleSpeed?.Raise(-1f);
            if (!player.IsAlive)
            {
                player.SetControlsEnabled(false);
            }

            seat = null;
            vehicle = null;
            roadkill = null;
            pistol = null;
        }

        /// <summary>The first spot beside the car that is on the NavMesh: left door, right door, behind, the seat itself, then home.</summary>
        private Vector3 FindExit()
        {
            Vector3[] candidates =
            {
                seat.ExitLeft != null ? seat.ExitLeft.position : vehicle.transform.position - vehicle.transform.right * 1.6f,
                seat.ExitRight != null ? seat.ExitRight.position : vehicle.transform.position + vehicle.transform.right * 1.6f,
                vehicle.transform.position - vehicle.transform.forward * 4f,
                seat.Anchor != null ? seat.Anchor.position : vehicle.transform.position,
            };

            foreach (Vector3 candidate in candidates)
            {
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }

            return NavMesh.SamplePosition(vehicle.HomePosition, out NavMeshHit home, 3f, NavMesh.AllAreas) ? home.position : candidates[3];
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (!IsDriving)
            {
                return;
            }

            if (input != null)
            {
                Vector2 move = input.Move;
                vehicle.SetInput(new VehicleInput(move.y, move.x, input.JumpHeld));
            }

            float speed01 = vehicle.Speed01;
            chase.SetHeadingSpeed(Mathf.Abs(vehicle.ForwardSpeed));
            player.SetSeatedMotion(speed01);
            if (player.Motion != null)
            {
                player.Motion.FovBoost = fovBoostDegrees * speed01;
            }

            if (seat.Arm != null)
            {
                Ray aim = player.AimRay;
                float distance = Physics.Raycast(aim, out RaycastHit hit, 80f, player.ShootMask, QueryTriggerInteraction.Ignore) ? hit.distance : 40f;
                seat.Arm.SetAimPoint(aim.GetPoint(distance));
            }

            if (Time.unscaledTime >= nextPublish)
            {
                nextPublish = Time.unscaledTime + 1f / speedPublishHz;
                vehicleSpeed?.Raise(Mathf.Abs(vehicle.ForwardSpeed) * 3.6f);
            }
        }

        private string Key => input != null && input.UsingGamepad ? "B" : "E";

        // ------------------------------------------------------------------ feedback

        /// <inheritdoc/>
        public void AddRecoil(Vector2 pitchYawDegrees) => chase?.AddKick(pitchYawDegrees * recoilToCamera);

        private void OnFired(float ramp) => seat?.Arm?.Kick(ramp);

        private void OnRoadkill(RoadkillInfo info) => player.Motion?.Shake(info.Lethal ? 0.06f : 0.03f);

        private void OnImpact(float speed) => player.Motion?.Shake(Mathf.Clamp(speed / 15f, 0.02f, 0.08f));

        private void OnInteractPressed() => Exit();

        private void OnPlayerDied()
        {
            if (IsDriving)
            {
                Exit();
            }
        }

        /// <summary>A new run starts at level 1: nobody starts a run in a car.</summary>
        private void OnLevelChanged(LevelInfo info)
        {
            if (info.Level <= 1 && IsDriving)
            {
                Exit();
            }
        }
    }
}
