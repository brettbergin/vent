using UnityEngine;
using Vent.Core.Interaction;
using Vent.Core.Services;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// The driver's seat: the transforms the occupant needs (where to sit, where to get out, what
    /// the chase camera follows, where the drive-by pistol pokes out) and the interaction that puts
    /// them there. The occupant itself is found through <see cref="GameServices"/>, so this
    /// assembly never references the player.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleSeat : MonoBehaviour, IInteractable
    {
        [Header("Wiring")]
        [SerializeField, Tooltip("The occupant's root is parented here.")]
        private Transform anchor;
        [SerializeField] private Transform exitLeft;
        [SerializeField] private Transform exitRight;
        [SerializeField, Tooltip("What the chase camera orbits.")]
        private Transform cameraTarget;
        [SerializeField, Tooltip("The arm-and-pistol prop out of the driver's window; active only while occupied.")]
        private VehicleDriveByArm arm;
        [SerializeField, Tooltip("Where the pistol's flash and tracer start while seated.")]
        private Transform muzzleOut;
        [SerializeField, Tooltip("Where the pistol's brass ejects while seated.")]
        private Transform portOut;

        [Header("Prompt")]
        [SerializeField] private string prompt = "ENTER CAR";

        private VehicleController controller;

        public VehicleController Controller => controller != null ? controller : controller = GetComponent<VehicleController>();
        public Transform Anchor => anchor;
        public Transform ExitLeft => exitLeft;
        public Transform ExitRight => exitRight;
        public Transform CameraTarget => cameraTarget;
        public VehicleDriveByArm Arm => arm;
        public Transform MuzzleOut => muzzleOut;
        public Transform PortOut => portOut;

        public void Configure(Transform seatAnchor, Transform left, Transform right, Transform camTarget, VehicleDriveByArm driveByArm, Transform muzzle, Transform port)
        {
            anchor = seatAnchor;
            exitLeft = left;
            exitRight = right;
            cameraTarget = camTarget;
            arm = driveByArm;
            muzzleOut = muzzle;
            portOut = port;
        }

        private void Awake() => controller = GetComponent<VehicleController>();

        // ----- IInteractable -----
        public string Prompt => Controller.IsOccupied ? string.Empty : prompt;
        public bool IsAvailable => !Controller.IsOccupied;

        public void Interact()
        {
            if (Controller.IsOccupied)
            {
                return;
            }

            if (GameServices.TryGet(out IVehicleOccupant occupant))
            {
                occupant.TryEnter(this);
            }
        }
    }
}
