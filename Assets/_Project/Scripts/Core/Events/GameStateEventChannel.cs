using UnityEngine;
using Vent.Core;

namespace Vent.Core.Events
{
    /// <summary>Carries <see cref="GameState"/>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Game State Event", fileName = "Evt_GameState")]
    public sealed class GameStateEventChannel : EventChannel<GameState> { }
}
