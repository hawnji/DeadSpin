using System.Collections.Generic;
using UnityEngine;

namespace DeadSpin.Runtime.UI
{
    /// <summary>
    /// IMGUI does not stop clicks from reaching the 3D table, so HUD panels register their screen
    /// rectangles here every frame and the interactor ignores the pointer while it is over one.
    /// Rectangles expire after a frame, so hidden panels stop blocking automatically.
    /// </summary>
    public static class UiPointerBlocker
    {
        private struct Entry
        {
            public Rect ScreenRect;
            public int Frame;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>Registers a rectangle in scaled GUI space (origin top-left) for this frame.</summary>
        public static void Block(Rect guiRect, float scale)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            int frame = Time.frameCount;
            Entries.RemoveAll(e => e.Frame < frame - 1);
            var screen = new Rect(guiRect.x * scale, Screen.height - guiRect.yMax * scale, guiRect.width * scale, guiRect.height * scale);
            Entries.Add(new Entry { ScreenRect = screen, Frame = frame });
        }

        /// <summary>True when the pointer (screen pixels, origin bottom-left) is over a HUD panel drawn this or last frame.</summary>
        public static bool IsBlocked(Vector2 pointer)
        {
            int oldest = Time.frameCount - 1;
            foreach (Entry entry in Entries)
            {
                if (entry.Frame >= oldest && entry.ScreenRect.Contains(pointer)) return true;
            }
            return false;
        }
    }
}
