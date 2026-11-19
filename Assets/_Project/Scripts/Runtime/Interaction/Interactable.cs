using UnityEngine;

namespace DeadSpin.Runtime.Interaction
{
    /// <summary>
    /// Base class for interactables. Handles the prompt text and a subtle emissive highlight on
    /// focus (applied through a MaterialPropertyBlock so shared materials are never modified).
    /// </summary>
    public abstract class Interactable : MonoBehaviour, IInteractable
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private string prompt = "Interact";
        [Tooltip("Renderers that glow while focused. Leave empty for no highlight.")]
        [SerializeField] private Renderer[] highlightRenderers = new Renderer[0];
        [SerializeField] private Color highlightColor = new Color(0.35f, 0.25f, 0.08f);

        private MaterialPropertyBlock _block;

        public virtual string Prompt => prompt;

        public bool IsFocused { get; private set; }

        public void Configure(string promptText, params Renderer[] renderers)
        {
            prompt = promptText;
            highlightRenderers = renderers ?? new Renderer[0];
        }

        public virtual bool CanInteract(Interactor interactor)
        {
            return isActiveAndEnabled;
        }

        public abstract void Interact(Interactor interactor);

        public void SetFocused(bool focused)
        {
            IsFocused = focused;
            ApplyHighlight(focused);
            OnFocusChanged(focused);
        }

        /// <summary>Extra feedback for subclasses (e.g. lighting up every number a bet covers).</summary>
        protected virtual void OnFocusChanged(bool focused)
        {
        }

        private void ApplyHighlight(bool on)
        {
            if (highlightRenderers == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            foreach (Renderer target in highlightRenderers)
            {
                if (target == null) continue;
                target.GetPropertyBlock(_block);
                if (on)
                {
                    _block.SetColor(EmissionColorId, highlightColor);
                    target.SetPropertyBlock(_block);
                }
                else
                {
                    _block.Clear();
                    target.SetPropertyBlock(_block);
                }
            }
        }
    }
}
