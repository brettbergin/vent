using UnityEngine;
using Vent.Core.Perks;

namespace Vent.Core.Events
{
    /// <summary>Carries <see cref="PerkInfo"/>: raised by a pickup when the player collects it. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Perk Event", fileName = "Evt_PerkCollected")]
    public sealed class PerkEventChannel : EventChannel<PerkInfo> { }
}
