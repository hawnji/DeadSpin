using System;

namespace DeadSpin.Core.View
{
    /// <summary>
    /// Engine-free state for the top-down table camera: which view preset is active, the zoom
    /// level and the pan offset, all clamped to designer limits. The Unity camera rig turns this
    /// state into a pose, so the navigation rules can be unit-tested.
    /// </summary>
    public sealed class TableViewNavigator
    {
        private readonly bool[] _panAllowed;

        /// <summary>Number of view presets.</summary>
        public int ViewCount { get; }

        public int CurrentView { get; private set; }

        /// <summary>Distance multiplier: 1 = the preset's distance, lower = closer.</summary>
        public float Zoom { get; private set; } = 1f;

        public float MinZoom { get; }
        public float MaxZoom { get; }

        /// <summary>Pan offset across the table (metres, table-local X).</summary>
        public float PanX { get; private set; }

        /// <summary>Pan offset along the table (metres, table-local Z).</summary>
        public float PanZ { get; private set; }

        public float PanLimitX { get; }
        public float PanLimitZ { get; }

        /// <summary>Raised with the new index whenever the active view changes.</summary>
        public event Action<int> ViewChanged;

        /// <param name="panAllowed">One entry per view; false locks pan and zoom (e.g. a fixed look at the Game Master).</param>
        public TableViewNavigator(bool[] panAllowed, float minZoom, float maxZoom, float panLimitX, float panLimitZ)
        {
            if (panAllowed == null || panAllowed.Length == 0) throw new ArgumentException("At least one view is required.", nameof(panAllowed));
            if (minZoom <= 0f || maxZoom < minZoom) throw new ArgumentOutOfRangeException(nameof(minZoom), "Zoom range must be positive and ordered.");
            if (minZoom > 1f || maxZoom < 1f) throw new ArgumentOutOfRangeException(nameof(minZoom), "Zoom range must include 1 (the preset distance).");
            if (panLimitX < 0f || panLimitZ < 0f) throw new ArgumentOutOfRangeException(nameof(panLimitX), "Pan limits cannot be negative.");

            _panAllowed = (bool[])panAllowed.Clone();
            ViewCount = _panAllowed.Length;
            MinZoom = minZoom;
            MaxZoom = maxZoom;
            PanLimitX = panLimitX;
            PanLimitZ = panLimitZ;
        }

        public bool CanPanCurrentView => _panAllowed[CurrentView];

        /// <summary>Switches to a view and resets zoom and pan. Returns false for an invalid index.</summary>
        public bool SelectView(int index)
        {
            if (index < 0 || index >= ViewCount) return false;
            bool changed = index != CurrentView;
            CurrentView = index;
            ResetFraming();
            if (changed) ViewChanged?.Invoke(CurrentView);
            return true;
        }

        public void NextView()
        {
            SelectView((CurrentView + 1) % ViewCount);
        }

        public void PreviousView()
        {
            SelectView((CurrentView - 1 + ViewCount) % ViewCount);
        }

        /// <summary>Changes zoom by a number of steps (positive = closer). Ignored on locked views.</summary>
        public void ZoomSteps(float steps, float stepSize)
        {
            if (!CanPanCurrentView || steps == 0f) return;
            Zoom = Clamp(Zoom * (float)Math.Pow(1f - stepSize, steps), MinZoom, MaxZoom);
        }

        /// <summary>Moves the focus point by a table-local offset. Ignored on locked views.</summary>
        public void Pan(float deltaX, float deltaZ)
        {
            if (!CanPanCurrentView) return;
            PanX = Clamp(PanX + deltaX, -PanLimitX, PanLimitX);
            PanZ = Clamp(PanZ + deltaZ, -PanLimitZ, PanLimitZ);
        }

        public void ResetFraming()
        {
            Zoom = 1f;
            PanX = 0f;
            PanZ = 0f;
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
