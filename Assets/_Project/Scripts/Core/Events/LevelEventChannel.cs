using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Carries <see cref="LevelInfo"/>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Level Event", fileName = "Evt_Level")]
    public sealed class LevelEventChannel : EventChannel<LevelInfo> { }
}
