using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Vent.Core.Utility;
using Vent.Vehicles.Runtime;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// The generated car prefabs: four wheels, a parked (kinematic) body on the Vehicle layer, a
    /// NavMesh obstacle so zombies walk round it, the seat's anchors, and every renderer sunlit.
    /// </summary>
    public sealed class VehiclePrefabTests
    {
        [TestCase("Vehicle_Sedan")]
        [TestCase("Vehicle_Van")]
        public void HasWheelsBodyObstacleAndSeat(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Vent.Editor.Paths.Prefabs}/{name}.prefab");
            Assert.IsNotNull(prefab, $"{name}.prefab missing; run Vent/Rebuild Everything.");

            Assert.AreEqual(4, prefab.GetComponentsInChildren<WheelCollider>(true).Length, "four wheel colliders");
            var body = prefab.GetComponent<Rigidbody>();
            Assert.IsNotNull(body);
            Assert.GreaterOrEqual(body.mass, 1000f);
            Assert.IsTrue(body.isKinematic, "parked cars are kinematic until someone gets in");

            int vehicle = Layers.VehicleIndex;
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                Assert.AreEqual(vehicle, t.gameObject.layer, $"{t.name} must be on the Vehicle layer");
            }

            var obstacle = prefab.GetComponent<NavMeshObstacle>();
            Assert.IsNotNull(obstacle, "parked cars carve the NavMesh");
            Assert.IsTrue(obstacle.carving && obstacle.carveOnlyStationary);

            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Assert.AreNotEqual(0u, r.renderingLayerMask & (1u << 1), $"{r.name} must be lit by the sun (exterior rendering layer)");
            }

            Assert.IsNotNull(prefab.GetComponent<VehicleController>());
            Assert.IsNotNull(prefab.GetComponent<VehicleRoadkill>());
            Assert.IsNotNull(prefab.GetComponent<VehicleAudio>());
            var seat = prefab.GetComponent<VehicleSeat>();
            Assert.IsNotNull(seat);
            Assert.IsNotNull(seat.Anchor);
            Assert.IsNotNull(seat.ExitLeft);
            Assert.IsNotNull(seat.ExitRight);
            Assert.IsNotNull(seat.CameraTarget);
            Assert.IsNotNull(seat.MuzzleOut);
            Assert.IsNotNull(seat.Arm);
            Assert.IsFalse(seat.Arm.gameObject.activeSelf, "the drive-by arm shows only while occupied");
            Assert.GreaterOrEqual(prefab.GetComponentsInChildren<BoxCollider>(true).Length, 2, "chassis and cabin collide");
        }
    }
}
