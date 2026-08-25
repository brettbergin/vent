using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Event carrying a single <c>bool</c>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Bool Event", fileName = "Evt_Bool")]
    public sealed class BoolEventChannel : EventChannel<bool> { }
}
