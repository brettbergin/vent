using UnityEngine;

namespace Vent.Core.Items
{
    /// <summary>
    /// Payload of <c>Evt_ItemCollected</c>. The map travels with its own image and the world
    /// rectangle the image covers, so the HUD — which lives in the persistent Boot scene and knows
    /// nothing about the building — can draw it and place the player on it.
    /// </summary>
    public readonly struct OfficeItemInfo
    {
        public readonly OfficeItem Kind;
        /// <summary>The floor plan, transparent outside the walls. Null for anything but the map.</summary>
        public readonly Texture2D Map;
        /// <summary>World extent of the map image: x is world x, y is world z. Zero for anything but the map.</summary>
        public readonly Rect WorldRect;

        public OfficeItemInfo(OfficeItem kind, Texture2D map = null, Rect worldRect = default)
        {
            Kind = kind;
            Map = map;
            WorldRect = worldRect;
        }
    }
}
