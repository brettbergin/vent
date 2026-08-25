namespace Vent.Core.Perks
{
    /// <summary>
    /// Every power-up a zombie can drop. Adding one means: a case here, a row in
    /// <see cref="PerkDropTable.ApplyDefaults"/>, a colour/name in <see cref="PerkStyle"/>, and a
    /// consumer (the player, the weapons, or <c>PerkSystem</c>) that reacts to it.
    /// </summary>
    public enum PerkKind
    {
        /// <summary>Both guns are topped up instantly; an in-progress reload completes.</summary>
        InstantReload,
        /// <summary>The player ignores damage for the perk's duration.</summary>
        Invulnerable,
        /// <summary>Every zombie alive dies at once. Counts toward the level.</summary>
        Nuke,
        /// <summary>Any hit kills for the perk's duration.</summary>
        OneShot,
    }
}
