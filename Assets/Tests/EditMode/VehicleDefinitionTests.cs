using NUnit.Framework;
using UnityEngine;
using Vent.Vehicles.Data;

namespace Vent.Tests.EditMode
{
    /// <summary>The car's numbers: sane defaults for both bodies, and the two pure curves the controller and roadkill rely on.</summary>
    public sealed class VehicleDefinitionTests
    {
        [TestCase(VehicleShape.Sedan)]
        [TestCase(VehicleShape.Van)]
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
            Assert.Less(def.HandbrakeSidewaysStiffness, def.SidewaysStiffness, "the handbrake loosens the rear");
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
            Assert.Less(def.SteerAngle(1f), def.SteerAngle(0f));
            Assert.Greater(def.SteerAngle(1f), 0f);
            Object.DestroyImmediate(def);
        }
    }
}
