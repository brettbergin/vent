using UnityEngine;
using Vent.Core.Services;
using Vent.Gameplay.Levels;
using Vent.Gameplay.World;
using Vent.Player;

namespace Vent.Gameplay.Flow
{
    /// <summary>
    /// The Building scene's entry point. It knows what is in the scene (player, spawn point,
    /// level director) so the persistent <see cref="GameManager"/> only needs one handle.
    /// </summary>
    public sealed class BuildingSceneController : MonoBehaviour
    {
        [SerializeField] private LevelDirector director;
        [SerializeField] private PlayerSpawnPoint spawnPoint;
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private KeyHuntDirector keyHunt;

        public LevelDirector Director => director;
        public PlayerCharacter Player => player;
        public KeyHuntDirector KeyHunt => keyHunt;

        public void Configure(LevelDirector levelDirector, PlayerSpawnPoint spawn, PlayerCharacter playerCharacter, KeyHuntDirector hunt)
        {
            director = levelDirector;
            spawnPoint = spawn;
            player = playerCharacter;
            keyHunt = hunt;
        }

        private void OnEnable() => GameServices.Register(this);
        private void OnDisable() => GameServices.Unregister(this);

        /// <summary>Place the player, reset their state, and start level 1.</summary>
        public void BeginRun()
        {
            if (player != null && spawnPoint != null)
            {
                player.ResetForNewRun(spawnPoint.Position, spawnPoint.Yaw);
            }

            director?.StartRun();
            // After StartRun: its level-1 announcement relocks the front door and clears the key,
            // so the hunt's fresh roll and objective line have to land on top of that, not under it.
            keyHunt?.BeginRun();
        }

        public void EndRun()
        {
            director?.EndRun();
            player?.SetControlsEnabled(false);
        }
    }
}
