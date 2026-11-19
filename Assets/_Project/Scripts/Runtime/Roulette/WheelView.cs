using System;
using System.Collections.Generic;
using DeadSpin.Core.Wheel;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// The visible wheel. Rebuilds the rotor mesh, number labels, special-pocket markers and the
    /// optional outer ring from a <see cref="WheelConfiguration"/>, so the physical wheel always
    /// matches the logical one. Changes are animated physically: the rotor lifts, pockets slide to
    /// their new slots, clones split out of their source pocket, removed pockets collapse, new
    /// pockets emerge, colours flip, and the whole wheel changes diameter with its pocket count.
    /// The bowl, rim and ball track are static; only the rotor (and the outer ring) turn.
    /// </summary>
    public sealed class WheelView : MonoBehaviour
    {
        [Header("Materials (submesh order: floor, red, black, green, frets, closed)")]
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material redMaterial;
        [SerializeField] private Material blackMaterial;
        [SerializeField] private Material greenMaterial;
        [SerializeField] private Material fretMaterial;
        [SerializeField] private Material closedMaterial;
        [SerializeField] private Material labelMaterial;
        [SerializeField] private Material hubMaterial;
        [SerializeField] private Material bowlMaterial;
        [Tooltip("Outer ring segment materials by multiplier: x0, x0.5, x1, x2, x3+.")]
        [SerializeField] private Material[] ringMaterials = new Material[0];
        [Tooltip("Unlit-ish material tinted per special pocket type.")]
        [SerializeField] private Material markerMaterial;

        [Header("Geometry (metres, wheel-local at scale 1)")]
        [Tooltip("Radius of the groove the ball circles in before it drops.")]
        [SerializeField] private float ballTrackRadius = 0.425f;
        [SerializeField] private float ballTrackHeight = 0.082f;
        [SerializeField] private float trackInnerRadius = 0.4f;
        [SerializeField] private float trackOuterRadius = 0.45f;
        [SerializeField] private float rimOuterRadius = 0.49f;
        [SerializeField] private float rimHeight = 0.11f;
        [SerializeField] private float labelHeight = 0.034f;

        [Header("Diameter")]
        [Tooltip("Uniform scale for the smallest and largest wheels; 37 pockets is always 1.")]
        [SerializeField] private float minimumScale = 0.86f;
        [SerializeField] private float maximumScale = 1.15f;

        [Header("Ring")]
        [SerializeField] private float ringInnerRadius = 0.505f;
        [SerializeField] private float ringOuterRadius = 0.565f;
        [SerializeField] private float ringHeight = 0.014f;

        [Header("Transformation")]
        [Min(0.3f)] [SerializeField] private float transformSeconds = 2.6f;
        [SerializeField] private float rotorLift = 0.03f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly WheelMeshBuilder _builder = new WheelMeshBuilder();
        private readonly List<TextMesh> _labels = new List<TextMesh>();
        private readonly List<Renderer> _markers = new List<Renderer>();
        private readonly List<TextMesh> _markerLabels = new List<TextMesh>();
        private readonly List<TextMesh> _ringLabels = new List<TextMesh>();
        private readonly List<PocketSlot> _animSlots = new List<PocketSlot>(160);
        private readonly List<int> _animNumbers = new List<int>(160);
        private MaterialPropertyBlock _block;
        private Transform _rotor;
        private Transform _labelRoot;
        private Transform _markerRoot;
        private Transform _ring;
        private Transform _ringPointer;
        private Mesh _mesh;
        private Mesh _bowlMesh;
        private Mesh _ringMesh;
        private Renderer _bowlRenderer;
        private Renderer _rotorRenderer;
        private Vector3 _rotorBase = new Vector3(0f, 0.004f, 0f);

        private WheelChange _animating;
        private WheelDiff _diff;
        private float _animTime;
        private float _scaleFrom = 1f;
        private float _scaleTo = 1f;
        private Action _onAnimationDone;
        private float _ringVisibility;
        private float _ringVisibilityTarget;

        /// <summary>Renderers worth highlighting when the wheel is hovered.</summary>
        public Renderer[] HighlightRenderers => new[] { _bowlRenderer, _rotorRenderer };

        public WheelConfiguration Configuration { get; private set; }

        /// <summary>The spinning part. Rotation about local Y, clockwise seen from above.</summary>
        public Transform Rotor => _rotor;

        /// <summary>The outer bonus ring (rotates independently), or null before the wheel is built.</summary>
        public Transform Ring => _ring;

        public bool IsTransforming => _animating != null;

        public WheelMeshBuilder Geometry => _builder;
        public float BallTrackRadius => ballTrackRadius;
        public float BallTrackHeight => ballTrackHeight;

        /// <summary>Raised when a transformation animation finishes.</summary>
        public event Action TransformFinished;

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

        public void SetExtraMaterials(Material closed, Material[] ring, Material marker)
        {
            closedMaterial = closed;
            ringMaterials = ring ?? new Material[0];
            markerMaterial = marker;
        }

        /// <summary>Builds or rebuilds the rotor for a configuration at once (no animation). Keeps the current rotor angle.</summary>
        public void Build(WheelConfiguration configuration)
        {
            if (configuration == null)
            {
                Debug.LogError("[WheelView] Cannot build a null wheel configuration.", this);
                return;
            }
            Configuration = configuration;
            EnsureRotor();
            _animating = null;
            _builder.Build(configuration, _mesh);
            RebuildLabels(configuration);
            RebuildMarkers(configuration);
            float scale = ScaleFor(configuration.PocketCount);
            transform.localScale = Vector3.one * scale;
            _scaleFrom = _scaleTo = scale;
            _rotor.localPosition = _rotorBase;
            BuildRing(configuration.Ring);
            _ringVisibility = _ringVisibilityTarget = configuration.Ring != null ? 1f : 0f;
            ApplyRingVisibility();
        }

        /// <summary>
        /// Physically transforms the wheel from the change's "before" to its "after" configuration.
        /// The rules already use the new configuration; this only shows it. <paramref name="onDone"/>
        /// runs when the wheel has finished moving.
        /// </summary>
        public void AnimateChange(WheelChange change, Action onDone = null)
        {
            if (change == null || change.After == null)
            {
                onDone?.Invoke();
                return;
            }
            EnsureRotor();
            if (_animating != null) FinishAnimation();
            if (Configuration == null || change.Kind == WheelChangeKind.None)
            {
                Build(change.After);
                onDone?.Invoke();
                return;
            }

            _animating = change;
            _diff = change.Diff();
            _animTime = 0f;
            _scaleFrom = transform.localScale.x;
            _scaleTo = ScaleFor(change.After.PocketCount);
            _onAnimationDone = onDone;
            if (change.After.Ring != null && _ringMesh == null) BuildRing(change.After.Ring);
            if (change.After.Ring != null) BuildRing(change.After.Ring);
            _ringVisibilityTarget = change.After.Ring != null ? 1f : 0f;
            HideMarkers();
        }

        /// <summary>World position of a pocket's floor centre (follows the rotor).</summary>
        public Vector3 PocketWorldPosition(int index)
        {
            return _rotor.TransformPoint(_builder.PocketCenter(index, Configuration.PocketCount));
        }

        /// <summary>Centre angle (degrees, ring-local) of a ring segment.</summary>
        public float RingSegmentAngle(int segment)
        {
            if (Configuration == null || Configuration.Ring == null) return 0f;
            WheelRing ring = Configuration.Ring;
            float total = ring.TotalWeight;
            float start = 0f;
            for (int i = 0; i < segment; i++) start += 360f * ring.Segments[i].Weight / total;
            return start + 180f * ring.Segments[segment].Weight / total;
        }

        private float ScaleFor(int pocketCount)
        {
            if (pocketCount <= 37) return Mathf.Lerp(minimumScale, 1f, Mathf.InverseLerp(12f, 37f, pocketCount));
            return Mathf.Lerp(1f, maximumScale, Mathf.InverseLerp(37f, 100f, pocketCount));
        }

        private void Update()
        {
            if (_animating != null)
            {
                _animTime += Time.deltaTime;
                float u = Mathf.Clamp01(_animTime / transformSeconds);
                StepAnimation(u);
                if (u >= 1f) FinishAnimation();
            }

            if (!Mathf.Approximately(_ringVisibility, _ringVisibilityTarget))
            {
                _ringVisibility = Mathf.MoveTowards(_ringVisibility, _ringVisibilityTarget, Time.deltaTime / 1.2f);
                ApplyRingVisibility();
            }
        }

        private void StepAnimation(float u)
        {
            // 0-15%: rotor unlocks and lifts. 15-85%: pockets move. 85-100%: rotor drops back and locks.
            float lift = u < 0.15f ? Mathf.SmoothStep(0f, 1f, u / 0.15f) : u > 0.85f ? Mathf.SmoothStep(1f, 0f, (u - 0.85f) / 0.15f) : 1f;
            _rotor.localPosition = _rotorBase + Vector3.up * (rotorLift * lift);
            float m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.85f, u));
            transform.localScale = Vector3.one * Mathf.Lerp(_scaleFrom, _scaleTo, m);

            WheelConfiguration before = _animating.Before;
            WheelConfiguration after = _animating.After;
            int nb = before.PocketCount, na = after.PocketCount;
            float stepB = 360f / nb, stepA = 360f / na;
            _animSlots.Clear();
            _animNumbers.Clear();
            foreach (PocketMove move in _diff.Moves)
            {
                PocketSlot slot;
                int number;
                if (move.IsRemoved)
                {
                    Pocket p = before.Pockets[move.FromIndex];
                    slot = new PocketSlot { CenterAngle = WheelMeshBuilder.PocketAngle(move.FromIndex, nb), Width = stepB * (1f - m), Color = p.Color, BandScale = 1f - m, Closed = !p.IsActive };
                    number = p.Number;
                }
                else if (move.IsAdded)
                {
                    Pocket p = after.Pockets[move.ToIndex];
                    float to = WheelMeshBuilder.PocketAngle(move.ToIndex, na);
                    float from = move.SourceIndex >= 0 ? WheelMeshBuilder.PocketAngle(move.SourceIndex, nb) : to;
                    slot = new PocketSlot { CenterAngle = from + Mathf.DeltaAngle(from, to) * m, Width = stepA * m, Color = p.Color, BandScale = m, Closed = !p.IsActive };
                    number = p.Number;
                }
                else
                {
                    Pocket pb = before.Pockets[move.FromIndex];
                    Pocket pa = after.Pockets[move.ToIndex];
                    float from = WheelMeshBuilder.PocketAngle(move.FromIndex, nb);
                    float to = WheelMeshBuilder.PocketAngle(move.ToIndex, na);
                    bool changes = pb.Color != pa.Color || pb.Number != pa.Number;
                    // Pockets that change colour or number flip: the band drops to the floor and rises in its new colour.
                    float band = changes ? Mathf.Abs(Mathf.Cos(Mathf.PI * m)) : 1f;
                    bool late = m >= 0.5f;
                    slot = new PocketSlot
                    {
                        CenterAngle = from + Mathf.DeltaAngle(from, to) * m,
                        Width = Mathf.Lerp(stepB, stepA, m),
                        Color = late ? pa.Color : pb.Color,
                        BandScale = band,
                        Closed = late ? !pa.IsActive || pa.Weight == 0 : !pb.IsActive || pb.Weight == 0
                    };
                    number = late ? pa.Number : pb.Number;
                }
                _animSlots.Add(slot);
                _animNumbers.Add(number);
            }
            _builder.Build(_animSlots, _mesh);
            PlaceLabels(_animSlots, _animNumbers);
        }

        private void FinishAnimation()
        {
            WheelChange change = _animating;
            _animating = null;
            Action done = _onAnimationDone;
            _onAnimationDone = null;
            Configuration = change.After;
            _builder.Build(change.After, _mesh);
            RebuildLabels(change.After);
            RebuildMarkers(change.After);
            transform.localScale = Vector3.one * _scaleTo;
            _rotor.localPosition = _rotorBase;
            if (change.After.Ring == null) _ringVisibilityTarget = 0f;
            TransformFinished?.Invoke();
            done?.Invoke();
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
            renderer.sharedMaterials = new[] { floorMaterial, redMaterial, blackMaterial, greenMaterial, fretMaterial, closedMaterial != null ? closedMaterial : blackMaterial };
            _rotorRenderer = renderer;
            rotor.transform.localPosition = _rotorBase;

            // Centre cone and turret ride with the rotor.
            CreateHubPart("Cone", new Vector3(0f, 0.06f, 0f), new Vector3(0.38f, 0.02f, 0.38f));
            CreateHubPart("ConeTop", new Vector3(0f, 0.085f, 0f), new Vector3(0.18f, 0.02f, 0.18f));
            CreateHubPart("Turret", new Vector3(0f, 0.12f, 0f), new Vector3(0.04f, 0.035f, 0.04f));
            CreateHubPart("TurretBarX", new Vector3(0f, 0.15f, 0f), new Vector3(0.16f, 0.006f, 0.012f), PrimitiveType.Cube);
            CreateHubPart("TurretBarZ", new Vector3(0f, 0.15f, 0f), new Vector3(0.012f, 0.006f, 0.16f), PrimitiveType.Cube);

            _labelRoot = new GameObject("Numbers").transform;
            _labelRoot.SetParent(_rotor, false);
            _markerRoot = new GameObject("SpecialMarkers").transform;
            _markerRoot.SetParent(_rotor, false);
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

        private void CreateHubPart(string partName, Vector3 position, Vector3 scale, PrimitiveType type = PrimitiveType.Cylinder)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            part.transform.SetParent(_rotor, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            if (hubMaterial != null) part.GetComponent<Renderer>().sharedMaterial = hubMaterial;
        }

        // ------------------------------------------------------------------ labels

        private void RebuildLabels(WheelConfiguration configuration)
        {
            List<PocketSlot> slots = _builder.SlotsFor(configuration);
            _animNumbers.Clear();
            foreach (Pocket p in configuration.Pockets) _animNumbers.Add(p.Number);
            PlaceLabels(slots, _animNumbers);
        }

        private void PlaceLabels(IList<PocketSlot> slots, IList<int> numbers)
        {
            int count = slots.Count;
            while (_labels.Count < count)
            {
                TextMesh label = WorldTextFont.CreateFlatLabel(_labelRoot, "Number", "", Vector3.zero, 0f, labelHeight, Color.white, labelMaterial);
                _labels.Add(label);
            }

            float radius = (_builder.PocketOuterRadius + _builder.BandOuterRadius) * 0.5f;
            for (int i = 0; i < _labels.Count; i++)
            {
                TextMesh label = _labels[i];
                bool used = i < count && slots[i].Width > 0.2f;
                label.gameObject.SetActive(used);
                if (!used) continue;

                PocketSlot slot = slots[i];
                // Shrink labels on crowded wheels so neighbours never overlap.
                float arc = 2f * Mathf.PI * radius * slot.Width / 360f;
                string text = numbers[i].ToString();
                float height = Mathf.Min(labelHeight, arc * (text.Length >= 3 ? 0.62f : 0.9f));
                if (label.text != text) label.text = text;
                label.characterSize = height * 10f / label.fontSize * WorldTextFont.LabelScale;
                Transform t = label.transform;
                float top = Mathf.Lerp(_builder.PocketFloorHeight, _builder.BandHeight, Mathf.Clamp01(slot.BandScale));
                t.localPosition = WheelMeshBuilder.Polar(slot.CenterAngle, radius, top + 0.0015f);
                // Lie flat with the top of each number pointing out from the centre.
                t.localRotation = Quaternion.Euler(90f, slot.CenterAngle, 0f);
            }
        }

        // ------------------------------------------------------------------ special pockets and weights

        private static Color SpecialColor(SpecialPocketType type)
        {
            switch (type)
            {
                case SpecialPocketType.Skull: return new Color(0.85f, 0.85f, 0.8f);
                case SpecialPocketType.Jackpot: return new Color(1f, 0.82f, 0.2f);
                case SpecialPocketType.Mystery: return new Color(0.55f, 0.3f, 0.85f);
                case SpecialPocketType.Bomb: return new Color(1f, 0.35f, 0.1f);
                case SpecialPocketType.Hourglass: return new Color(0.4f, 0.75f, 1f);
                case SpecialPocketType.Tax: return new Color(0.6f, 0.6f, 0.6f);
                case SpecialPocketType.Influence: return new Color(0.3f, 1f, 0.75f);
                case SpecialPocketType.DebtRelief: return new Color(0.35f, 0.95f, 0.35f);
                case SpecialPocketType.Curse: return new Color(0.6f, 0.05f, 0.15f);
                case SpecialPocketType.GoldenTicket: return new Color(1f, 0.95f, 0.55f);
                default: return Color.white;
            }
        }

        /// <summary>Two-letter code painted in a special pocket (the built-in font has no icons).</summary>
        public static string SpecialCode(SpecialPocketType type)
        {
            switch (type)
            {
                case SpecialPocketType.Skull: return "SK";
                case SpecialPocketType.Jackpot: return "$$";
                case SpecialPocketType.Mystery: return "??";
                case SpecialPocketType.Bomb: return "BM";
                case SpecialPocketType.Hourglass: return "HG";
                case SpecialPocketType.Tax: return "TX";
                case SpecialPocketType.Influence: return "IN";
                case SpecialPocketType.DebtRelief: return "DR";
                case SpecialPocketType.Curse: return "CU";
                case SpecialPocketType.GoldenTicket: return "GT";
                default: return "";
            }
        }

        private void RebuildMarkers(WheelConfiguration configuration)
        {
            HideMarkers();
            int used = 0;
            float radius = (_builder.PocketInnerRadius + _builder.PocketOuterRadius) * 0.5f;
            int n = configuration.PocketCount;
            float arc = 2f * Mathf.PI * radius / n;
            for (int i = 0; i < n; i++)
            {
                Pocket p = configuration.Pockets[i];
                string text = null;
                Color color = Color.white;
                if (p.Special != SpecialPocketType.None)
                {
                    text = SpecialCode(p.Special);
                    color = SpecialColor(p.Special);
                }
                else if (p.IsActive && p.Weight != 1)
                {
                    text = "x" + p.Weight;
                    color = p.Weight > 1 ? new Color(0.4f, 0.8f, 1f) : new Color(0.6f, 0.6f, 0.6f);
                }
                else if (p.PayoutPercent != 100)
                {
                    text = p.PayoutPercent + "%";
                    color = new Color(0.9f, 0.7f, 0.3f);
                }
                if (text == null) continue;

                float angle = WheelMeshBuilder.PocketAngle(i, n);
                Renderer disc = MarkerAt(used);
                disc.transform.localPosition = WheelMeshBuilder.Polar(angle, radius, _builder.PocketFloorHeight + 0.002f);
                float d = Mathf.Min(0.05f, arc * 0.8f);
                disc.transform.localScale = new Vector3(d, 0.0015f, d);
                Tint(disc, color);

                TextMesh label = MarkerLabelAt(used);
                label.text = text;
                label.color = Color.black;
                label.characterSize = Mathf.Min(0.018f, arc * 0.5f) * 10f / label.fontSize * WorldTextFont.LabelScale;
                label.transform.localPosition = WheelMeshBuilder.Polar(angle, radius, _builder.PocketFloorHeight + 0.004f);
                label.transform.localRotation = Quaternion.Euler(90f, angle, 0f);
                used++;
            }
        }

        private Renderer MarkerAt(int index)
        {
            while (_markers.Count <= index)
            {
                GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Marker";
                Destroy(disc.GetComponent<Collider>());
                disc.transform.SetParent(_markerRoot, false);
                Renderer r = disc.GetComponent<Renderer>();
                if (markerMaterial != null) r.sharedMaterial = markerMaterial;
                _markers.Add(r);
            }
            _markers[index].gameObject.SetActive(true);
            return _markers[index];
        }

        private TextMesh MarkerLabelAt(int index)
        {
            while (_markerLabels.Count <= index)
            {
                _markerLabels.Add(WorldTextFont.CreateFlatLabel(_markerRoot, "MarkerText", "", Vector3.zero, 0f, 0.014f, Color.black, labelMaterial));
            }
            _markerLabels[index].gameObject.SetActive(true);
            return _markerLabels[index];
        }

        private void HideMarkers()
        {
            foreach (Renderer r in _markers) if (r != null) r.gameObject.SetActive(false);
            foreach (TextMesh t in _markerLabels) if (t != null) t.gameObject.SetActive(false);
        }

        private void Tint(Renderer renderer, Color color)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_block);
        }

        // ------------------------------------------------------------------ outer ring

        private void BuildRing(WheelRing ring)
        {
            if (_ring == null)
            {
                var go = new GameObject("OuterRing");
                go.transform.SetParent(transform, false);
                _ring = go.transform;
                _ringMesh = new Mesh { name = "OuterRing" };
                go.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
                var renderer = go.AddComponent<MeshRenderer>();
                var mats = new Material[6];
                for (int i = 0; i < 5; i++) mats[i] = i < ringMaterials.Length && ringMaterials[i] != null ? ringMaterials[i] : floorMaterial;
                mats[5] = fretMaterial;
                renderer.sharedMaterials = mats;

                GameObject pointer = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pointer.name = "RingPointer";
                Destroy(pointer.GetComponent<Collider>());
                pointer.transform.SetParent(transform, false);
                pointer.transform.localPosition = new Vector3(0f, ringHeight + 0.012f, ringOuterRadius + 0.004f);
                pointer.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                pointer.transform.localScale = new Vector3(0.022f, 0.012f, 0.022f);
                pointer.GetComponent<Renderer>().sharedMaterial = fretMaterial;
                _ringPointer = pointer.transform;
            }
            foreach (TextMesh t in _ringLabels) if (t != null) t.gameObject.SetActive(false);
            if (ring == null) return;

            var widths = new List<float>();
            var subs = new List<int>();
            foreach (RingSegment s in ring.Segments)
            {
                widths.Add(s.Weight);
                subs.Add(s.MultiplierPercent <= 0 ? 0 : s.MultiplierPercent < 100 ? 1 : s.MultiplierPercent == 100 ? 2 : s.MultiplierPercent <= 200 ? 3 : 4);
            }
            _builder.BuildRing(_ringMesh, ringInnerRadius, ringOuterRadius, ringHeight, widths, subs, 6);

            float angle = 0f;
            for (int i = 0; i < ring.Segments.Count; i++)
            {
                float sweep = 360f * ring.Segments[i].Weight / ring.TotalWeight;
                while (_ringLabels.Count <= i) _ringLabels.Add(WorldTextFont.CreateFlatLabel(_ring, "RingLabel", "", Vector3.zero, 0f, 0.02f, Color.white, labelMaterial));
                TextMesh label = _ringLabels[i];
                label.gameObject.SetActive(true);
                label.text = ring.Segments[i].Label;
                float mid = angle + sweep * 0.5f;
                label.transform.localPosition = WheelMeshBuilder.Polar(mid, (ringInnerRadius + ringOuterRadius) * 0.5f, ringHeight + 0.0015f);
                label.transform.localRotation = Quaternion.Euler(90f, mid, 0f);
                angle += sweep;
            }
        }

        private void ApplyRingVisibility()
        {
            if (_ring == null) return;
            bool visible = _ringVisibility > 0.001f;
            _ring.gameObject.SetActive(visible);
            if (_ringPointer != null) _ringPointer.gameObject.SetActive(visible);
            // The ring rises out of the table as it activates.
            float v = Mathf.SmoothStep(0f, 1f, _ringVisibility);
            _ring.localPosition = new Vector3(0f, Mathf.Lerp(-0.03f, 0f, v), 0f);
            _ring.localScale = new Vector3(1f, Mathf.Max(0.01f, v), 1f);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_bowlMesh != null) Destroy(_bowlMesh);
            if (_ringMesh != null) Destroy(_ringMesh);
        }
    }
}
