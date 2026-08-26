namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// What the driver wants this frame. A plain struct so the vehicles assembly never sees the
    /// Input System: the gameplay side reads the player's controls and hands the car these numbers.
    /// </summary>
    public readonly struct VehicleInput
    {
        /// <summary>-1 (reverse / brake) .. 1 (accelerate).</summary>
        public readonly float Throttle;

        /// <summary>-1 (left) .. 1 (right).</summary>
        public readonly float Steer;

        public readonly bool Handbrake;

        public VehicleInput(float throttle, float steer, bool handbrake)
        {
            Throttle = throttle < -1f ? -1f : throttle > 1f ? 1f : throttle;
            Steer = steer < -1f ? -1f : steer > 1f ? 1f : steer;
            Handbrake = handbrake;
        }
    }
}
