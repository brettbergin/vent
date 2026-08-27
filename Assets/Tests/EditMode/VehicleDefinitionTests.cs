using NUnit.Framework;
using UnityEngine;
using Vent.Vehicles.Data;

namespace Vent.Tests.EditMode
{
    /// <summary>The car's numbers: sane defaults for both bodies, and the pure curves the controller and roadkill rely on.</summary>
    public sealed class VehicleDefinitionTests
    {
        [TestCase(VehicleShape.Sedan)]
        [TestCase(VehicleShape.Van)]
        [TestCase(VehicleShape.Hatchback)]
        [TestCase(VehicleShape.Suv)]
        [TestCase(VehicleShape.Pickup)]
        public void DefaultsAreSane(VehicleShape shape)
        {
            var def = ScriptableObject.CreateInstance<VehicleDefinition>();
            def.ApplyDefaults(shape);
            Assert.AreEqual(shape, def.Shape);
            Assert.Greater(def.Mass, 1000f);
            Assert.Greater(def.TopSpeed, 0f);
            Assert.Greater(def.RoadkillLethalSpeed, def.RoadkillMinSpeed);
            Assert.Greater(def.RoadkillMinSpeed, 0f);
            Assert.That(def.OccupantDamageFactor, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.Less(def.HandbrakeGripScale, 1f, "the handbrake loosens the rear");
            Assert.AreEqual(1f, def.TyreForceHeight, "cornering forces act at the centre of mass, so steering cannot roll the car");
            Assert.Less(def.CentreOfMass.y, def.WheelRadius + 0.05f, "the centre of mass sits at hub height");
            Assert.Less(def.RestCompression, def.SuspensionTravel * 0.5f, "the springs carry the car in the first half of their travel");
            Assert.Greater(def.Drivetrain.GearRatios.Length, 2, "enough gears to hear a shift");
            Object.DestroyImmediate(def);
        }

        [Test]
        public void RoadkillDamageRampsThenBecomesLethal()
        {
            var def = ScriptableObject.CreateInstance<VehicleDefinition>();
            def.ApplyDefaults(VehicleShape.Sedan);
            Assert.AreEqual(0f, def.RoadkillDamage(3f), "a nudge does nothing");
            Assert.AreEqual(50f, def.RoadkillDamage(6.5f), 0.01f, "halfway between the ends is halfway up the ramp");
            Assert.AreEqual(def.RoadkillDamage(9f), def.RoadkillDamage(30f), "lethal is lethal");
            Assert.Greater(def.RoadkillDamage(9f), 1000f);
            Assert.AreEqual(def.RoadkillDamage(6.5f), def.RoadkillDamage(-6.5f), "reversing over a zombie counts");
            Object.DestroyImmediate(def);
        }

        [Test]
        public void SteeringTightensAtSpeed()
        {
            var def = ScriptableObject.CreateInstance<VehicleDefinition>();
            def.ApplyDefaults(VehicleShape.Sedan);
            Assert.AreEqual(def.MaxSteerDegrees, def.SteerAngle(0f), 0.001f);
            Assert.Less(def.SteerAngle(def.TopSpeed), 6f, "a few degrees is all the tyres can use at the top speed");
            Assert.Greater(def.SteerAngle(def.TopSpeed), 0f);
            Assert.Greater(def.SteerAngle(8f), def.SteerAngle(16f));
            Object.DestroyImmediate(def);
        }
    }
}
