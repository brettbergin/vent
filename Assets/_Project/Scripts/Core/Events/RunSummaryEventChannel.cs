using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Carries <see cref="RunSummary"/>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Run Summary Event", fileName = "Evt_RunSummary")]
    public sealed class RunSummaryEventChannel : EventChannel<RunSummary> { }
}
