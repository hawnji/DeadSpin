using UnityEngine;

namespace DeadSpin.Runtime.Player
{
    /// <summary>
    /// Mouse look: yaw rotates the player body, pitch rotates the camera pivot. Optional light
    /// smoothing keeps the camera from feeling jittery without adding noticeable lag.
    /// </summary>
    public sealed class PlayerLook : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [Tooltip("Child transform that holds the camera; receives pitch.")]
        [SerializeField] private Transform pitchPivot;

        [Header("Feel")]
        [Min(0.01f)] [SerializeField] private float sensitivity = 2f;
        [SerializeField] private bool invertY;
        [Range(0f, 0.95f)] [SerializeField] private float smoothing = 0.35f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        private float _yaw;
        private float _pitch;
        private Vector2 _smoothedDelta;

        public bool LookEnabled { get; set; } = true;

        public float Sensitivity
        {
            get { return sensitivity; }
            set { sensitivity = Mathf.Max(0.01f, value); }
        }

        public Transform PitchPivot => pitchPivot;

        /// <summary>Assigns the pitch pivot from code (scene factory). Must be called before Awake.</summary>
        public void SetPitchPivot(Transform pivot)
        {
            pitchPivot = pivot;
        }

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (input == null || pitchPivot == null)
            {
                Debug.LogError("[PlayerLook] Requires a PlayerInputReader and a pitch pivot transform.", this);
                enabled = false;
                return;
            }
            SyncFromTransforms();
        }

        private void Update()
        {
            if (!LookEnabled)
            {
                _smoothedDelta = Vector2.zero;
                return;
            }

            Vector2 raw = input.Current.Look * sensitivity;
            // Frame-rate independent exponential smoothing; smoothing = 0 disables it.
            float keep = Mathf.Pow(smoothing, Time.unscaledDeltaTime * 60f);
            _smoothedDelta = Vector2.Lerp(raw, _smoothedDelta, keep);

            _yaw += _smoothedDelta.x;
            _pitch += invertY ? _smoothedDelta.y : -_smoothedDelta.y;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        /// <summary>Re-reads yaw/pitch from the transforms (after a teleport or scripted camera move).</summary>
        public void SyncFromTransforms()
        {
            _yaw = transform.localEulerAngles.y;
            float pitch = pitchPivot.localEulerAngles.x;
            _pitch = pitch > 180f ? pitch - 360f : pitch;
            _smoothedDelta = Vector2.zero;
        }
    }
}
