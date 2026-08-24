using UnityEngine;
using Vent.Core.Services;
using Vent.Gameplay.Levels;
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

        public LevelDirector Director => director;
        public PlayerCharacter Player => player;

        public void Configure(LevelDirector levelDirector, PlayerSpawnPoint spawn, PlayerCharacter playerCharacter)
        {
            director = levelDirector;
            spawnPoint = spawn;
            player = playerCharacter;
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
        }

        public void EndRun()
        {
            director?.EndRun();
            player?.SetControlsEnabled(false);
        }
    }
}
