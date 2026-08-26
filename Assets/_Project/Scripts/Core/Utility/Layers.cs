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
        /// <summary>Cars: shootable and solid to the player, but they pass through zombies (roadkill is applied in code).</summary>
        public const string Vehicle = "Vehicle";

        public static int PlayerIndex => LayerMask.NameToLayer(Player);
        public static int ZombieIndex => LayerMask.NameToLayer(Zombie);
        public static int EnvironmentIndex => LayerMask.NameToLayer(Environment);
        public static int VentIndex => LayerMask.NameToLayer(Vent);
        public static int ProjectileIndex => LayerMask.NameToLayer(Projectile);
        public static int WeaponViewIndex => LayerMask.NameToLayer(WeaponView);
        public static int VehicleIndex => LayerMask.NameToLayer(Vehicle);

        /// <summary>What bullets can hit: the world, zombies and cars, never the player or the view-model.</summary>
        public static int ShootableMask => LayerMask.GetMask(Environment, Zombie, Vent, Vehicle);

        /// <summary>What blocks line of sight for spawn selection (walls and props only). Cars are deliberately not occluders: a driver is still visible.</summary>
        public static int OcclusionMask => LayerMask.GetMask(Environment);

        /// <summary>What the player can look at and press Interact on: doors (Environment) and cars.</summary>
        public static int InteractMask => LayerMask.GetMask(Environment, Vehicle);

        /// <summary>All layer names the project needs, in a stable order (used by the bootstrap). Append only: indices are baked into scenes.</summary>
        public static readonly string[] All = { Player, Zombie, Environment, Vent, Projectile, WeaponView, Vehicle };

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
