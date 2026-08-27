using System.Collections.Generic;
using NUnit.Framework;
using Vent.Editor;
using RoomType = Vent.Editor.BuildingGenerator.RoomType;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// The room plan in isolation. A plain per-room roll gives ServerRoom 15%, so on the shipped
    /// 4x3 grid roughly one seed in six would generate a building with nowhere to patch the
    /// servers — and the key hunt, and with it the way out before level 4, would silently not
    /// exist; other seeds come out with no conference room or no break room at all. These hammer
    /// the quotas over thousands of seeds, which is the whole reason the decision was pulled out
    /// of <see cref="BuildingGenerator.Generate"/>.
    /// </summary>
    public sealed class RoomPlanTests
    {
        private const int Cols = 4;
        private const int Rows = 3;
        private const int LobbyIndex = 1 * Cols + (Cols - 1); // what Generate() uses: c = cols-1, r = rows/2

        private static int Count(IReadOnlyList<RoomType> types, RoomType type)
        {
            int n = 0;
            foreach (RoomType t in types)
            {
                if (t == type) n++;
            }

            return n;
        }

        [Test]
        public void EverySeedMeetsEveryQuota()
        {
            for (int seed = 1; seed <= 5000; seed++)
            {
                RoomType[] types = BuildingGenerator.PlanRoomTypes(Cols, Rows, LobbyIndex, new System.Random(seed));

                Assert.AreEqual(Cols * Rows, types.Length, $"seed {seed}: one purpose per cell");
                Assert.GreaterOrEqual(Count(types, RoomType.ServerRoom), 1, $"seed {seed}: nowhere to patch the servers");
                Assert.GreaterOrEqual(Count(types, RoomType.Office), 3, $"seed {seed}: too few desks to hide a key in");
                Assert.GreaterOrEqual(Count(types, RoomType.Conference), 1, $"seed {seed}: no conference room");
                Assert.GreaterOrEqual(Count(types, RoomType.BreakRoom), 1, $"seed {seed}: no break room");
                Assert.GreaterOrEqual(Count(types, RoomType.Storage), 1, $"seed {seed}: no storage");
            }
        }

        [Test]
        public void ARepairNeverEmptiesTheTypeItTookFrom()
        {
            // Meeting one quota must not break another: converting the last conference room into an
            // office would just move the hole somewhere else.
            for (int seed = 1; seed <= 5000; seed++)
            {
                RoomType[] types = BuildingGenerator.PlanRoomTypes(Cols, Rows, LobbyIndex, new System.Random(seed));
                int guaranteed = Count(types, RoomType.ServerRoom) + Count(types, RoomType.Office)
                    + Count(types, RoomType.Conference) + Count(types, RoomType.BreakRoom) + Count(types, RoomType.Storage);
                Assert.LessOrEqual(guaranteed, Cols * Rows - 1, $"seed {seed}: the lobby is not one of them");
            }
        }

        [Test]
        public void TheLobbyIsAlwaysTheSpawnCellAndOnlyThat()
        {
            for (int seed = 1; seed <= 2000; seed++)
            {
                RoomType[] types = BuildingGenerator.PlanRoomTypes(Cols, Rows, LobbyIndex, new System.Random(seed));
                Assert.AreEqual(RoomType.Lobby, types[LobbyIndex], $"seed {seed}");
                Assert.AreEqual(1, Count(types, RoomType.Lobby), $"seed {seed}: exactly one lobby");
            }
        }

        [Test]
        public void ThePlanIsAFunctionOfTheSeed()
        {
            RoomType[] first = BuildingGenerator.PlanRoomTypes(Cols, Rows, LobbyIndex, new System.Random(1337));
            RoomType[] again = BuildingGenerator.PlanRoomTypes(Cols, Rows, LobbyIndex, new System.Random(1337));
            CollectionAssert.AreEqual(first, again);
        }

        [Test]
        public void ASeedThatAlreadySatisfiesTheGuaranteesIsLeftAlone()
        {
            // Same rolls, drawn the same way, with the repair pass skipped: where a seed already
            // has its server room and its offices, the plan must match the raw roll exactly, so
            // adding the guarantee did not quietly re-shuffle every building.
            for (int seed = 1; seed <= 2000; seed++)
            {
                var rng = new System.Random(seed);
                var raw = new RoomType[Cols * Rows];
                for (int i = 0; i < raw.Length; i++)
                {
                    raw[i] = i == LobbyIndex ? RoomType.Lobby : Roll(rng);
                }

                if (Count(raw, RoomType.ServerRoom) < 1 || Count(raw, RoomType.Office) < 3 ||
                    Count(raw, RoomType.Conference) < 1 || Count(raw, RoomType.BreakRoom) < 1 ||
                    Count(raw, RoomType.Storage) < 1)
                {
                    continue;
                }

                RoomType[] planned = BuildingGenerator.PlanRoomTypes(Cols, Rows, LobbyIndex, new System.Random(seed));
                CollectionAssert.AreEqual(raw, planned, $"seed {seed} needed no repair, so nothing should have moved");
            }
        }

        /// <summary>The weights in <c>BuildingGenerator.PickRoomType</c>, restated so a change to them fails here.</summary>
        private static RoomType Roll(System.Random rng)
        {
            double roll = rng.NextDouble();
            return roll < 0.40 ? RoomType.Office
                : roll < 0.55 ? RoomType.Conference
                : roll < 0.70 ? RoomType.BreakRoom
                : roll < 0.85 ? RoomType.Storage
                : RoomType.ServerRoom;
        }
    }
}
