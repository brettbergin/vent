namespace Vent.Core.Perks
{
    /// <summary>A perk instance: what it is and, for timed perks, how long it lasts (0 for instant effects).</summary>
    public readonly struct PerkInfo
    {
        public readonly PerkKind Kind;
        public readonly float Duration;

        public bool IsTimed => Duration > 0f;

        public PerkInfo(PerkKind kind, float duration)
        {
            Kind = kind;
            Duration = duration;
        }
    }
}
