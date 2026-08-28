using UnityEngine;
using Vent.Core.Interaction;
using Vent.Core.Items;

namespace Vent.Gameplay.World
{
    /// <summary>
    /// A building map or a vanity mirror lying on a desk or a shelf. Taken by looking at it and
    /// pressing Interact, like a patch cable. Every candidate exists in the baked scene;
    /// <see cref="OfficeItemDirector"/> shows one of each kind per run.
    /// </summary>
    public sealed class OfficeItemPickup : MonoBehaviour, IInteractable
    {
        [SerializeField] private OfficeItemDirector director;
        [SerializeField] private OfficeItem kind;
        [SerializeField, Tooltip("Which room this sits in, so the two items are never on the same shelf.")]
        private int room;

        public OfficeItem Kind => kind;
        public int Room => room;

        /// <summary>Editor-time wiring used by the building generator.</summary>
        public void Configure(OfficeItemDirector owner, OfficeItem item, int roomIndex)
        {
            director = owner;
            kind = item;
            room = roomIndex;
        }

        public string Prompt => kind == OfficeItem.BuildingMap ? "TAKE THE BUILDING MAP" : "TAKE THE MIRROR";
        public bool IsAvailable => true;

        public void Interact() => director?.Taken(this);
    }
}
