using UnityEngine;
using Vent.Core.Interaction;

namespace Vent.Gameplay.World
{
    /// <summary>
    /// A coil of patch cable on a shelf or a desktop. Taken by looking at it and pressing
    /// Interact, deliberately not by walking through it the way a perk orb is collected: these
    /// are the objects the player is hunting for, and picking one up by accident while sprinting
    /// past would rob the find of its moment.
    ///
    /// Every candidate coil exists in the baked scene; <see cref="KeyHuntDirector"/> rolls three
    /// of them at the start of each run and shows them once the whiteboard has been read.
    /// </summary>
    public sealed class PatchCablePickup : MonoBehaviour, IInteractable
    {
        [SerializeField] private KeyHuntDirector hunt;
        [SerializeField, Tooltip("Which room this coil is in, so the director can spread the three it picks.")]
        private int room;

        public int Room => room;

        /// <summary>Editor-time wiring used by the building generator.</summary>
        public void Configure(KeyHuntDirector director, int roomIndex)
        {
            hunt = director;
            room = roomIndex;
        }

        public string Prompt => "TAKE PATCH CABLE";
        public bool IsAvailable => hunt == null || hunt.State.CablesShown;

        public void Interact() => hunt?.CableTaken(this);
    }
}
