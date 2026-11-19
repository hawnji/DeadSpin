using UnityEngine;

namespace DeadSpin.Runtime.Player
{
    /// <summary>One frame of sampled player input. Values are backend-independent.</summary>
    public struct PlayerInputFrame
    {
        /// <summary>WASD / arrow keys, each axis in [-1, 1]. Pans the table camera.</summary>
        public Vector2 Pan;
        /// <summary>Mouse movement this frame, scaled to roughly match the legacy "Mouse X/Y" axes.</summary>
        public Vector2 PointerDelta;
        /// <summary>Cursor position in screen pixels.</summary>
        public Vector2 PointerPosition;
        /// <summary>Scroll notches this frame: positive = towards the table.</summary>
        public float Zoom;
        /// <summary>Right or middle mouse held: drag to pan.</summary>
        public bool DragHeld;
        /// <summary>Left click: select / inspect.</summary>
        public bool InteractPressed;
        /// <summary>1-4 pressed this frame selects a view (0-based); -1 when none.</summary>
        public int ViewSelected;
        /// <summary>Tab: next view.</summary>
        public bool NextViewPressed;
        /// <summary>Esc: pause / back.</summary>
        public bool BackPressed;
        public bool DebugTogglePressed;
    }
}
