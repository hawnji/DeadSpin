using System.Collections.Generic;
using DeadSpin.Core.Wheel;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// The visible wheel. Rebuilds the rotor mesh and number labels from a
    /// <see cref="WheelConfiguration"/> so the physical wheel always matches the logical one.
    /// The bowl, rim and ball track are static scene geometry; only the rotor turns.
    /// </summary>
    public sealed class WheelView : MonoBehaviour
    {
        [Header("Materials (submesh order: floor, red, black, green, frets)")]
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material redMaterial;
        [SerializeField] private Material blackMaterial;
        [SerializeField] private Material greenMaterial;
        [SerializeField] private Material fretMaterial;
        [SerializeField] private Material labelMaterial;
        [SerializeField] private Material hubMaterial;
        [SerializeField] private Material bowlMaterial;

        [Header("Geometry (metres, wheel-local)")]
        [Tooltip("Radius of the groove the ball circles in before it drops.")]
        [SerializeField] private float ballTrackRadius = 0.425f;
        [SerializeField] private float ballTrackHeight = 0.082f;
        [SerializeField] private float trackInnerRadius = 0.4f;
        [SerializeField] private float trackOuterRadius = 0.45f;
        [SerializeField] private float rimOuterRadius = 0.49f;
        [SerializeField] private float rimHeight = 0.11f;
        [SerializeField] private float labelHeight = 0.034f;

        private readonly WheelMeshBuilder _builder = new WheelMeshBuilder();
        private readonly List<TextMesh> _labels = new List<TextMesh>();
        private Transform _rotor;
        private Transform _labelRoot;
        private Mesh _mesh;
        private Mesh _bowlMesh;
        private Renderer _bowlRenderer;
        private Renderer _rotorRenderer;

        /// <summary>Renderers worth highlighting when the wheel is hovered.</summary>
        public Renderer[] HighlightRenderers => new[] { _bowlRenderer, _rotorRenderer };

        public WheelConfiguration Configuration { get; private set; }

        /// <summary>The spinning part. Rotation about local Y, clockwise seen from above.</summary>
        public Transform Rotor => _rotor;

        public WheelMeshBuilder Geometry => _builder;
        public float BallTrackRadius => ballTrackRadius;
        public float BallTrackHeight => ballTrackHeight;

        public void SetMaterials(Material floor, Material red, Material black, Material green, Material fret, Material label, Material hub, Material bowl)
        {
            bowlMaterial = bowl;
            floorMaterial = floor;
            redMaterial = red;
            blackMaterial = black;
            greenMaterial = green;
            fretMaterial = fret;
            labelMaterial = label;
            hubMaterial = hub;
        }

        /// <summary>Builds or rebuilds the rotor for a configuration. Keeps the current rotor angle.</summary>
        public void Build(WheelConfiguration configuration)
        {
            if (configuration == null)
            {
                Debug.LogError("[WheelView] Cannot build a null wheel configuration.", this);
                return;
            }
            Configuration = configuration;
            EnsureRotor();

            _builder.Build(configuration, _mesh);
            RebuildLabels(configuration);
        }

        /// <summary>World position of a pocket's floor centre (follows the rotor).</summary>
        public Vector3 PocketWorldPosition(int index)
        {
            return _rotor.TransformPoint(_builder.PocketCenter(index, Configuration.PocketCount));
        }

        private void EnsureRotor()
        {
            if (_rotor != null) return;
            BuildBowl();

            var rotor = new GameObject("Rotor");
            rotor.transform.SetParent(transform, false);
            _rotor = rotor.transform;

            _mesh = new Mesh { name = "WheelRotor" };
            _mesh.MarkDynamic();
            rotor.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = rotor.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { floorMaterial, redMaterial, blackMaterial, greenMaterial, fretMaterial };
            _rotorRenderer = renderer;
            rotor.transform.localPosition = new Vector3(0f, 0.004f, 0f);

            // Centre cone and turret ride with the rotor.
            CreateHubPart("Cone", new Vector3(0f, 0.06f, 0f), new Vector3(0.38f, 0.02f, 0.38f));
            CreateHubPart("ConeTop", new Vector3(0f, 0.085f, 0f), new Vector3(0.18f, 0.02f, 0.18f));
            CreateHubPart("Turret", new Vector3(0f, 0.12f, 0f), new Vector3(0.04f, 0.035f, 0.04f));
            CreateHubPart("TurretBarX", new Vector3(0f, 0.15f, 0f), new Vector3(0.16f, 0.006f, 0.012f), PrimitiveType.Cube);
            CreateHubPart("TurretBarZ", new Vector3(0f, 0.15f, 0f), new Vector3(0.012f, 0.006f, 0.16f), PrimitiveType.Cube);

            _labelRoot = new GameObject("Numbers").transform;
            _labelRoot.SetParent(_rotor, false);
        }

        private void BuildBowl()
        {
            var bowl = new GameObject("Bowl");
            bowl.transform.SetParent(transform, false);
            _bowlMesh = new Mesh { name = "WheelBowl" };
            float trackLow = ballTrackHeight - 0.01f;
            float trackHigh = ballTrackHeight + 0.008f;
            _builder.BuildBowl(_bowlMesh, trackInnerRadius, trackOuterRadius, trackLow, trackHigh, rimOuterRadius, rimHeight);
            bowl.AddComponent<MeshFilter>().sharedMesh = _bowlMesh;
            _bowlRenderer = bowl.AddComponent<MeshRenderer>();
            _bowlRenderer.sharedMaterials = new[] { bowlMaterial != null ? bowlMaterial : floorMaterial, fretMaterial };

            // One simple collider so the whole wheel can be pointed at and inspected.
            var disc = gameObject.GetComponent<BoxCollider>();
            if (disc == null) disc = gameObject.AddComponent<BoxCollider>();
            disc.size = new Vector3(rimOuterRadius * 1.6f, 0.12f, rimOuterRadius * 1.6f);
            disc.center = new Vector3(0f, 0.06f, 0f);
        }

        private void CreateHubPart(string name, Vector3 position, Vector3 scale, PrimitiveType type = PrimitiveType.Cylinder)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            part.transform.SetParent(_rotor, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            if (hubMaterial != null) part.GetComponent<Renderer>().sharedMaterial = hubMaterial;
        }

        private void RebuildLabels(WheelConfiguration configuration)
        {
            int count = configuration.PocketCount;
            // Reuse existing labels; create or hide the difference.
            while (_labels.Count < count)
            {
                TextMesh label = WorldTextFont.CreateFlatLabel(_labelRoot, "Number", "", Vector3.zero, 0f, labelHeight, Color.white, labelMaterial);
                _labels.Add(label);
            }

            // Shrink labels on crowded wheels so neighbours never overlap.
            float arc = 2f * Mathf.PI * (_builder.PocketOuterRadius + _builder.BandOuterRadius) * 0.5f / count;
            float height = Mathf.Min(labelHeight, arc * 0.9f);
            float radius = (_builder.PocketOuterRadius + _builder.BandOuterRadius) * 0.5f;

            for (int i = 0; i < _labels.Count; i++)
            {
                TextMesh label = _labels[i];
                bool used = i < count;
                label.gameObject.SetActive(used);
                if (!used) continue;

                float angle = WheelMeshBuilder.PocketAngle(i, count);
                label.text = configuration.Pockets[i].Number.ToString();
                label.characterSize = height * 10f / label.fontSize * WorldTextFont.LabelScale;
                Transform t = label.transform;
                t.localPosition = WheelMeshBuilder.Polar(angle, radius, _builder.BandHeight + 0.0015f);
                // Lie flat with the top of each number pointing out from the centre.
                t.localRotation = Quaternion.Euler(90f, angle, 0f);
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_bowlMesh != null) Destroy(_bowlMesh);
        }
    }
}
