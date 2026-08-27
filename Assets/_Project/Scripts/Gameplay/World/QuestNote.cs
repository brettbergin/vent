using UnityEngine;
using Vent.Core.Interaction;

namespace Vent.Gameplay.World
{
    /// <summary>
    /// The hint on the lobby whiteboard, and the entry point to the key hunt. Sits on the
    /// whiteboard's root, which is safe because the board is the only collider under it.
    ///
    /// Always readable, including after the fact: the board says what the chain is, and a player
    /// who forgot which room the racks were in should be able to walk back and check.
    /// </summary>
    public sealed class QuestNote : MonoBehaviour, IInteractable
    {
        [SerializeField] private KeyHuntDirector hunt;
        [SerializeField] private string prompt = "READ THE NOTE";

        /// <summary>Editor-time wiring used by the building generator.</summary>
        public void Configure(KeyHuntDirector director) => hunt = director;

        public string Prompt => prompt;
        public bool IsAvailable => true;

        public void Interact() => hunt?.NoteRead();
    }
}
