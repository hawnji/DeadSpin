using UnityEngine;

namespace DeadSpin.Runtime.Player
{
    /// <summary>
    /// Grounded first-person movement on a CharacterController: walk, sprint, gravity and smooth
    /// acceleration. No jumping: the casino is a place you walk through carefully.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;

        [Header("Speeds (m/s)")]
        [Min(0f)] [SerializeField] private float walkSpeed = 2.4f;
        [Min(0f)] [SerializeField] private float sprintSpeed = 4.2f;
        [Tooltip("How quickly velocity approaches the target speed. Higher = snappier.")]
        [Min(0.1f)] [SerializeField] private float acceleration = 10f;

        [Header("Gravity")]
        [SerializeField] private float gravity = -18f;
        [Tooltip("Small downward force while grounded so the controller sticks to slopes and steps.")]
        [SerializeField] private float groundedStickForce = -2f;

        private CharacterController _controller;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;

        /// <summary>When false the player stands still (seated, paused, cutscenes). Gravity still applies.</summary>
        public bool MovementEnabled { get; set; } = true;

        /// <summary>Current planar speed, useful for head bob and footstep audio.</summary>
        public float PlanarSpeed => _horizontalVelocity.magnitude;

        public bool IsGrounded => _controller != null && _controller.isGrounded;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (input == null)
            {
                Debug.LogError("[FirstPersonController] No PlayerInputReader assigned or found on the player.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 move = MovementEnabled ? input.Current.Move : Vector2.zero;
            float speed = MovementEnabled && input.Current.Sprint ? sprintSpeed : walkSpeed;

            Vector3 desired = (transform.right * move.x + transform.forward * move.y) * speed;
            float blend = 1f - Mathf.Exp(-acceleration * dt);
            _horizontalVelocity = Vector3.Lerp(_horizontalVelocity, desired, blend);

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStickForce;
            }
            else
            {
                _verticalVelocity += gravity * dt;
            }

            Vector3 velocity = _horizontalVelocity + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * dt);
        }

        /// <summary>Moves the player instantly (spawns, run restarts). Disables the controller briefly so the move is not swallowed.</summary>
        public void Teleport(Vector3 position, float yawDegrees)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            _controller.enabled = true;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }
    }
}
