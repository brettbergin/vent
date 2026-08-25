using NUnit.Framework;
using UnityEngine;
using Vent.Core.Perks;

namespace Vent.Tests.EditMode
{
    public sealed class PerkDropTableTests
    {
        private static PerkDropTable Defaults()
        {
            var table = ScriptableObject.CreateInstance<PerkDropTable>();
            table.ApplyDefaults();
            return table;
        }

        [Test]
        public void DefaultsCoverEveryKindWithPositiveWeight()
        {
            PerkDropTable table = Defaults();
            foreach (PerkKind kind in System.Enum.GetValues(typeof(PerkKind)))
            {
                bool found = false;
                foreach (PerkDropTable.Entry e in table.Entries)
                {
                    found |= e.Kind == kind && e.Weight > 0f;
                }

                Assert.IsTrue(found, $"{kind} must be droppable by default");
            }

            Assert.Greater(table.DropChance, 0f);
            Assert.Less(table.DropChance, 0.5f, "perks are a treat, not the loop");
        }

        [Test]
        public void ChanceRollGatesTheDrop()
        {
            PerkDropTable table = Defaults();
            Assert.IsTrue(table.TryRoll(0.0, 0.5, out _), "a roll under the chance drops");
            Assert.IsFalse(table.TryRoll(table.DropChance, 0.5, out _), "a roll at the chance does not");
            Assert.IsFalse(table.TryRoll(0.999, 0.5, out _));
        }

        [Test]
        public void KindRollWalksTheWeightsInOrder()
        {
            PerkDropTable table = Defaults();
            float total = 0f;
            foreach (PerkDropTable.Entry e in table.Entries)
            {
                total += e.Weight;
            }

            // Just inside the first bucket, and just inside the last.
            Assert.IsTrue(table.TryPick(0.0, out PerkInfo first));
            Assert.AreEqual(table.Entries[0].Kind, first.Kind);
            Assert.IsTrue(table.TryPick((total - 0.01f) / total, out PerkInfo last));
            Assert.AreEqual(table.Entries[^1].Kind, last.Kind);
            Assert.IsTrue(table.TryPick(0.999999, out PerkInfo edge), "a roll at the very end still picks");
            Assert.AreEqual(table.Entries[^1].Kind, edge.Kind);
        }

        [Test]
        public void TimedPerksCarryTheirDurationAndInstantOnesDoNot()
        {
            PerkDropTable table = Defaults();
            Assert.IsTrue(table.Describe(PerkKind.Invulnerable).IsTimed);
            Assert.IsTrue(table.Describe(PerkKind.OneShot).IsTimed);
            Assert.IsFalse(table.Describe(PerkKind.Nuke).IsTimed);
            Assert.IsFalse(table.Describe(PerkKind.InstantReload).IsTimed);
        }

        [Test]
        public void EveryKindHasANameAndAColour()
        {
            foreach (PerkKind kind in System.Enum.GetValues(typeof(PerkKind)))
            {
                Assert.IsNotEmpty(PerkStyle.DisplayName(kind));
                Assert.AreNotEqual(Color.white, PerkStyle.Color(kind), $"{kind} needs a distinct colour");
            }
        }
    }
}
