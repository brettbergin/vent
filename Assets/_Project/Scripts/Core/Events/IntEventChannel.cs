using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Event carrying a single <c>int</c>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Int Event", fileName = "Evt_Int")]
    public sealed class IntEventChannel : EventChannel<int> { }
}
