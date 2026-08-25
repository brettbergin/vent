using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Something loud happened (gunfire). Listeners decide whether they were close enough to hear it.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Noise Event", fileName = "Evt_Noise")]
    public sealed class NoiseEventChannel : EventChannel<NoiseInfo> { }
}
