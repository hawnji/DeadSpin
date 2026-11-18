using UnityEngine;
#if ENABLE_INPUT_SYSTEM && DEADSPIN_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DeadSpin.Runtime.Player
{
    /// <summary>
    /// Samples input once per frame, before any consumer runs, and exposes it as a plain struct.
    /// Supports the Input System package (preferred) and the legacy Input Manager, chosen by the
    /// project's "Active Input Handling" setting. Gameplay scripts never read devices directly,
    /// so rebinding and controller support can be added here later without touching them.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInputReader : MonoBehaviour
    {
#if ENABLE_INPUT_SYSTEM && DEADSPIN_INPUT_SYSTEM
        [Tooltip("Multiplier applied to Input System mouse deltas so both backends feel the same.")]
        [SerializeField] private float inputSystemMouseScale = 0.1f;
#elif !ENABLE_LEGACY_INPUT_MANAGER
        private bool _reportedMissingBackend;
#endif

        public PlayerInputFrame Current { get; private set; }

        /// <summary>When false every value reads as neutral (used while paused or during cutscenes).</summary>
        public bool InputEnabled { get; set; } = true;

        private void Update()
        {
            PlayerInputFrame frame = Sample();
            if (!InputEnabled)
            {
                // Keep "back" and the debug toggle alive so the player can always unpause.
                frame = new PlayerInputFrame
                {
                    PointerPosition = frame.PointerPosition,
                    BackPressed = frame.BackPressed,
                    DebugTogglePressed = frame.DebugTogglePressed
                };
            }
            Current = frame;
        }

        private PlayerInputFrame Sample()
        {
            var frame = new PlayerInputFrame();
#if ENABLE_INPUT_SYSTEM && DEADSPIN_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard != null)
            {
                float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                float y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                frame.Move = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
                frame.Sprint = keyboard.leftShiftKey.isPressed;
                frame.InteractPressed = keyboard.eKey.wasPressedThisFrame;
                frame.BackPressed = keyboard.escapeKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame;
                frame.DebugTogglePressed = keyboard.f1Key.wasPressedThisFrame;
            }
            if (mouse != null)
            {
                frame.Look = mouse.delta.ReadValue() * inputSystemMouseScale;
                frame.PointerPosition = mouse.position.ReadValue();
                frame.InteractPressed |= mouse.leftButton.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            frame.Move = Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
            frame.Look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            frame.PointerPosition = Input.mousePosition;
            frame.Sprint = Input.GetKey(KeyCode.LeftShift);
            frame.InteractPressed = Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0);
            frame.BackPressed = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab);
            frame.DebugTogglePressed = Input.GetKeyDown(KeyCode.F1);
#else
            if (!_reportedMissingBackend)
            {
                _reportedMissingBackend = true;
                Debug.LogError("[PlayerInputReader] No input backend is active. Install the Input System package or enable the legacy Input Manager in Project Settings > Player > Active Input Handling.", this);
            }
#endif
            return frame;
        }
    }
}
