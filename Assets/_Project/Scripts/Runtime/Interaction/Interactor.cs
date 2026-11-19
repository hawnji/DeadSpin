using System;
using DeadSpin.Runtime.Player;
using UnityEngine;

namespace DeadSpin.Runtime.Interaction
{
    /// <summary>
    /// Finds the interactable under the mouse cursor and uses it on left click. The table is
    /// viewed from above, so all interaction is cursor-based. Ignored while the player drags to pan.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class Interactor : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [Min(0.5f)] [SerializeField] private float range = 12f;
        [SerializeField] private LayerMask interactionMask = ~0;

        private Camera _camera;
        private IInteractable _focused;

        /// <summary>The object currently under the cursor that can be used, or null.</summary>
        public IInteractable Focused => _focused;

        /// <summary>Prompt for the focused object, or null when nothing is usable.</summary>
        public string CurrentPrompt => _focused != null ? _focused.Prompt : null;

        /// <summary>Raised when an inspectable reports text to show (title, body).</summary>
        public event Action<string, string> InspectionReported;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (input == null) input = GetComponentInParent<PlayerInputReader>();
            if (input == null) input = FindFirstObjectByType<PlayerInputReader>();
            if (input == null)
            {
                Debug.LogError("[Interactor] No PlayerInputReader found.", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            SetFocus(null);
        }

        private void Update()
        {
            PlayerInputFrame frame = input.Current;
            SetFocus(frame.DragHeld ? null : FindTarget(frame.PointerPosition));

            if (_focused != null && frame.InteractPressed)
            {
                _focused.Interact(this);
            }
        }

        /// <summary>Lets interactables push inspection text to whatever UI listens.</summary>
        public void ReportInspection(string title, string body)
        {
            InspectionReported?.Invoke(title, body);
        }

        private IInteractable FindTarget(Vector2 pointer)
        {
            if (pointer.x < 0f || pointer.y < 0f || pointer.x > Screen.width || pointer.y > Screen.height) return null;

            Ray ray = _camera.ScreenPointToRay(pointer);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, range, interactionMask, QueryTriggerInteraction.Ignore)) return null;

            IInteractable candidate = hit.collider.GetComponentInParent<IInteractable>();
            // Destroyed Unity objects compare equal to null only through UnityEngine.Object.
            var asObject = candidate as UnityEngine.Object;
            if (asObject == null) return null;
            return candidate.CanInteract(this) ? candidate : null;
        }

        private void SetFocus(IInteractable next)
        {
            if (ReferenceEquals(next, _focused)) return;
            var previousObject = _focused as UnityEngine.Object;
            if (previousObject != null) _focused.SetFocused(false);
            _focused = next;
            if (_focused != null) _focused.SetFocused(true);
        }
    }
}
