namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// Whoever can get into a car — the player, via the gameplay assembly's driver component.
    /// The seat asks this through <see cref="Vent.Core.Services.GameServices"/> so the vehicles
    /// assembly never references the player.
    /// </summary>
    public interface IVehicleOccupant
    {
        bool IsDriving { get; }

        /// <summary>Take the driver's seat. Returns false if already driving, dead, or the seat is taken.</summary>
        bool TryEnter(VehicleSeat seat);

        /// <summary>Get out of the current car (no-op when on foot).</summary>
        void Exit();
    }
}
