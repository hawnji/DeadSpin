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

        /// <summary>When false only Back, the debug toggle and the pointer position are reported (paused, menus).</summary>
        public bool InputEnabled { get; set; } = true;

        private void Update()
        {
            PlayerInputFrame frame = Sample();
            if (!InputEnabled)
            {
                frame = new PlayerInputFrame
                {
                    PointerPosition = frame.PointerPosition,
                    BackPressed = frame.BackPressed,
                    DebugTogglePressed = frame.DebugTogglePressed,
                    ViewSelected = -1
                };
            }
            Current = frame;
        }

        private PlayerInputFrame Sample()
        {
            var frame = new PlayerInputFrame { ViewSelected = -1 };
#if ENABLE_INPUT_SYSTEM && DEADSPIN_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard != null)
            {
                float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                        - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
                float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                        - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
                frame.Pan = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
                frame.NextViewPressed = keyboard.tabKey.wasPressedThisFrame;
                frame.BackPressed = keyboard.escapeKey.wasPressedThisFrame;
                frame.DebugTogglePressed = keyboard.f1Key.wasPressedThisFrame;
                if (keyboard.digit1Key.wasPressedThisFrame) frame.ViewSelected = 0;
                else if (keyboard.digit2Key.wasPressedThisFrame) frame.ViewSelected = 1;
                else if (keyboard.digit3Key.wasPressedThisFrame) frame.ViewSelected = 2;
                else if (keyboard.digit4Key.wasPressedThisFrame) frame.ViewSelected = 3;
            }
            if (mouse != null)
            {
                frame.PointerDelta = mouse.delta.ReadValue() * inputSystemMouseScale;
                frame.PointerPosition = mouse.position.ReadValue();
                frame.InteractPressed = mouse.leftButton.wasPressedThisFrame;
                frame.DragHeld = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
                // Scroll magnitude per notch differs by platform and package version; count notches by sign.
                float scroll = mouse.scroll.ReadValue().y;
                frame.Zoom = Mathf.Abs(scroll) > 0.01f ? Mathf.Sign(scroll) : 0f;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            float lx = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                     - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float ly = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                     - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            frame.Pan = Vector2.ClampMagnitude(new Vector2(lx, ly), 1f);
            frame.PointerDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            frame.PointerPosition = Input.mousePosition;
            frame.InteractPressed = Input.GetMouseButtonDown(0);
            frame.DragHeld = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            float legacyScroll = Input.mouseScrollDelta.y;
            frame.Zoom = Mathf.Abs(legacyScroll) > 0.01f ? Mathf.Sign(legacyScroll) : 0f;
            frame.NextViewPressed = Input.GetKeyDown(KeyCode.Tab);
            frame.BackPressed = Input.GetKeyDown(KeyCode.Escape);
            frame.DebugTogglePressed = Input.GetKeyDown(KeyCode.F1);
            if (Input.GetKeyDown(KeyCode.Alpha1)) frame.ViewSelected = 0;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) frame.ViewSelected = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) frame.ViewSelected = 2;
            else if (Input.GetKeyDown(KeyCode.Alpha4)) frame.ViewSelected = 3;
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
