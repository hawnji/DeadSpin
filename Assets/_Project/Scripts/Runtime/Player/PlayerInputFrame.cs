using UnityEngine;

namespace DeadSpin.Runtime.Player
{
    /// <summary>One frame of sampled player input. Values are backend-independent.</summary>
    public struct PlayerInputFrame
    {
        /// <summary>WASD / left stick, each axis in [-1, 1].</summary>
        public Vector2 Move;
        /// <summary>Mouse delta scaled to roughly match the legacy "Mouse X/Y" axes.</summary>
        public Vector2 Look;
        /// <summary>Cursor position in screen pixels (used for pointing at the table).</summary>
        public Vector2 PointerPosition;
        public bool Sprint;
        public bool InteractPressed;
        public bool BackPressed;
        public bool DebugTogglePressed;
    }
}
