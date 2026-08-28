using UnityEngine;
using Vent.Core.Items;

namespace Vent.Core.Events
{
    /// <summary>Carries <see cref="OfficeItemInfo"/>: raised when the player picks up a building map or a rear-view mirror. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Office Item Event", fileName = "Evt_ItemCollected")]
    public sealed class OfficeItemEventChannel : EventChannel<OfficeItemInfo> { }
}
