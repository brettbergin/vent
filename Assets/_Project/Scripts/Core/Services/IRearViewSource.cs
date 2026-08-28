using UnityEngine;

namespace Vent.Core.Services
{
    /// <summary>
    /// Whatever renders the view behind the player. Registered in <see cref="GameServices"/> by the
    /// player prefab; the HUD polls it so the UI assembly never references the Player assembly.
    /// </summary>
    public interface IRearViewSource
    {
        /// <summary>The rendered rear view; null until the mirror has been found.</summary>
        RenderTexture View { get; }

        /// <summary>True while the mirror is held and rendering.</summary>
        bool IsActive { get; }
    }
}
