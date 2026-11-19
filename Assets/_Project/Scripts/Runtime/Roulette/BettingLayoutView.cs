using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// Builds the betting layout on the felt from the table's <see cref="BettingLayout"/>: number
    /// boxes, outside boxes, the special-bet strip and invisible border spots for splits, streets,
    /// corners and six lines, all positioned from the same layout units the rules use. Rebuilds
    /// whenever the wheel changes size, scaling the boxes so even a 100-pocket layout fits the
    /// felt. Box colours come from the live wheel (colour reversal shows on the felt), closed
    /// sections are shaded, and chip stacks are drawn by <see cref="ChipStackView"/>.
    /// </summary>
    public sealed class BettingLayoutView : MonoBehaviour
    {
        [Tooltip("Largest size of one number box in metres.")]
        [Min(0.05f)] [SerializeField] private float maxUnitSize = 0.15f;
        [Tooltip("Felt area available for the layout, metres (x = across the table, y = toward the Game Master).")]
        [SerializeField] private Vector2 availableSize = new Vector2(2.1f, 1.02f);
        [Tooltip("Gold line width between boxes, metres.")]
        [Min(0f)] [SerializeField] private float lineWidth = 0.006f;
        [SerializeField] private Color highlightColor = new Color(0.6f, 0.45f, 0.12f);
        [SerializeField] private Color closedColor = new Color(0.02f, 0.02f, 0.02f);

        [Header("Materials")]
        [SerializeField] private Material feltMaterial;
        [SerializeField] private Material redMaterial;
        [SerializeField] private Material blackMaterial;
        [SerializeField] private Material greenMaterial;
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Material labelMaterial;
        [SerializeField] private Material markerMaterial;
        [Tooltip("Chip materials by denomination, lowest first: must match ChipStackView denominations.")]
        [SerializeField] private Material[] chipMaterials = new Material[0];
        [Tooltip("Chip materials by special kind (ChipKind order, Standard unused).")]
        [SerializeField] private Material[] chipKindMaterials = new Material[0];

        /// <summary>Special bets shown on the strip below the even-money row, in order.</summary>
        public static readonly BetType[] SpecialStrip =
        {
            BetType.Neighbor, BetType.Sequence, BetType.Repeat, BetType.Survivor, BetType.Mystery, BetType.Combination, BetType.Jackpot
        };

        private const float SpecialRowY = -2.5f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Dictionary<int, Renderer> _numberCells = new Dictionary<int, Renderer>();
        private readonly Dictionary<string, Renderer> _cellsByKey = new Dictionary<string, Renderer>();
        private readonly Dictionary<string, Renderer> _markers = new Dictionary<string, Renderer>();
        private readonly Dictionary<BetType, Renderer> _specialCells = new Dictionary<BetType, Renderer>();
        private readonly Dictionary<BetType, TextMesh> _specialLabels = new Dictionary<BetType, TextMesh>();
        private readonly List<Renderer> _lit = new List<Renderer>();
        private readonly List<Renderer> _shaded = new List<Renderer>();
        private MaterialPropertyBlock _block;
        private Transform _cellsRoot;
        private ChipStackView _chips;
        private TableGame _game;
        private BettingLayout _built;
        private float _unitSize = 0.15f;

        /// <summary>Raised when the player clicks a spot. The controller decides what to do.</summary>
        public event Action<BetSelection> SpotClicked;

        /// <summary>Raised when the player clicks a special-bet box. The controller builds the actual bet.</summary>
        public event Action<BetType> SpecialClicked;

        /// <summary>Supplies hover text for a spot (payout, odds, chips on it). Set by the controller.</summary>
        public Func<BetSelection, string> SpotDescriber { get; set; }

        /// <summary>Supplies hover text for a special-bet box. Set by the controller.</summary>
        public Func<BetType, string> SpecialDescriber { get; set; }

        public float UnitSize => _unitSize;
        public ChipStackView Chips => _chips;
        public BettingLayout Layout => _built;

        public void SetMaterials(Material felt, Material red, Material black, Material green, Material line, Material label, Material marker, Material[] chips)
        {
            feltMaterial = felt;
            redMaterial = red;
            blackMaterial = black;
            greenMaterial = green;
            lineMaterial = line;
            labelMaterial = label;
            markerMaterial = marker;
            chipMaterials = chips ?? new Material[0];
        }

        public void SetChipKindMaterials(Material[] kinds)
        {
            chipKindMaterials = kinds ?? new Material[0];
            if (_chips != null) _chips.SetKindMaterials(chipKindMaterials);
        }

        /// <summary>Layout units to table-local position on top of the felt.</summary>
        public Vector3 LayoutToLocal(float x, float y, float height)
        {
            BettingLayout layout = _built;
            float minX = layout != null ? layout.MinX : 0f, maxX = layout != null ? layout.MaxX : 14f;
            float minY = SpecialMinY, maxY = layout != null ? layout.MaxY : 3f;
            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            return new Vector3((x - cx) * _unitSize, height, (y - cy) * _unitSize);
        }

        private static float SpecialMinY => SpecialRowY - 0.5f;

        /// <summary>World position of a spot's chip stack.</summary>
        public Vector3 StackWorldPosition(BetSelection selection)
        {
            if (_chips == null) return transform.position;
            return transform.TransformPoint(StackLocalPosition(selection));
        }

        public Vector3 StackLocalPosition(BetSelection selection)
        {
            if (selection.IsSpecial) return SpecialLocal(selection.Type) + new Vector3(_unitSize * 0.3f, 0.008f, 0f);
            BetSpot spot;
            if (_game == null || !_game.Layout.TryGetSpot(selection, out spot)) return Vector3.zero;
            // Stacks on boxes sit slightly off-centre so the number stays readable.
            float offsetX = spot.IsBorderSpot ? 0f : spot.Width * 0.22f;
            float offsetY = spot.IsBorderSpot ? 0f : -spot.Height * 0.18f;
            return LayoutToLocal(spot.CenterX + offsetX, spot.CenterY + offsetY, 0.008f);
        }

        private Vector3 SpecialLocal(BetType type)
        {
            int index = Array.IndexOf(SpecialStrip, type);
            float width = (_built.MaxX - 1f) / SpecialStrip.Length;
            return LayoutToLocal(1f + width * (index + 0.5f), SpecialRowY, 0.0015f);
        }

        /// <summary>Connects the view to a table game (each run creates a new one) and redraws everything.</summary>
        public void Bind(TableGame game)
        {
            if (_game != null)
            {
                _game.BetsChanged -= RefreshChips;
                _game.ModifiersChanged -= OnModifiersChanged;
            }
            _game = game;
            if (_chips == null)
            {
                var chipRoot = new GameObject("Chips");
                chipRoot.transform.SetParent(transform, false);
                _chips = chipRoot.AddComponent<ChipStackView>();
            }
            if (_game != null)
            {
                _game.BetsChanged += RefreshChips;
                _game.ModifiersChanged += OnModifiersChanged;
            }
            Rebuild();
        }

        private void OnDestroy()
        {
            if (_game != null)
            {
                _game.BetsChanged -= RefreshChips;
                _game.ModifiersChanged -= OnModifiersChanged;
            }
        }

        /// <summary>Rebuilds boxes for the table's current layout and wheel (after a transformation).</summary>
        public void Rebuild()
        {
            if (_game == null) return;
            BettingLayout layout = _game.Layout;
            if (_built != layout)
            {
                if (_cellsRoot != null) Destroy(_cellsRoot.gameObject);
                _numberCells.Clear();
                _cellsByKey.Clear();
                _markers.Clear();
                _specialCells.Clear();
                _specialLabels.Clear();
                _lit.Clear();
                _shaded.Clear();
                _built = layout;
                float widthUnits = layout.MaxX - layout.MinX;
                float heightUnits = layout.MaxY - SpecialMinY;
                _unitSize = Mathf.Min(maxUnitSize, availableSize.x / widthUnits, availableSize.y / heightUnits);
                _cellsRoot = new GameObject("Cells").transform;
                _cellsRoot.SetParent(transform, false);
                Build(layout);
                _chips.Initialise(chipMaterials, _unitSize * 0.42f);
                _chips.SetKindMaterials(chipKindMaterials);
            }
            RecolourNumbers();
            OnModifiersChanged(_game.Modifiers);
            RefreshSpecialLabels();
            RefreshChips();
        }

        public string DescribeSpot(BetSelection selection)
        {
            return SpotDescriber != null ? SpotDescriber(selection) : selection.DisplayName;
        }

        public string DescribeSpecial(BetType type)
        {
            return SpecialDescriber != null ? SpecialDescriber(type) : type.ToString();
        }

        public void HandleSpotClicked(BetSelection selection)
        {
            SpotClicked?.Invoke(selection);
        }

        public void HandleSpecialClicked(BetType type)
        {
            SpecialClicked?.Invoke(type);
        }

        /// <summary>Lights every box a selection covers (or its own box for rule-based bets). Mystery bets light nothing.</summary>
        public void SetCoverageHighlight(BetSelection selection, bool on)
        {
            ClearHighlight();
            if (!on || selection == null) return;

            Renderer marker;
            if (_markers.TryGetValue(selection.Key, out marker))
            {
                marker.enabled = true;
                _lit.Add(marker);
            }

            if (selection.IsNumbered && !selection.IsHidden)
            {
                foreach (int n in selection.Numbers)
                {
                    Renderer cell;
                    if (_numberCells.TryGetValue(n, out cell)) Light(cell);
                }
            }
            else
            {
                Renderer cell;
                if (_cellsByKey.TryGetValue(selection.Key, out cell)) Light(cell);
            }
        }

        /// <summary>Lights the number boxes a special bet would cover (used while hovering the strip).</summary>
        public void HighlightNumbers(IEnumerable<int> numbers)
        {
            ClearHighlight();
            if (numbers == null) return;
            foreach (int n in numbers)
            {
                Renderer cell;
                if (_numberCells.TryGetValue(n, out cell)) Light(cell);
            }
        }

        public void HighlightSpecial(BetType type, bool on)
        {
            ClearHighlight();
            Renderer cell;
            if (on && _specialCells.TryGetValue(type, out cell)) Light(cell);
        }

        private void Light(Renderer renderer)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor(EmissionColorId, highlightColor);
            renderer.SetPropertyBlock(_block);
            _lit.Add(renderer);
        }

        private void ClearHighlight()
        {
            foreach (Renderer renderer in _lit)
            {
                if (renderer == null) continue;
                if (_markers.ContainsValue(renderer))
                {
                    renderer.enabled = false;
                    continue;
                }
                if (_block == null) _block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(_block);
                _block.SetColor(EmissionColorId, Color.black);
                renderer.SetPropertyBlock(_block);
            }
            _lit.Clear();
        }

        private void Build(BettingLayout layout)
        {
            const float cellThickness = 0.003f;
            const float baseHeight = 0.0015f;

            foreach (BetSpot spot in layout.Spots)
            {
                if (spot.IsBorderSpot) BuildBorderSpot(spot, baseHeight + cellThickness);
                else BuildBox(spot, baseHeight, cellThickness);
            }
            BuildSpecialStrip(layout, baseHeight, cellThickness);
        }

        private void BuildBox(BetSpot spot, float baseHeight, float thickness)
        {
            var group = new GameObject("Spot " + spot.Selection.Key);
            group.transform.SetParent(_cellsRoot, false);
            group.transform.localPosition = LayoutToLocal(spot.CenterX, spot.CenterY, baseHeight);

            Renderer renderer = CreateCell(group.transform, spot.Width, spot.Height, thickness, BoxMaterial(spot.Selection));
            var target = renderer.gameObject.AddComponent<BetSpotTarget>();
            target.Bind(this, spot.Selection);

            if (spot.Selection.Type == BetType.Straight) _numberCells[spot.Selection.Numbers[0]] = renderer;
            _cellsByKey[spot.Selection.Key] = renderer;

            // Labels: numbers read across the table; wide boxes keep text horizontal.
            float labelHeight = spot.Selection.Type == BetType.Straight ? _unitSize * (spot.Label.Length >= 3 ? 0.3f : 0.38f) : _unitSize * 0.28f;
            if (spot.Selection.Type != BetType.Straight && spot.Width < 2f) labelHeight = Mathf.Min(labelHeight, _unitSize * spot.Width * 0.11f + _unitSize * 0.12f);
            Color color = spot.Selection.Type == BetType.Red ? new Color(1f, 0.55f, 0.5f) : new Color(0.93f, 0.9f, 0.82f);
            WorldTextFont.CreateFlatLabel(group.transform, "Label", spot.Label, new Vector3(0f, thickness + 0.0008f, 0f), 0f, labelHeight, color, labelMaterial);
        }

        private Renderer CreateCell(Transform parent, float widthUnits, float heightUnits, float thickness, Material material)
        {
            Vector3 size = new Vector3(widthUnits * _unitSize, thickness, heightUnits * _unitSize);

            // Gold plate underneath; the cell sits on top inset by the line width so gold shows as lines.
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Line";
            plate.transform.SetParent(parent, false);
            plate.transform.localScale = new Vector3(size.x + lineWidth, thickness * 0.5f, size.z + lineWidth);
            Destroy(plate.GetComponent<Collider>());
            plate.GetComponent<Renderer>().sharedMaterial = lineMaterial;

            GameObject cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cell.name = "Box";
            cell.transform.SetParent(parent, false);
            cell.transform.localPosition = new Vector3(0f, thickness * 0.5f, 0f);
            cell.transform.localScale = new Vector3(Mathf.Max(0.001f, size.x - lineWidth), thickness, Mathf.Max(0.001f, size.z - lineWidth));
            var renderer = cell.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private void BuildBorderSpot(BetSpot spot, float height)
        {
            var go = new GameObject("Spot " + spot.Selection.Key);
            go.transform.SetParent(_cellsRoot, false);
            go.transform.localPosition = LayoutToLocal(spot.CenterX, spot.CenterY, height + 0.003f);
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(spot.Width * _unitSize, 0.006f, spot.Height * _unitSize);
            var target = go.AddComponent<BetSpotTarget>();
            target.Bind(this, spot.Selection);

            // Small gold marker shown only while hovered, so the player sees exactly which line or corner they are on.
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Marker";
            marker.transform.SetParent(go.transform, false);
            float d = Mathf.Min(spot.Width, spot.Height) * _unitSize * 0.9f;
            marker.transform.localScale = new Vector3(d, 0.0008f, d);
            Destroy(marker.GetComponent<Collider>());
            var markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = markerMaterial;
            markerRenderer.enabled = false;
            _markers[spot.Selection.Key] = markerRenderer;
        }

        private void BuildSpecialStrip(BettingLayout layout, float baseHeight, float thickness)
        {
            float width = (layout.MaxX - 1f) / SpecialStrip.Length;
            foreach (BetType type in SpecialStrip)
            {
                var group = new GameObject("Special " + type);
                group.transform.SetParent(_cellsRoot, false);
                group.transform.localPosition = SpecialLocal(type);
                Renderer renderer = CreateCell(group.transform, width, 1f, thickness, feltMaterial);
                var target = renderer.gameObject.AddComponent<BetSpotTarget>();
                target.BindSpecial(this, type);
                _specialCells[type] = renderer;
                TextMesh label = WorldTextFont.CreateFlatLabel(group.transform, "Label", type.ToString().ToUpperInvariant(), new Vector3(0f, thickness + 0.0008f, 0f), 0f,
                    Mathf.Min(_unitSize * 0.24f, _unitSize * width * 0.13f), new Color(0.95f, 0.82f, 0.5f), labelMaterial);
                _specialLabels[type] = label;
            }
        }

        private void RefreshSpecialLabels()
        {
            if (_game == null) return;
            foreach (KeyValuePair<BetType, TextMesh> pair in _specialLabels)
            {
                bool available = _game.Modifiers.SpecialBetsAllowed && !_game.Modifiers.ClosedBetTypes.Contains(pair.Key);
                pair.Value.color = available ? new Color(0.95f, 0.82f, 0.5f) : new Color(0.35f, 0.33f, 0.3f);
            }
        }

        private Material BoxMaterial(BetSelection selection)
        {
            if (selection.Type == BetType.Straight)
            {
                PocketColor color = _game != null ? _game.Wheel.ColorOf(selection.Numbers[0]) : StandardWheels.TraditionalColor(selection.Numbers[0]);
                return MaterialFor(color);
            }
            if (selection.Type == BetType.Red) return redMaterial;
            if (selection.Type == BetType.Black) return blackMaterial;
            return feltMaterial;
        }

        private Material MaterialFor(PocketColor color)
        {
            return color == PocketColor.Red ? redMaterial : color == PocketColor.Black ? blackMaterial : greenMaterial;
        }

        /// <summary>Number boxes follow the wheel's colours (colour reversal shows on the felt).</summary>
        private void RecolourNumbers()
        {
            if (_game == null) return;
            foreach (KeyValuePair<int, Renderer> pair in _numberCells)
            {
                if (pair.Value == null) continue;
                pair.Value.sharedMaterial = MaterialFor(_game.Wheel.ColorOf(pair.Key));
            }
        }

        private void OnModifiersChanged(TableModifiers modifiers)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            foreach (Renderer r in _shaded)
            {
                if (r == null) continue;
                r.SetPropertyBlock(null);
            }
            _shaded.Clear();
            if (_game == null) return;
            foreach (BetSpot spot in _game.Layout.Spots)
            {
                if (spot.IsBorderSpot || !modifiers.IsClosed(spot.Selection)) continue;
                Renderer cell;
                if (!_cellsByKey.TryGetValue(spot.Selection.Key, out cell)) continue;
                cell.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, closedColor);
                _block.SetColor(ColorId, closedColor);
                cell.SetPropertyBlock(_block);
                _shaded.Add(cell);
            }
            foreach (KeyValuePair<BetType, Renderer> pair in _specialCells)
            {
                bool closed = !modifiers.SpecialBetsAllowed || modifiers.ClosedBetTypes.Contains(pair.Key);
                if (!closed) continue;
                pair.Value.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, closedColor);
                _block.SetColor(ColorId, closedColor);
                pair.Value.SetPropertyBlock(_block);
                _shaded.Add(pair.Value);
            }
            transform.localRotation = modifiers.LayoutRotated ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            RefreshSpecialLabels();
        }

        private void RefreshChips()
        {
            if (_chips == null || _game == null) return;
            _chips.Sync(_game, this);
        }
    }
}
