using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>Carries <see cref="WeaponLevelUpInfo"/>. One class per file: Unity resolves asset scripts by file name.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Weapon Level Up Event", fileName = "Evt_WeaponLevelUp")]
    public sealed class WeaponLevelUpEventChannel : EventChannel<WeaponLevelUpInfo> { }
}
