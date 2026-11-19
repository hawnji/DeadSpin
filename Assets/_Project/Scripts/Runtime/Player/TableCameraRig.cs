using System;
using DeadSpin.Core.View;
using UnityEngine;

namespace DeadSpin.Runtime.Player
{
    /// <summary>
    /// Top-down camera for the roulette table. The player switches between framed views
    /// (overview, betting layout, wheel, Game Master), pans with WASD or a right/middle-drag and
    /// zooms with the scroll wheel. Every move is smoothed so the table never jumps.
    /// Navigation rules (clamping, view switching) live in <see cref="TableViewNavigator"/>.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class TableCameraRig : MonoBehaviour
    {
        [Serializable]
        public struct ViewPreset
        {
            public string Name;
            [Tooltip("Point the camera looks at, in the table's local space.")]
            public Vector3 Focus;
            [Tooltip("Degrees below horizontal. 90 = straight down.")]
            [Range(0f, 90f)] public float Pitch;
            [Tooltip("Degrees around the table. 0 = from the player's side.")]
            public float Yaw;
            [Min(0.2f)] public float Distance;
            [Range(20f, 80f)] public float FieldOfView;
            [Tooltip("Off for fixed views such as looking up at the Game Master.")]
            public bool AllowPanAndZoom;

            public ViewPreset(string name, Vector3 focus, float pitch, float yaw, float distance, float fieldOfView, bool allowPanAndZoom)
            {
                Name = name;
                Focus = focus;
                Pitch = pitch;
                Yaw = yaw;
                Distance = distance;
                FieldOfView = fieldOfView;
                AllowPanAndZoom = allowPanAndZoom;
            }
        }

        [SerializeField] private PlayerInputReader input;
        [Tooltip("The table root. Views are expressed in its local space.")]
        [SerializeField] private Transform table;
        [SerializeField] private ViewPreset[] views = DefaultViews();

        [Header("Navigation")]
        [Range(0.1f, 1f)] [SerializeField] private float minZoom = 0.55f;
        [Range(1f, 2f)] [SerializeField] private float maxZoom = 1.35f;
        [Range(0.02f, 0.3f)] [SerializeField] private float zoomStep = 0.1f;
        [Min(0f)] [SerializeField] private float panLimitX = 1.6f;
        [Min(0f)] [SerializeField] private float panLimitZ = 0.75f;
        [Tooltip("Keyboard pan speed in metres per second at the preset distance.")]
        [Min(0f)] [SerializeField] private float keyboardPanSpeed = 1.2f;
        [Tooltip("Drag pan in metres per unit of mouse movement at the preset distance.")]
        [Min(0f)] [SerializeField] private float dragPanSpeed = 0.035f;

        [Header("Smoothing (seconds)")]
        [Min(0.01f)] [SerializeField] private float moveSmoothTime = 0.22f;
        [Tooltip("Degrees the view drifts toward the cursor, for a subtle sense of depth.")]
        [Range(0f, 6f)] [SerializeField] private float cursorDrift = 1.5f;

        private Camera _camera;
        private TableViewNavigator _navigator;

        private Vector3 _focus;
        private Vector3 _focusVelocity;
        private float _pitch;
        private float _pitchVelocity;
        private float _yaw;
        private float _yawVelocity;
        private float _distance;
        private float _distanceVelocity;
        private float _fov;
        private float _fovVelocity;

        /// <summary>When false the rig holds its pose and ignores input (paused, end screen).</summary>
        public bool ControlsEnabled { get; set; } = true;

        public TableViewNavigator Navigator => _navigator;

        public string CurrentViewName => _navigator != null && views.Length > 0 ? views[_navigator.CurrentView].Name : string.Empty;

        public int ViewCount => views.Length;

        public string GetViewName(int index)
        {
            return index >= 0 && index < views.Length ? views[index].Name : string.Empty;
        }

        public void SetTable(Transform tableRoot)
        {
            table = tableRoot;
        }

        public static ViewPreset[] DefaultViews()
        {
            return new[]
            {
                new ViewPreset("Table", new Vector3(0f, 0.86f, 0.1f), 62f, 0f, 3.4f, 45f, true),
                new ViewPreset("Betting layout", new Vector3(0.5f, 0.86f, -0.03f), 76f, 0f, 2.2f, 45f, true),
                new ViewPreset("Wheel", new Vector3(-1.12f, 0.92f, 0f), 72f, 0f, 1.45f, 45f, true),
                new ViewPreset("The Game Master", new Vector3(0.2f, 1.75f, 1.6f), 14f, 0f, 2.9f, 40f, false)
            };
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (input == null) input = GetComponentInParent<PlayerInputReader>();
            if (input == null) input = FindAnyObjectByType<PlayerInputReader>();

            if (table == null || views == null || views.Length == 0)
            {
                Debug.LogError("[TableCameraRig] Needs a table transform and at least one view preset.", this);
                enabled = false;
                return;
            }

            var panAllowed = new bool[views.Length];
            for (int i = 0; i < views.Length; i++) panAllowed[i] = views[i].AllowPanAndZoom;
            _navigator = new TableViewNavigator(panAllowed, minZoom, maxZoom, panLimitX, panLimitZ);
            SnapToCurrentView();
        }

        /// <summary>Jumps to a view and resets framing (run restart).</summary>
        public void ResetToView(int index, bool snap)
        {
            if (_navigator == null) return;
            _navigator.SelectView(index);
            _navigator.ResetFraming();
            if (snap) SnapToCurrentView();
        }

        private void Update()
        {
            if (ControlsEnabled && input != null) HandleInput(input.Current, Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            ViewPreset view = views[_navigator.CurrentView];
            float dt = Time.unscaledDeltaTime;

            Vector3 targetFocus = table.TransformPoint(view.Focus + new Vector3(_navigator.PanX, 0f, _navigator.PanZ));
            float targetDistance = view.Distance * _navigator.Zoom;

            _focus = Vector3.SmoothDamp(_focus, targetFocus, ref _focusVelocity, moveSmoothTime, Mathf.Infinity, dt);
            _distance = Mathf.SmoothDamp(_distance, targetDistance, ref _distanceVelocity, moveSmoothTime, Mathf.Infinity, dt);
            _pitch = Mathf.SmoothDamp(_pitch, view.Pitch, ref _pitchVelocity, moveSmoothTime, Mathf.Infinity, dt);
            _yaw = Mathf.SmoothDampAngle(_yaw, view.Yaw + table.eulerAngles.y, ref _yawVelocity, moveSmoothTime, Mathf.Infinity, dt);
            _fov = Mathf.SmoothDamp(_fov, view.FieldOfView, ref _fovVelocity, moveSmoothTime, Mathf.Infinity, dt);

            ApplyPose();
        }

        private void HandleInput(PlayerInputFrame frame, float dt)
        {
            if (frame.ViewSelected >= 0) _navigator.SelectView(frame.ViewSelected);
            if (frame.NextViewPressed) _navigator.NextView();

            if (frame.Zoom != 0f) _navigator.ZoomSteps(frame.Zoom, zoomStep);

            // Pan speed scales with zoom so the table moves at a steady speed on screen.
            float scale = _navigator.Zoom;
            Vector2 pan = frame.Pan * keyboardPanSpeed * scale * dt;
            if (frame.DragHeld) pan -= frame.PointerDelta * dragPanSpeed * scale;
            if (pan.sqrMagnitude > 0f) _navigator.Pan(pan.x, pan.y);
        }

        private void SnapToCurrentView()
        {
            ViewPreset view = views[_navigator.CurrentView];
            _focus = table.TransformPoint(view.Focus + new Vector3(_navigator.PanX, 0f, _navigator.PanZ));
            _distance = view.Distance * _navigator.Zoom;
            _pitch = view.Pitch;
            _yaw = view.Yaw + table.eulerAngles.y;
            _fov = view.FieldOfView;
            _focusVelocity = Vector3.zero;
            _distanceVelocity = _pitchVelocity = _yawVelocity = _fovVelocity = 0f;
            ApplyPose();
        }

        private void ApplyPose()
        {
            Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 position = _focus - orbit * Vector3.forward * _distance;
            transform.SetPositionAndRotation(position, orbit * CursorDrift());
            _camera.fieldOfView = _fov;
        }

        private Quaternion CursorDrift()
        {
            if (!ControlsEnabled || input == null || cursorDrift <= 0f || Screen.width <= 0 || Screen.height <= 0) return Quaternion.identity;
            Vector2 pointer = input.Current.PointerPosition;
            float nx = Mathf.Clamp(pointer.x / Screen.width * 2f - 1f, -1f, 1f);
            float ny = Mathf.Clamp(pointer.y / Screen.height * 2f - 1f, -1f, 1f);
            return Quaternion.Euler(-ny * cursorDrift * 0.5f, nx * cursorDrift, 0f);
        }
    }
}
