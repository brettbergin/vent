using UnityEngine;

namespace Vent.Vehicles.Runtime
{
    /// <summary>One body hit by the car: where, how fast, and whether it was the end of them.</summary>
    public readonly struct RoadkillInfo
    {
        public readonly Vector3 Point;
        public readonly float Speed;
        public readonly bool Lethal;

        public RoadkillInfo(Vector3 point, float speed, bool lethal)
        {
            Point = point;
            Speed = speed;
            Lethal = lethal;
        }
    }
}
