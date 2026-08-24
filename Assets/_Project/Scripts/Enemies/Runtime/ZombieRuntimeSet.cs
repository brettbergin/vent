using UnityEngine;
using Vent.Core.Collections;

namespace Vent.Enemies.Runtime
{
    /// <summary>All zombies currently alive (or dying) in the scene.</summary>
    [CreateAssetMenu(menuName = "Vent/Enemies/Zombie Runtime Set", fileName = "Set_Zombies")]
    public sealed class ZombieRuntimeSet : RuntimeSet<Zombie> { }
}
