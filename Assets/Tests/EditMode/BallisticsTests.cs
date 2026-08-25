using NUnit.Framework;
using Vent.Weapons.Runtime;

namespace Vent.Tests.EditMode
{
    public sealed class BallisticsTests
    {
        [Test]
        public void DamageIsFullInsideFalloffStartAndClampsAtTheFloor()
        {
            Assert.AreEqual(1f, Ballistics.DamageScale(0f, 18f, 45f, 0.55f), 1e-5f);
            Assert.AreEqual(1f, Ballistics.DamageScale(18f, 18f, 45f, 0.55f), 1e-5f);
            Assert.AreEqual(0.775f, Ballistics.DamageScale(31.5f, 18f, 45f, 0.55f), 1e-4f, "halfway through the band");
            Assert.AreEqual(0.55f, Ballistics.DamageScale(45f, 18f, 45f, 0.55f), 1e-5f);
            Assert.AreEqual(0.55f, Ballistics.DamageScale(500f, 18f, 45f, 0.55f), 1e-5f);
        }

        [Test]
        public void DegenerateFalloffBandNeverReducesDamage()
        {
            Assert.AreEqual(1f, Ballistics.DamageScale(100f, 40f, 40f, 0.5f));
            Assert.AreEqual(1f, Ballistics.DamageScale(100f, 50f, 40f, 0.5f));
        }

        [Test]
        public void RecoilClimbsFromOneToTheMaximumOverTheRamp()
        {
            Assert.AreEqual(1f, Ballistics.RecoilRamp(1, 8, 1.8f), 1e-5f, "first shot is baseline");
            Assert.AreEqual(1.8f, Ballistics.RecoilRamp(8, 8, 1.8f), 1e-5f);
            Assert.AreEqual(1.8f, Ballistics.RecoilRamp(30, 8, 1.8f), 1e-5f, "holds at max");
            float mid = Ballistics.RecoilRamp(4, 8, 1.8f);
            Assert.Greater(mid, 1f);
            Assert.Less(mid, 1.8f);
        }

        [Test]
        public void RecoilRampIsInertWhenDisabled()
        {
            Assert.AreEqual(1f, Ballistics.RecoilRamp(10, 1, 2f));
            Assert.AreEqual(1f, Ballistics.RecoilRamp(10, 8, 1f));
        }

        [Test]
        public void TacticalReloadKeepsTheChamberedRound()
        {
            Assert.AreEqual(31, Ballistics.RoundsAfterReload(30, hadRoundChambered: true));
            Assert.AreEqual(30, Ballistics.RoundsAfterReload(30, hadRoundChambered: false));
        }
    }
}
