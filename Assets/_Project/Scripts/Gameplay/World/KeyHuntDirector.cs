using System.Collections.Generic;
using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Events;
using Vent.Core.Services;

namespace Vent.Gameplay.World
{
    /// <summary>
    /// Runs the alternate way out of the building, and — the whole point — re-rolls it at the
    /// start of every run.
    ///
    /// The building is generated at edit time from a fixed seed and baked into the scene, so
    /// anything decided there would put the key in the same drawer for ever and the player would
    /// walk straight to it on their second run. So the generator places *every* candidate — a
    /// drawer on each desk, a patch panel on each rack, a cable coil on every flat surface it
    /// passed — and this picks among them here, at runtime, once per run. Nothing moves; the roll
    /// only decides what is shown, what is lit and what has a key in it.
    /// </summary>
    public sealed class KeyHuntDirector : MonoBehaviour
    {
        [Header("Events out")]
        [SerializeField] private StringEventChannel objective;
        [SerializeField] private StringEventChannel announcement;
        [SerializeField, Tooltip("Raised when the key is taken, so the front door knows the player is carrying one.")]
        private VoidEventChannel keyFound;

        [Header("Candidates, placed at regen")]
        [SerializeField] private QuestNote note;
        [SerializeField] private DeskDrawer[] drawers = System.Array.Empty<DeskDrawer>();
        [SerializeField] private PatchPanel[] panels = System.Array.Empty<PatchPanel>();
        [SerializeField] private PatchCablePickup[] cables = System.Array.Empty<PatchCablePickup>();
        [SerializeField] private Vector3 playerSpawn;

        [Header("Tuning")]
        [SerializeField, Min(1)] private int cablesRequired = 3;
        [SerializeField, Min(0f), Tooltip("Metres between the coils that get shown, so three cables are never one shelf.")]
        private float minCableSeparation = 14f;
        [SerializeField, Min(0f), Tooltip("Metres the key desk must be from the spawn, so the key is never the first thing you trip over.")]
        private float minKeyDistanceFromSpawn = 14f;
        [SerializeField, Tooltip("Non-zero pins the per-run roll; tests use it. Zero rolls fresh every run, which is what ships.")]
        private int seedOverride;

        [Header("Text")]
        [SerializeField] private string hintText = "THE SERVERS ARE DOWN\nTHREE PATCH CABLES WILL BRING THEM UP";
        [SerializeField] private string poweredText = "POWER RESTORED\nONE TERMINAL CAME BACK UP";
        [SerializeField] private string keyText = "FRONT DOOR KEY\nTHE LOBBY DOOR WILL OPEN NOW";

        private readonly List<PatchCablePickup> activeCables = new();
        private KeyHuntState state;

        public KeyHuntState State => state ??= new KeyHuntState(cablesRequired);

        /// <summary>The desk whose drawer holds the key this run.</summary>
        public DeskDrawer KeyDrawer { get; private set; }

        /// <summary>The one rack the cables go into this run.</summary>
        public PatchPanel ActivePanel { get; private set; }

        /// <summary>The three coils shown this run.</summary>
        public IReadOnlyList<PatchCablePickup> ActiveCables => activeCables;

        /// <summary>The seed the last roll actually used; logged so a strange run can be reproduced.</summary>
        public int LastSeed { get; private set; }

        /// <summary>Non-zero pins the roll. Tests set this; the shipped value is zero.</summary>
        public int SeedOverride { get => seedOverride; set => seedOverride = value; }

        public QuestNote Note => note;
        public IReadOnlyList<DeskDrawer> Drawers => drawers;
        public IReadOnlyList<PatchPanel> Panels => panels;

        /// <summary>Editor-time wiring used by the building generator.</summary>
        public void Configure(StringEventChannel objectiveChannel, StringEventChannel banner, VoidEventChannel keyChannel,
            QuestNote hintNote, DeskDrawer[] deskDrawers, PatchPanel[] rackPanels, PatchCablePickup[] coils, Vector3 spawn)
        {
            objective = objectiveChannel;
            announcement = banner;
            keyFound = keyChannel;
            note = hintNote;
            drawers = deskDrawers;
            panels = rackPanels;
            cables = coils;
            playerSpawn = spawn;
        }

        private void OnEnable() => GameServices.Register(this);

        private void OnDisable()
        {
            GameServices.Unregister(this);
            objective?.Raise(string.Empty);
        }

        /// <summary>
        /// Put everything back, then roll this run's cables, rack and key desk.
        /// Called by <c>BuildingSceneController.BeginRun</c>.
        /// </summary>
        public void BeginRun()
        {
            // Guid rather than UnityEngine.Random: the global stream is seeded by tests and by
            // other systems, and a run's hiding places should not be a function of either.
            LastSeed = seedOverride != 0 ? seedOverride : System.Guid.NewGuid().GetHashCode();
            var rng = new System.Random(LastSeed);

            State.Reset();
            KeyDrawer = null;
            ActivePanel = null;
            activeCables.Clear();

            foreach (DeskDrawer drawer in drawers)
            {
                if (drawer != null)
                {
                    drawer.ResetForNewRun();
                }
            }

            foreach (PatchPanel panel in panels)
            {
                if (panel != null)
                {
                    panel.ResetForNewRun();
                    panel.gameObject.SetActive(false);
                }
            }

            foreach (PatchCablePickup cable in cables)
            {
                if (cable != null)
                {
                    cable.gameObject.SetActive(false);
                }
            }

            ChoosePanel(rng);
            ChooseKeyDesk(rng);
            ChooseCables(rng);
            PublishObjective();
        }

        // ----- callbacks from the things the player looks at -----

        public void NoteRead()
        {
            State.ReadHint();
            announcement?.Raise(hintText);
            PublishObjective();
        }

        public void CableTaken(PatchCablePickup cable)
        {
            if (cable == null || !State.TakeCable())
            {
                return;
            }

            SfxPlayer.TryPlayAt(SoundId.CablePickup, cable.transform.position, 0.8f);
            cable.gameObject.SetActive(false);
            activeCables.Remove(cable);
            PublishObjective();
        }

        /// <summary>Only the rack this run chose does anything; the others are not even shown.</summary>
        public PanelAction TryRestorePower(PatchPanel panel)
        {
            if (panel != ActivePanel)
            {
                return PanelAction.NotEnoughCables;
            }

            PanelAction action = State.TryRestorePower();
            if (action != PanelAction.Restored)
            {
                return action;
            }

            KeyDrawer?.SetScreenLit(true);
            announcement?.Raise(poweredText);
            PublishObjective();
            return action;
        }

        public bool IsKeyDrawer(DeskDrawer drawer) => drawer != null && drawer == KeyDrawer;

        public DrawerAction TryOpenDrawer(DeskDrawer drawer)
        {
            DrawerAction action = State.TryOpenDrawer(IsKeyDrawer(drawer));
            if (action != DrawerAction.KeyTaken)
            {
                return action;
            }

            keyFound?.Raise();
            announcement?.Raise(keyText);
            PublishObjective();
            return action;
        }

        /// <summary>The front door turning the key. True the first time only.</summary>
        public bool SpendKey()
        {
            if (!State.SpendKey())
            {
                return false;
            }

            PublishObjective();
            return true;
        }

        // ----- the roll -----

        private void ChoosePanel(System.Random rng)
        {
            List<PatchPanel> live = Alive(panels);
            if (live.Count == 0)
            {
                return;
            }

            ActivePanel = live[rng.Next(live.Count)];
            ActivePanel.gameObject.SetActive(true);
        }

        private void ChooseKeyDesk(System.Random rng)
        {
            List<DeskDrawer> live = Alive(drawers);
            if (live.Count == 0)
            {
                return;
            }

            // Far enough from the spawn that the player has to go looking. If the building is small
            // enough that nothing qualifies, take anything rather than leave the run unwinnable.
            var far = new List<DeskDrawer>();
            foreach (DeskDrawer drawer in live)
            {
                if (Vector3.Distance(drawer.transform.position, playerSpawn) > minKeyDistanceFromSpawn)
                {
                    far.Add(drawer);
                }
            }

            List<DeskDrawer> pool = far.Count > 0 ? far : live;
            KeyDrawer = pool[rng.Next(pool.Count)];
        }

        private void ChooseCables(System.Random rng)
        {
            List<PatchCablePickup> pool = Alive(cables);
            Shuffle(pool, rng);

            // Spread them out: three different rooms, and not on top of each other. Each pass
            // relaxes a rule, so a cramped building still ends up with the full set rather than a
            // run the player cannot finish.
            if (!Take(pool, State.CablesRequired, minCableSeparation, distinctRooms: true) &&
                !Take(pool, State.CablesRequired, minCableSeparation * 0.5f, distinctRooms: false))
            {
                Take(pool, State.CablesRequired, 0f, distinctRooms: false);
            }

            foreach (PatchCablePickup cable in activeCables)
            {
                cable.gameObject.SetActive(true);
            }
        }

        /// <summary>Greedily fill <see cref="activeCables"/>; true when it reached <paramref name="wanted"/>.</summary>
        private bool Take(List<PatchCablePickup> pool, int wanted, float separation, bool distinctRooms)
        {
            activeCables.Clear();
            var rooms = new HashSet<int>();
            foreach (PatchCablePickup candidate in pool)
            {
                if (activeCables.Count >= wanted)
                {
                    break;
                }

                if (distinctRooms && !rooms.Add(candidate.Room))
                {
                    continue;
                }

                bool crowded = false;
                foreach (PatchCablePickup taken in activeCables)
                {
                    if (Vector3.Distance(taken.transform.position, candidate.transform.position) < separation)
                    {
                        crowded = true;
                        break;
                    }
                }

                if (!crowded)
                {
                    activeCables.Add(candidate);
                }
            }

            return activeCables.Count >= wanted;
        }

        private void PublishObjective() => objective?.Raise(State.Objective);

        private static List<T> Alive<T>(T[] source) where T : Object
        {
            var live = new List<T>();
            if (source == null)
            {
                return live;
            }

            foreach (T item in source)
            {
                if (item != null)
                {
                    live.Add(item);
                }
            }

            return live;
        }

        private static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
