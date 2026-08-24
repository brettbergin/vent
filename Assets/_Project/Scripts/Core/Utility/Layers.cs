using UnityEngine;

namespace Vent.Core.Utility
{
    /// <summary>
    /// Single source of truth for physics layer names. The editor bootstrap creates these layers
    /// in TagManager; runtime code resolves masks through here rather than hard-coding indices.
    /// </summary>
    public static class Layers
    {
        public const string Player = "Player";
        public const string Zombie = "Zombie";
        public const string Environment = "Environment";
        public const string Vent = "Vent";
        public const string Projectile = "Projectile";
        public const string WeaponView = "WeaponView";

        public static int PlayerIndex => LayerMask.NameToLayer(Player);
        public static int ZombieIndex => LayerMask.NameToLayer(Zombie);
        public static int EnvironmentIndex => LayerMask.NameToLayer(Environment);
        public static int VentIndex => LayerMask.NameToLayer(Vent);
        public static int ProjectileIndex => LayerMask.NameToLayer(Projectile);
        public static int WeaponViewIndex => LayerMask.NameToLayer(WeaponView);

        /// <summary>What bullets can hit: the world and zombies, never the player or the view-model.</summary>
        public static int ShootableMask => LayerMask.GetMask(Environment, Zombie, Vent);

        /// <summary>What blocks line of sight for spawn selection (walls and props only).</summary>
        public static int OcclusionMask => LayerMask.GetMask(Environment);

        /// <summary>All layer names the project needs, in a stable order (used by the bootstrap).</summary>
        public static readonly string[] All = { Player, Zombie, Environment, Vent, Projectile, WeaponView };

        /// <summary>Set a layer on an object and all descendants.</summary>
        public static void SetRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetRecursively(child.gameObject, layer);
            }
        }
    }
}
