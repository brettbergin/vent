using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vent.Core;
using Vent.Core.Audio;
using Vent.Core.Events;
using Vent.Core.Services;
using Vent.Core.Settings;
using Vent.Core.Utility;
using Vent.Gameplay.Persistence;
using Vent.Player.Input;

namespace Vent.Gameplay.Flow
{
    /// <summary>
    /// Application-level state machine: Boot → MainMenu → Playing ⇄ Paused → GameOver.
    ///
    /// Lives in the Boot scene and survives scene loads. It owns the things that must be
    /// consistent across every state — time scale, cursor lock, which input map is active,
    /// which scene is loaded — and nothing else. Gameplay rules belong to the level director;
    /// presentation belongs to the UI, which only ever hears about state through
    /// <see cref="GameStateEventChannel"/>.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputReader input;

        [Header("Requests (raised by UI)")]
        [SerializeField] private VoidEventChannel playRequested;
        [SerializeField] private VoidEventChannel resumeRequested;
        [SerializeField] private VoidEventChannel restartRequested;
        [SerializeField] private VoidEventChannel menuRequested;
        [SerializeField] private VoidEventChannel quitRequested;

        [Header("Gameplay signals")]
        [SerializeField] private VoidEventChannel playerDied;

        [Header("Broadcasts")]
        [SerializeField] private GameStateEventChannel stateChanged;
        [SerializeField] private RunSummaryEventChannel runEnded;
        [SerializeField] private IntEventChannel bestLevelChanged;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float deathToGameOverDelay = 2f;

        private static GameManager instance;

        private HighScoreStore scores;
        /// <summary>Non-null while a transition is in flight; cancelling it aborts the transition.</summary>
        private CancellationTokenSource transition;

        public GameState State { get; private set; } = GameState.Boot;
        public HighScoreStore Scores => scores;

        public void Configure(InputReader reader, VoidEventChannel play, VoidEventChannel resume, VoidEventChannel restart,
            VoidEventChannel menu, VoidEventChannel quit, VoidEventChannel died, GameStateEventChannel state,
            RunSummaryEventChannel summary, IntEventChannel bestLevel)
        {
            input = reader;
            playRequested = play;
            resumeRequested = resume;
            restartRequested = restart;
            menuRequested = menu;
            quitRequested = quit;
            playerDied = died;
            stateChanged = state;
            runEnded = summary;
            bestLevelChanged = bestLevel;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            scores = new HighScoreStore();
            GameServices.Register(this);
        }

        private void OnEnable()
        {
            playRequested?.Subscribe(OnPlayRequested);
            resumeRequested?.Subscribe(Resume);
            restartRequested?.Subscribe(OnRestartRequested);
            menuRequested?.Subscribe(OnMenuRequested);
            quitRequested?.Subscribe(Quit);
            playerDied?.Subscribe(OnPlayerDied);
            if (input != null)
            {
                input.PausePressed += Pause;
                input.UnpausePressed += OnUnpausePressed;
            }

            SettingsStore.Changed += ApplySettings;
        }

        private void OnDisable()
        {
            playRequested?.Unsubscribe(OnPlayRequested);
            resumeRequested?.Unsubscribe(Resume);
            restartRequested?.Unsubscribe(OnRestartRequested);
            menuRequested?.Unsubscribe(OnMenuRequested);
            quitRequested?.Unsubscribe(Quit);
            playerDied?.Unsubscribe(OnPlayerDied);
            if (input != null)
            {
                input.PausePressed -= Pause;
                input.UnpausePressed -= OnUnpausePressed;
            }

            SettingsStore.Changed -= ApplySettings;
            if (instance == this)
            {
                GameServices.Unregister(this);
            }
        }

        private void Start()
        {
            ApplySettings();
            ProceduralSoundBank.WarmUp();
            SetState(GameState.Boot);
            BeginTransition(LoadMenuAsync);
        }

        // ------------------------------------------------------------------ requests

        private void OnPlayRequested()
        {
            if (State == GameState.MainMenu)
            {
                BeginTransition(StartRunAsync);
            }
        }

        private void OnRestartRequested()
        {
            if (State == GameState.GameOver || State == GameState.Paused)
            {
                BeginTransition(StartRunAsync);
            }
        }

        private void OnMenuRequested()
        {
            if (State == GameState.Paused || State == GameState.GameOver)
            {
                BeginTransition(LoadMenuAsync);
            }
        }

        private void Pause()
        {
            // transition != null covers the death delay: the player is dead but the state is still Playing.
            if (State == GameState.Playing && transition == null)
            {
                SetState(GameState.Paused);
            }
        }

        private void OnUnpausePressed()
        {
            if (State == GameState.Paused)
            {
                Resume();
            }
        }

        private void Resume()
        {
            if (State == GameState.Paused)
            {
                SetState(GameState.Playing);
            }
        }

        private void OnPlayerDied()
        {
            if (State == GameState.Playing)
            {
                BeginTransition(GameOverAsync);
            }
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ transitions
        //
        // Transitions are Unity 6 Awaitables rather than coroutines: each one runs under a
        // CancellationTokenSource linked to destroyCancellationToken, so starting a new transition
        // (or destroying the manager) cancels the old one at its next await, and the "is anything in
        // flight" question is just `transition != null`.

        private void BeginTransition(Func<CancellationToken, Awaitable> run)
        {
            CancelTransition();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            transition = cts;
            RunTransition(run, cts);
        }

        private async void RunTransition(Func<CancellationToken, Awaitable> run, CancellationTokenSource cts)
        {
            try
            {
                await run(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer transition, or the manager is being destroyed.
            }
            finally
            {
                if (transition == cts)
                {
                    transition = null;
                }

                cts.Dispose();
            }
        }

        private void CancelTransition()
        {
            CancellationTokenSource current = transition;
            transition = null;
            current?.Cancel();
        }

        private void OnDestroy() => CancelTransition();

        private async Awaitable LoadMenuAsync(CancellationToken ct)
        {
            Time.timeScale = 1f;
            input?.DisableAll();
            await Awaitable.FromAsyncOperation(SceneManager.LoadSceneAsync(SceneNames.MainMenu, LoadSceneMode.Single), ct);
            bestLevelChanged?.Raise(scores.Data.BestLevel);
            SetState(GameState.MainMenu);
        }

        private async Awaitable StartRunAsync(CancellationToken ct)
        {
            Time.timeScale = 1f;
            input?.DisableAll();
            await Awaitable.FromAsyncOperation(SceneManager.LoadSceneAsync(SceneNames.Building, LoadSceneMode.Single), ct);
            await Awaitable.NextFrameAsync(ct); // let Awake/OnEnable in the new scene register services

            if (!GameServices.TryGet(out BuildingSceneController building))
            {
                Debug.LogError("GameManager: Building scene has no BuildingSceneController.");
                await LoadMenuAsync(ct);
                return;
            }

            SetState(GameState.Playing);
            building.BeginRun();
        }

        private async Awaitable GameOverAsync(CancellationToken ct)
        {
            if (GameServices.TryGet(out BuildingSceneController building))
            {
                building.EndRun();
            }

            SfxPlayer.TryPlay2D(SoundId.PlayerDeath, 0.9f);
            // Scaled time is fine here: the state stays Playing (timeScale 1) and Pause is refused while
            // a transition is in flight.
            await Awaitable.WaitForSecondsAsync(deathToGameOverDelay, ct);

            RunSummary summary = BuildSummary(building);
            SetState(GameState.GameOver);
            runEnded?.Raise(summary);
        }

        private RunSummary BuildSummary(BuildingSceneController building)
        {
            int level = 1, kills = 0, headshots = 0;
            float seconds = 0f;
            if (building != null && building.Director != null)
            {
                level = building.Director.Level;
                kills = building.Director.TotalKills;
                headshots = building.Director.Headshots;
                seconds = building.Director.ElapsedSeconds;
            }

            bool newRecord = scores.Record(level, kills, seconds);
            return new RunSummary(level, kills, headshots, seconds, scores.Data.BestLevel, newRecord);
        }

        // ------------------------------------------------------------------ state

        private void SetState(GameState next)
        {
            State = next;

            bool playing = next == GameState.Playing;
            Time.timeScale = next == GameState.Paused ? 0f : 1f;
            AudioListener.pause = next == GameState.Paused;

            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !playing;

            if (input != null)
            {
                if (playing)
                {
                    input.EnableGameplay();
                }
                else if (next == GameState.Boot)
                {
                    input.DisableAll();
                }
                else
                {
                    input.EnableUI();
                }
            }

            stateChanged?.Raise(next);
        }

        private void ApplySettings()
        {
            AudioListener.volume = SettingsStore.Volume;
        }
    }
}
