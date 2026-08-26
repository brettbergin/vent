using NUnit.Framework;
using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// The layer table is written into TagManager in order, so the scenes bake the indices in;
    /// a layer added anywhere but the end would silently re-layer every saved object.
    /// </summary>
    public sealed class LayersTests
    {
        [Test]
        public void VehicleIsTheLastLayerAndExists()
        {
            Assert.AreEqual(Layers.Vehicle, Layers.All[^1], "new layers are appended; the earlier indices are baked into scenes");
            Assert.GreaterOrEqual(Layers.VehicleIndex, 8, "the bootstrap must have written the Vehicle layer");
        }

        [Test]
        public void CarsAreShootableAndUsableButNeverOccluders()
        {
            int vehicle = 1 << Layers.VehicleIndex;
            Assert.AreNotEqual(0, Layers.ShootableMask & vehicle, "bullets spark off cars");
            Assert.AreNotEqual(0, Layers.InteractMask & vehicle, "the player can look at a car and get in");
            Assert.AreEqual(0, Layers.OcclusionMask & vehicle, "a car must not hide the driver from zombies");
            Assert.AreNotEqual(0, Layers.InteractMask & (1 << Layers.EnvironmentIndex), "doors are Environment");
        }
    }
}
