using UnityEngine;

namespace DeadSpin.Runtime.Interaction
{
    /// <summary>An object the player can examine; its title and description are shown in the HUD.</summary>
    public sealed class InspectableObject : Interactable
    {
        [SerializeField] private string title = "Object";
        [TextArea(2, 6)] [SerializeField] private string description = "";

        public string Title => title;
        public string Description => description;

        public void SetText(string newTitle, string newDescription)
        {
            title = newTitle;
            description = newDescription;
        }

        public override void Interact(Interactor interactor)
        {
            interactor.ReportInspection(title, description);
        }
    }
}
