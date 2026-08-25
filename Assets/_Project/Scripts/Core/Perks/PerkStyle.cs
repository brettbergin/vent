using UnityEngine;

namespace Vent.Core.Perks
{
    /// <summary>Presentation constants shared by the pickup orb and the HUD, so a perk reads the same in the world and on screen.</summary>
    public static class PerkStyle
    {
        public static string DisplayName(PerkKind kind) => kind switch
        {
            PerkKind.InstantReload => "INSTANT RELOAD",
            PerkKind.Invulnerable => "INVULNERABLE",
            PerkKind.Nuke => "NUKE",
            PerkKind.OneShot => "ONE SHOT",
            _ => kind.ToString().ToUpperInvariant(),
        };

        public static Color Color(PerkKind kind) => kind switch
        {
            PerkKind.InstantReload => new Color(0.35f, 0.75f, 1f),   // ammo blue
            PerkKind.Invulnerable => new Color(1f, 0.85f, 0.25f),    // shield gold
            PerkKind.Nuke => new Color(1f, 0.3f, 0.15f),             // fire red
            PerkKind.OneShot => new Color(0.55f, 1f, 0.45f),         // headshot green
            _ => UnityEngine.Color.white,
        };
    }
}
