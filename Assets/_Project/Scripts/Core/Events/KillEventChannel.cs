using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Carries <see cref="KillInfo"/>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Kill Event", fileName = "Evt_Kill")]
    public sealed class KillEventChannel : EventChannel<KillInfo> { }
}
