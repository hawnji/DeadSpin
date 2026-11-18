using System;
using DeadSpin.Runtime.Player;
using UnityEngine;

namespace DeadSpin.Runtime.Interaction
{
    /// <summary>
    /// Finds the interactable the player is pointing at and triggers it.
    /// Crosshair mode casts from the screen centre (free walking); Pointer mode casts through the
    /// mouse cursor (seated at the table, where the cursor is visible).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class Interactor : MonoBehaviour
    {
        public enum AimMode
        {
            Crosshair,
            Pointer
        }

        [SerializeField] private PlayerInputReader input;
        [Min(0.1f)] [SerializeField] private float crosshairRange = 2.6f;
        [Min(0.1f)] [SerializeField] private float pointerRange = 4f;
        [SerializeField] private LayerMask interactionMask = ~0;

        private Camera _camera;
        private IInteractable _focused;

        public AimMode Mode { get; set; } = AimMode.Crosshair;

        /// <summary>The object currently under the crosshair/cursor that can be used, or null.</summary>
        public IInteractable Focused => _focused;

        /// <summary>Prompt for the focused object, or null when nothing is usable.</summary>
        public string CurrentPrompt => _focused != null ? _focused.Prompt : null;

        /// <summary>Raised when an inspectable reports text to show (title, body).</summary>
        public event Action<string, string> InspectionReported;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (input == null) input = GetComponentInParent<PlayerInputReader>();
            if (input == null)
            {
                Debug.LogError("[Interactor] No PlayerInputReader found in parents.", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            SetFocus(null);
        }

        private void Update()
        {
            SetFocus(FindTarget());

            if (_focused != null && input.Current.InteractPressed)
            {
                _focused.Interact(this);
            }
        }

        /// <summary>Lets interactables push inspection text to whatever UI listens.</summary>
        public void ReportInspection(string title, string body)
        {
            InspectionReported?.Invoke(title, body);
        }

        private IInteractable FindTarget()
        {
            Ray ray;
            float range;
            if (Mode == AimMode.Pointer)
            {
                ray = _camera.ScreenPointToRay(input.Current.PointerPosition);
                range = pointerRange;
            }
            else
            {
                ray = new Ray(_camera.transform.position, _camera.transform.forward);
                range = crosshairRange;
            }

            // Walking: trigger volumes widen easy targets such as the seat. Seated: triggers are
            // ignored so they never block clicks on the table surface behind them.
            QueryTriggerInteraction triggers = Mode == AimMode.Pointer ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide;
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, range, interactionMask, triggers)) return null;

            IInteractable candidate = hit.collider.GetComponentInParent<IInteractable>();
            if (candidate == null) return null;
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
