using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Vent.Core.Utility;
using Vent.Editor;
using Vent.Vehicles.Data;
using Vent.Vehicles.Runtime;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// The generated car prefabs: four probe wheels and no WheelColliders, a parked (kinematic)
    /// body on the Vehicle layer, colliders kept out of the visual body on a slippery material, a
    /// NavMesh obstacle so zombies walk round it, the seat's anchors, headlamps off, and every
    /// renderer sunlit.
    /// </summary>
    public sealed class VehiclePrefabTests
    {
        [TestCase(VehicleShape.Sedan)]
        [TestCase(VehicleShape.Van)]
        [TestCase(VehicleShape.Hatchback)]
        [TestCase(VehicleShape.Suv)]
        [TestCase(VehicleShape.Pickup)]
        public void HasWheelsBodyObstacleAndSeat(VehicleShape shape)
        {
            CarBodyLibrary.Spec spec = CarBodyLibrary.For(shape);
            string name = $"Vehicle_{spec.Name}";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Vent.Editor.Paths.Prefabs}/{name}.prefab");
            Assert.IsNotNull(prefab, $"{name}.prefab missing; run Vent/Rebuild Everything.");

            var controller = prefab.GetComponent<VehicleController>();
            Assert.IsNotNull(controller);
            Assert.AreEqual(shape, controller.Definition.Shape);
            Assert.AreEqual(spec.Track, controller.Definition.Track, 1e-3f, "the body and the physics agree on the track");
            Assert.AreEqual(spec.Wheelbase, controller.Definition.Wheelbase, 1e-3f, "and the wheelbase");
            Assert.AreEqual(spec.WheelRadius, controller.Definition.WheelRadius, 1e-3f, "and the wheel size");

            Transform hull = prefab.transform.Find("Body/Hull");
            Assert.IsNotNull(hull, "the hull is a lofted mesh under the visual body");
            Mesh hullMesh = hull.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(hullMesh);
            Assert.AreEqual(3, hullMesh.subMeshCount, "paint, glass, underbody");
            Assert.Greater(hullMesh.vertexCount, 200, "a real hull, not a box");
            Assert.Greater(hullMesh.GetTriangles(CarBodyLibrary.SubmeshGlass).Length, 0, "it has windows");
            Assert.AreEqual(3, hull.GetComponent<MeshRenderer>().sharedMaterials.Length);
            Assert.AreEqual("Universal Render Pipeline/Complex Lit", hull.GetComponent<MeshRenderer>().sharedMaterials[0].shader.name, "clear-coat paint");
            Bounds bounds = hullMesh.bounds;
            Assert.AreEqual(spec.Length, bounds.size.z, 0.05f, "as long as its spec");
            Assert.AreEqual(spec.Width, bounds.size.x, 0.05f, "as wide as its spec");
            Assert.AreEqual(spec.Height, bounds.max.y, 0.08f, "as tall as its spec");

            Assert.AreEqual(0, prefab.GetComponentsInChildren<WheelCollider>(true).Length, "the car probes for the road itself");
            VehicleWheel[] wheels = prefab.GetComponentsInChildren<VehicleWheel>(true);
            Assert.AreEqual(4, wheels.Length, "four probe wheels");
            foreach (VehicleWheel w in wheels)
            {
                Assert.IsNotNull(w.Visual, $"{w.name} has a visual");
                Assert.AreEqual(-w.RestLength, w.Visual.localPosition.y, 1e-4f, $"{w.name} hangs at its parked length");
                Assert.AreEqual(w.Axle == 0, w.Steered, "the front axle steers");
            }

            var body = prefab.GetComponent<Rigidbody>();
            Assert.IsNotNull(body);
            Assert.GreaterOrEqual(body.mass, 1000f);
            Assert.IsTrue(body.isKinematic, "parked cars are kinematic until someone gets in");

            int vehicle = Layers.VehicleIndex;
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                bool lamp = t.GetComponent<Light>() != null;
                Assert.AreEqual(lamp ? Layers.PlayerIndex : vehicle, t.gameObject.layer, $"{t.name} must be on the {(lamp ? "Player (light)" : "Vehicle")} layer");
            }

            var obstacle = prefab.GetComponent<NavMeshObstacle>();
            Assert.IsNotNull(obstacle, "parked cars carve the NavMesh");
            Assert.IsTrue(obstacle.carving && obstacle.carveOnlyStationary);

            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Assert.AreNotEqual(0u, r.renderingLayerMask & (1u << 1), $"{r.name} must be lit by the sun (exterior rendering layer)");
            }

            Assert.IsNotNull(prefab.GetComponent<VehicleRoadkill>());
            Assert.IsNotNull(prefab.GetComponent<VehicleAudio>());
            Assert.IsNotNull(prefab.GetComponent<VehicleBodyMotion>());
            var lights = prefab.GetComponent<VehicleLights>();
            Assert.IsNotNull(lights);
            Assert.AreEqual(2, lights.Headlights.Length);
            foreach (Light beam in lights.Headlights)
            {
                Assert.IsFalse(beam.enabled, "headlamps are off until someone gets in");
                Assert.AreEqual(LightType.Spot, beam.type);
            }

            var seat = prefab.GetComponent<VehicleSeat>();
            Assert.IsNotNull(seat);
            Assert.IsNotNull(seat.Anchor);
            Assert.IsNotNull(seat.ExitLeft);
            Assert.IsNotNull(seat.ExitRight);
            Assert.IsNotNull(seat.CameraTarget);
            Assert.IsNotNull(seat.MuzzleOut);
            Assert.IsNotNull(seat.Arm);
            Assert.IsFalse(seat.Arm.gameObject.activeSelf, "the drive-by arm shows only while occupied");

            BoxCollider[] boxes = prefab.GetComponentsInChildren<BoxCollider>(true);
            Assert.GreaterOrEqual(boxes.Length, 6, "chassis, cabin and a guard per wheel collide");
            Transform visualBody = prefab.transform.Find("Body");
            Assert.IsNotNull(visualBody);
            Assert.AreEqual(0, visualBody.GetComponentsInChildren<Collider>(true).Length, "the visual body leans, so it carries no colliders");
            foreach (BoxCollider box in boxes)
            {
                Assert.IsNotNull(box.sharedMaterial, $"{box.name} needs the slippery bodywork material");
                Assert.Less(box.sharedMaterial.dynamicFriction, 0.5f);
            }
        }
    }
}
