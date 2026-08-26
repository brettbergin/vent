using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Vent.Vehicles.Runtime;

namespace Vent.Editor
{
    /// <summary>
    /// Parks the fleet on the district's bays. The nearest spot to the front door gets the hero car —
    /// a red sedan you cannot miss on your way out — the rest are a seeded mix of sedans and vans in
    /// whatever colour came off the line.
    /// </summary>
    public static class VehiclePlacer
    {
        public static List<VehicleController> Place(GameAssets a, IReadOnlyList<DistrictGenerator.ParkingSpot> spots, Transform parent, int seed)
        {
            var rng = new System.Random(seed);
            var rootGo = new GameObject("Vehicles");
            if (parent != null)
            {
                rootGo.transform.SetParent(parent, false);
            }

            var placed = new List<VehicleController>();
            for (int i = 0; i < spots.Count; i++)
            {
                DistrictGenerator.ParkingSpot spot = spots[i];
                bool hero = i == 0;
                bool van = !hero && rng.NextDouble() < 0.3;
                GameObject prefab = van ? a.VanPrefab : a.SedanPrefab;
                if (prefab == null)
                {
                    throw new System.InvalidOperationException("Vehicle prefabs are missing; run PrefabFactory first.");
                }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rootGo.transform);
                go.name = $"Vehicle_{i}_{(van ? "Van" : "Sedan")}";
                go.transform.SetPositionAndRotation(spot.Position + Vector3.up * 0.02f, Quaternion.Euler(0f, spot.Yaw, 0f));

                Material paint = hero ? a.CarPaints[0] : a.CarPaints[rng.Next(a.CarPaints.Length)];
                var controller = go.GetComponent<VehicleController>();
                foreach (Renderer r in controller.PaintRenderers)
                {
                    r.sharedMaterial = paint;
                }

                placed.Add(controller);
            }

            return placed;
        }
    }
}
