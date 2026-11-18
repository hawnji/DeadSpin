namespace DeadSpin.Runtime.Interaction
{
    /// <summary>Anything the player can point at and use. Implemented by components on collider objects or their parents.</summary>
    public interface IInteractable
    {
        /// <summary>Short verb phrase shown in the HUD, e.g. "Take a seat".</summary>
        string Prompt { get; }

        /// <summary>Whether the interactor may use this right now (also hides the prompt when false).</summary>
        bool CanInteract(Interactor interactor);

        void Interact(Interactor interactor);

        /// <summary>Called when the interactor starts or stops pointing at this object.</summary>
        void SetFocused(bool focused);
    }
}
