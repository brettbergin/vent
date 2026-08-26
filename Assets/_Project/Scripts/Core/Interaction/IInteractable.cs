namespace Vent.Core.Interaction
{
    /// <summary>
    /// Something the player can look at and press Interact on: the front door, a car seat.
    /// Lives in Core so the HUD prompt, the player and the things being interacted with never
    /// need to reference each other's assemblies.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Shown while the player looks at it; empty hides the prompt.</summary>
        string Prompt { get; }

        /// <summary>
        /// True when <see cref="Interact"/> does something right now; the interactor prefixes the
        /// key hint. <see cref="Interact"/> is still called when false, so a locked door can rattle.
        /// </summary>
        bool IsAvailable { get; }

        void Interact();
    }
}
