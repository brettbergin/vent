using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Carries <see cref="HealthInfo"/>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Health Event", fileName = "Evt_Health")]
    public sealed class HealthEventChannel : EventChannel<HealthInfo> { }
}
