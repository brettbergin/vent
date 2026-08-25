using System;
using UnityEngine;

namespace Vent.Core.Perks
{
    /// <summary>
    /// Tuning for perk drops: how often a kill drops one, the relative weight and duration of each
    /// kind, and how long an orb waits on the floor. The rolling itself is pure arithmetic over
    /// caller-supplied random numbers so it is deterministic and unit tested; shipped values live
    /// in <see cref="ApplyDefaults"/> like every other data asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Vent/Perks/Drop Table", fileName = "PerkDrops")]
    public sealed class PerkDropTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public PerkKind Kind;
            [Min(0f), Tooltip("Relative chance among the kinds; 0 disables the perk.")]
            public float Weight;
            [Min(0f), Tooltip("Seconds the effect lasts; 0 for instant effects.")]
            public float Duration;
        }

        [SerializeField, Range(0f, 1f), Tooltip("Chance that a weapon kill drops a perk.")]
        private float dropChance = 0.12f;
        [SerializeField, Min(1f), Tooltip("Seconds an orb waits on the floor before fading.")]
        private float pickupLifetime = 25f;
        [SerializeField, Min(1), Tooltip("No new drops while this many orbs are already on the floor.")]
        private int maxOnFloor = 3;
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public float DropChance => dropChance;
        public float PickupLifetime => pickupLifetime;
        public int MaxOnFloor => maxOnFloor;
        public Entry[] Entries => entries;

        public void ApplyDefaults()
        {
            dropChance = 0.12f;
            pickupLifetime = 25f;
            maxOnFloor = 3;
            entries = new[]
            {
                new Entry { Kind = PerkKind.InstantReload, Weight = 35f, Duration = 0f },
                new Entry { Kind = PerkKind.Invulnerable, Weight = 25f, Duration = 10f },
                new Entry { Kind = PerkKind.OneShot, Weight = 25f, Duration = 8f },
                new Entry { Kind = PerkKind.Nuke, Weight = 15f, Duration = 0f },
            };
        }

        /// <summary>
        /// Roll a drop. <paramref name="chanceRoll"/> and <paramref name="kindRoll"/> are uniform in [0, 1);
        /// the caller owns the random source so tests (and replays) can be exact.
        /// </summary>
        public bool TryRoll(double chanceRoll, double kindRoll, out PerkInfo perk)
        {
            perk = default;
            if (chanceRoll >= dropChance)
            {
                return false;
            }

            return TryPick(kindRoll, out perk);
        }

        /// <summary>Pick a kind by weight, ignoring the drop chance.</summary>
        public bool TryPick(double kindRoll, out PerkInfo perk)
        {
            perk = default;
            float total = 0f;
            foreach (Entry e in entries)
            {
                total += Mathf.Max(0f, e.Weight);
            }

            if (total <= 0f)
            {
                return false;
            }

            double cursor = kindRoll * total;
            foreach (Entry e in entries)
            {
                float w = Mathf.Max(0f, e.Weight);
                if (w <= 0f)
                {
                    continue;
                }

                if (cursor < w)
                {
                    perk = new PerkInfo(e.Kind, e.Duration);
                    return true;
                }

                cursor -= w;
            }

            // Floating-point edge (kindRoll → 1): the last weighted entry.
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                if (entries[i].Weight > 0f)
                {
                    perk = new PerkInfo(entries[i].Kind, entries[i].Duration);
                    return true;
                }
            }

            return false;
        }

        /// <summary>The configured instance of a kind (its duration), for direct spawns and tests.</summary>
        public PerkInfo Describe(PerkKind kind)
        {
            foreach (Entry e in entries)
            {
                if (e.Kind == kind)
                {
                    return new PerkInfo(kind, e.Duration);
                }
            }

            return new PerkInfo(kind, 0f);
        }
    }
}
