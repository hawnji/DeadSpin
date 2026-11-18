using UnityEngine;

namespace DeadSpin.Runtime.Player
{
    /// <summary>
    /// Smoothly blends the camera between its normal first-person anchor (its parent) and a focus
    /// point such as the table seat view. While focused, the view drifts slightly toward the mouse
    /// cursor so the static table view still feels alive.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFocus : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [Min(0.01f)] [SerializeField] private float blendSeconds = 0.75f;
        [Tooltip("Maximum degrees the focused view turns toward the cursor.")]
        [Range(0f, 20f)] [SerializeField] private float cursorDriftDegrees = 6f;

        private Transform _target;
        private float _weight;

        /// <summary>True while blending in, focused, or blending out.</summary>
        public bool IsActive => _target != null || _weight > 0f;

        /// <summary>True once the camera has fully arrived at the focus point.</summary>
        public bool IsSettled => _target != null && _weight >= 1f;

        private Transform _lastTarget;

        private void Awake()
        {
            if (input == null) input = GetComponentInParent<PlayerInputReader>();
        }

        public void Focus(Transform target)
        {
            if (target == null)
            {
                Debug.LogWarning("[CameraFocus] Focus called with a null target.", this);
                return;
            }
            _target = target;
            _lastTarget = target;
        }

        public void Release()
        {
            _target = null;
        }

        private void LateUpdate()
        {
            float direction = _target != null ? 1f : -1f;
            _weight = Mathf.Clamp01(_weight + direction * Time.unscaledDeltaTime / blendSeconds);

            if (_weight <= 0f)
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                return;
            }

            Transform focus = _target != null ? _target : _lastTarget;
            if (focus == null)
            {
                _weight = 0f;
                return;
            }

            Transform anchor = transform.parent;
            Vector3 anchorPosition = anchor != null ? anchor.position : transform.position;
            Quaternion anchorRotation = anchor != null ? anchor.rotation : transform.rotation;

            Quaternion focusRotation = focus.rotation * CursorDrift();
            float t = Mathf.SmoothStep(0f, 1f, _weight);
            transform.SetPositionAndRotation(
                Vector3.Lerp(anchorPosition, focus.position, t),
                Quaternion.Slerp(anchorRotation, focusRotation, t));
        }

        private Quaternion CursorDrift()
        {
            if (input == null || cursorDriftDegrees <= 0f || Screen.width <= 0 || Screen.height <= 0) return Quaternion.identity;
            Vector2 pointer = input.Current.PointerPosition;
            float nx = Mathf.Clamp(pointer.x / Screen.width * 2f - 1f, -1f, 1f);
            float ny = Mathf.Clamp(pointer.y / Screen.height * 2f - 1f, -1f, 1f);
            return Quaternion.Euler(-ny * cursorDriftDegrees * 0.5f, nx * cursorDriftDegrees, 0f);
        }
    }
}
