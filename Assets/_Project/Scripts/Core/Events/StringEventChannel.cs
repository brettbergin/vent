using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Event carrying a single <c>string</c>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/String Event", fileName = "Evt_String")]
    public sealed class StringEventChannel : EventChannel<string> { }
}
