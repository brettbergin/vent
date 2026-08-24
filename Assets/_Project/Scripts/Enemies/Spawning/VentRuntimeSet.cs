using UnityEngine;
using Vent.Core.Collections;

namespace Vent.Enemies.Spawning
{
    /// <summary>All AC vents in the building; the spawner picks from these.</summary>
    [CreateAssetMenu(menuName = "Vent/Enemies/Vent Runtime Set", fileName = "Set_Vents")]
    public sealed class VentRuntimeSet : RuntimeSet<AirVent> { }
}
