using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Vent.Vehicles.Data;
using Vent.Vehicles.Runtime;

namespace Vent.Editor
{
    /// <summary>
    /// Parks the fleet on the district's bays. The nearest spot to the front door gets the hero car —
    /// a red sedan you cannot miss on your way out — the rest are a seeded mix of body styles in
    /// whatever colour came off the line.
    /// </summary>
    public static class VehiclePlacer
    {
        private static readonly (VehicleShape shape, int weight)[] Mix =
        {
            (VehicleShape.Sedan, 30), (VehicleShape.Hatchback, 24), (VehicleShape.Suv, 20), (VehicleShape.Pickup, 12), (VehicleShape.Van, 14),
        };

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
                VehicleShape shape = hero ? VehicleShape.Sedan : Pick(rng);
                GameObject prefab = a.VehiclePrefab(shape);
                if (prefab == null)
                {
                    throw new System.InvalidOperationException("Vehicle prefabs are missing; run PrefabFactory first.");
                }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rootGo.transform);
                go.name = $"Vehicle_{i}_{shape}";
                go.transform.SetPositionAndRotation(spot.Position + Vector3.up * 0.02f, Quaternion.Euler(0f, spot.Yaw, 0f));

                Material paint = hero ? a.CarPaints[0] : a.CarPaints[rng.Next(a.CarPaints.Length)];
                var controller = go.GetComponent<VehicleController>();
                foreach (Renderer r in controller.PaintRenderers)
                {
                    // The hull carries paint, glass and underbody; only the paint slot changes colour.
                    Material[] materials = r.sharedMaterials;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        if (materials[m] == a.CarPaints[0])
                        {
                            materials[m] = paint;
                        }
                    }

                    r.sharedMaterials = materials;
                }

                placed.Add(controller);
            }

            return placed;
        }

        private static VehicleShape Pick(System.Random rng)
        {
            int total = 0;
            foreach ((_, int weight) in Mix)
            {
                total += weight;
            }

            int roll = rng.Next(total);
            foreach ((VehicleShape shape, int weight) in Mix)
            {
                roll -= weight;
                if (roll < 0)
                {
                    return shape;
                }
            }

            return VehicleShape.Sedan;
        }
    }
}
