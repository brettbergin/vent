using System.Collections.Generic;
using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Events;
using Vent.Core.Items;
using Vent.Core.Services;

namespace Vent.Gameplay.World
{
    /// <summary>
    /// The things to find around the office that are not the key: a floor plan and a mirror. Like
    /// the key hunt, every candidate spot is built at regen and this picks one of each per run, so
    /// the items are never in the same place twice. Taking one raises <c>Evt_ItemCollected</c>; the
    /// HUD and the player's rear camera listen, and a new run takes both away again.
    /// </summary>
    public sealed class OfficeItemDirector : MonoBehaviour
    {
        [Header("Events out")]
        [SerializeField] private OfficeItemEventChannel itemCollected;
        [SerializeField] private StringEventChannel announcement;

        [Header("Candidates, placed at regen")]
        [SerializeField] private OfficeItemPickup[] maps = System.Array.Empty<OfficeItemPickup>();
        [SerializeField] private OfficeItemPickup[] mirrors = System.Array.Empty<OfficeItemPickup>();
        [SerializeField] private Vector3 playerSpawn;

        [Header("The map")]
        [SerializeField, Tooltip("The floor plan drawn at regen, transparent outside the walls.")]
        private Texture2D mapTexture;
        [SerializeField, Tooltip("World x/z extent the map image covers.")]
        private Rect mapWorldRect;

        [Header("Tuning")]
        [SerializeField, Min(0f), Tooltip("Metres an item must be from the spawn, so it is never the first thing you trip over.")]
        private float minDistanceFromSpawn = 10f;
        [SerializeField, Tooltip("Non-zero pins the per-run roll; tests use it. Zero rolls fresh every run, which is what ships.")]
        private int seedOverride;

        [Header("Text")]
        [SerializeField] private string mapText = "BUILDING MAP\nPRESS C TO OPEN IT";
        [SerializeField] private string mirrorText = "REAR-VIEW MIRROR\nEYES IN THE BACK OF YOUR HEAD";

        public bool HasMap { get; private set; }
        public bool HasMirror { get; private set; }

        /// <summary>The map shown this run (hidden again once taken).</summary>
        public OfficeItemPickup ActiveMap { get; private set; }

        /// <summary>The mirror shown this run (hidden again once taken).</summary>
        public OfficeItemPickup ActiveMirror { get; private set; }

        public int LastSeed { get; private set; }
        public int SeedOverride { get => seedOverride; set => seedOverride = value; }
        public IReadOnlyList<OfficeItemPickup> Maps => maps;
        public IReadOnlyList<OfficeItemPickup> Mirrors => mirrors;
        public Texture2D MapTexture => mapTexture;
        public Rect MapWorldRect => mapWorldRect;

        /// <summary>Editor-time wiring used by the building generator.</summary>
        public void Configure(OfficeItemEventChannel items, StringEventChannel banner, OfficeItemPickup[] mapCandidates, OfficeItemPickup[] mirrorCandidates,
            Texture2D map, Rect worldRect, Vector3 spawn)
        {
            itemCollected = items;
            announcement = banner;
            maps = mapCandidates;
            mirrors = mirrorCandidates;
            mapTexture = map;
            mapWorldRect = worldRect;
            playerSpawn = spawn;
        }

        private void OnEnable() => GameServices.Register(this);
        private void OnDisable() => GameServices.Unregister(this);

        /// <summary>Hide everything, then roll where this run's map and mirror are. Called by <c>BuildingSceneController.BeginRun</c>.</summary>
        public void BeginRun()
        {
            LastSeed = seedOverride != 0 ? seedOverride : System.Guid.NewGuid().GetHashCode();
            var rng = new System.Random(LastSeed);
            HasMap = false;
            HasMirror = false;
            HideAll(maps);
            HideAll(mirrors);

            ActiveMap = Pick(maps, rng, avoidRoom: -1);
            ActiveMirror = Pick(mirrors, rng, avoidRoom: ActiveMap != null ? ActiveMap.Room : -1);
            if (ActiveMap != null)
            {
                ActiveMap.gameObject.SetActive(true);
            }

            if (ActiveMirror != null)
            {
                ActiveMirror.gameObject.SetActive(true);
            }
        }

        /// <summary>The player took one. Only the item this run put out counts.</summary>
        public void Taken(OfficeItemPickup pickup)
        {
            if (pickup == null)
            {
                return;
            }

            if (pickup == ActiveMap && !HasMap)
            {
                HasMap = true;
                pickup.gameObject.SetActive(false);
                SfxPlayer.TryPlayAt(SoundId.ItemPickup, pickup.transform.position, 0.8f);
                announcement?.Raise(mapText);
                itemCollected?.Raise(new OfficeItemInfo(OfficeItem.BuildingMap, mapTexture, mapWorldRect));
            }
            else if (pickup == ActiveMirror && !HasMirror)
            {
                HasMirror = true;
                pickup.gameObject.SetActive(false);
                SfxPlayer.TryPlayAt(SoundId.ItemPickup, pickup.transform.position, 0.8f);
                announcement?.Raise(mirrorText);
                itemCollected?.Raise(new OfficeItemInfo(OfficeItem.RearViewMirror));
            }
        }

        private static void HideAll(OfficeItemPickup[] items)
        {
            foreach (OfficeItemPickup item in items)
            {
                if (item != null)
                {
                    item.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>A candidate far enough from the spawn and, if possible, not in <paramref name="avoidRoom"/>; anything rather than nothing.</summary>
        private OfficeItemPickup Pick(OfficeItemPickup[] candidates, System.Random rng, int avoidRoom)
        {
            var live = new List<OfficeItemPickup>();
            foreach (OfficeItemPickup item in candidates)
            {
                if (item != null)
                {
                    live.Add(item);
                }
            }

            if (live.Count == 0)
            {
                return null;
            }

            var preferred = new List<OfficeItemPickup>();
            foreach (OfficeItemPickup item in live)
            {
                if (item.Room != avoidRoom && Vector3.Distance(item.transform.position, playerSpawn) > minDistanceFromSpawn)
                {
                    preferred.Add(item);
                }
            }

            List<OfficeItemPickup> pool = preferred.Count > 0 ? preferred : live;
            return pool[rng.Next(pool.Count)];
        }
    }
}
