using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Carries <see cref="WeaponHudInfo"/>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Weapon HUD Event", fileName = "Evt_WeaponHud")]
    public sealed class WeaponHudEventChannel : EventChannel<WeaponHudInfo> { }
}
