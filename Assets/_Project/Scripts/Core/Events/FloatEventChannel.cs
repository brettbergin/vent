using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Event carrying a single <c>float</c>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Float Event", fileName = "Evt_Float")]
    public sealed class FloatEventChannel : EventChannel<float> { }
}
