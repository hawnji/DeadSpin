using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// Builds the betting layout on the felt from <see cref="StandardLayout"/>: number boxes, outside
    /// boxes and invisible border spots for splits, streets, corners and six lines, all positioned
    /// from the same layout units the rules use. Draws chip stacks for the current bets.
    /// </summary>
    public sealed class BettingLayoutView : MonoBehaviour
    {
        [Tooltip("Size of one number box in metres.")]
        [Min(0.05f)] [SerializeField] private float unitSize = 0.15f;
        [Tooltip("Gold line width between boxes, metres.")]
        [Min(0f)] [SerializeField] private float lineWidth = 0.006f;
        [SerializeField] private Color highlightColor = new Color(0.6f, 0.45f, 0.12f);

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

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly Dictionary<int, Renderer> _numberCells = new Dictionary<int, Renderer>();
        private readonly Dictionary<string, Renderer> _outsideCells = new Dictionary<string, Renderer>();
        private readonly Dictionary<string, Renderer> _markers = new Dictionary<string, Renderer>();
        private readonly List<Renderer> _lit = new List<Renderer>();
        private MaterialPropertyBlock _block;
        private ChipStackView _chips;
        private TableGame _game;
        private bool _built;

        /// <summary>Raised when the player clicks a spot. The controller decides whether to add or remove chips.</summary>
        public event Action<BetSelection> SpotClicked;

        /// <summary>Supplies hover text for a spot (payout, odds, chips on it). Set by the controller.</summary>
        public Func<BetSelection, string> SpotDescriber { get; set; }

        public float UnitSize => unitSize;

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

        /// <summary>Layout units to table-local position on top of the felt.</summary>
        public Vector3 LayoutToLocal(float x, float y, float height)
        {
            float cx = (StandardLayout.MinX + StandardLayout.MaxX) * 0.5f;
            float cy = (StandardLayout.MinY + StandardLayout.MaxY) * 0.5f;
            return new Vector3((x - cx) * unitSize, height, (y - cy) * unitSize);
        }

        /// <summary>Connects the view to a table game (each run creates a new one) and redraws chips.</summary>
        public void Bind(TableGame game)
        {
            if (_game != null) _game.BetsChanged -= RefreshChips;
            _game = game;
            if (!_built) Build(game.Layout);
            if (_game != null) _game.BetsChanged += RefreshChips;
            RefreshChips();
        }

        private void OnDestroy()
        {
            if (_game != null) _game.BetsChanged -= RefreshChips;
        }

        public string DescribeSpot(BetSelection selection)
        {
            return SpotDescriber != null ? SpotDescriber(selection) : selection.DisplayName;
        }

        public void HandleSpotClicked(BetSelection selection)
        {
            SpotClicked?.Invoke(selection);
        }

        /// <summary>Lights every box a selection covers (or its own box for outside bets).</summary>
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

            if (selection.IsInside)
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
                if (_outsideCells.TryGetValue(selection.Key, out cell)) Light(cell);
            }
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
                renderer.SetPropertyBlock(null);
            }
            _lit.Clear();
        }

        private void Build(StandardLayout layout)
        {
            _built = true;
            const float cellThickness = 0.003f;
            const float baseHeight = 0.0015f;

            foreach (BetSpot spot in layout.Spots)
            {
                if (spot.IsBorderSpot) BuildBorderSpot(spot, baseHeight + cellThickness);
                else BuildBox(spot, baseHeight, cellThickness);
            }

            var chipRoot = new GameObject("Chips");
            chipRoot.transform.SetParent(transform, false);
            _chips = chipRoot.AddComponent<ChipStackView>();
            _chips.Initialise(chipMaterials, unitSize * 0.42f);
        }

        private void BuildBox(BetSpot spot, float baseHeight, float thickness)
        {
            var group = new GameObject("Spot " + spot.Selection.Key);
            group.transform.SetParent(transform, false);
            group.transform.localPosition = LayoutToLocal(spot.CenterX, spot.CenterY, baseHeight);

            Vector3 size = new Vector3(spot.Width * unitSize, thickness, spot.Height * unitSize);

            // Gold plate underneath; the cell sits on top inset by the line width so gold shows as lines.
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Line";
            plate.transform.SetParent(group.transform, false);
            plate.transform.localScale = new Vector3(size.x + lineWidth, thickness * 0.5f, size.z + lineWidth);
            Destroy(plate.GetComponent<Collider>());
            plate.GetComponent<Renderer>().sharedMaterial = lineMaterial;

            GameObject cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cell.name = "Box";
            cell.transform.SetParent(group.transform, false);
            cell.transform.localPosition = new Vector3(0f, thickness * 0.5f, 0f);
            cell.transform.localScale = new Vector3(size.x - lineWidth, thickness, size.z - lineWidth);
            var renderer = cell.GetComponent<Renderer>();
            renderer.sharedMaterial = BoxMaterial(spot.Selection);

            var target = cell.AddComponent<BetSpotTarget>();
            target.Bind(this, spot.Selection);

            if (spot.Selection.Type == BetType.Straight) _numberCells[spot.Selection.Numbers[0]] = renderer;
            else _outsideCells[spot.Selection.Key] = renderer;

            // Labels: numbers read across the table; tall boxes (zero) and wide ones keep text horizontal.
            float labelHeight = spot.Selection.Type == BetType.Straight ? unitSize * 0.38f : unitSize * 0.3f;
            Color color = spot.Selection.Type == BetType.Red ? new Color(1f, 0.55f, 0.5f) : new Color(0.93f, 0.9f, 0.82f);
            WorldTextFont.CreateFlatLabel(group.transform, "Label", spot.Label, new Vector3(0f, thickness + 0.0008f, 0f), 0f, labelHeight, color, labelMaterial);
        }

        private void BuildBorderSpot(BetSpot spot, float height)
        {
            var go = new GameObject("Spot " + spot.Selection.Key);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = LayoutToLocal(spot.CenterX, spot.CenterY, height + 0.003f);
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(spot.Width * unitSize, 0.006f, spot.Height * unitSize);
            var target = go.AddComponent<BetSpotTarget>();
            target.Bind(this, spot.Selection);

            // Small gold marker shown only while hovered, so the player sees exactly which line or corner they are on.
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Marker";
            marker.transform.SetParent(go.transform, false);
            float d = Mathf.Min(spot.Width, spot.Height) * unitSize * 0.9f;
            marker.transform.localScale = new Vector3(d, 0.0008f, d);
            Destroy(marker.GetComponent<Collider>());
            var markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = markerMaterial;
            markerRenderer.enabled = false;
            _markers[spot.Selection.Key] = markerRenderer;
        }

        private Material BoxMaterial(BetSelection selection)
        {
            if (selection.Type == BetType.Straight)
            {
                int n = selection.Numbers[0];
                PocketColor color = StandardWheels.TraditionalColor(n);
                return color == PocketColor.Red ? redMaterial : color == PocketColor.Black ? blackMaterial : greenMaterial;
            }
            if (selection.Type == BetType.Red) return redMaterial;
            if (selection.Type == BetType.Black) return blackMaterial;
            return feltMaterial;
        }

        private void RefreshChips()
        {
            if (_chips == null) return;
            _chips.BeginFrame();
            if (_game != null)
            {
                foreach (KeyValuePair<BetSelection, long> bet in _game.Bets.Entries)
                {
                    BetSpot spot;
                    if (!_game.Layout.TryGetSpot(bet.Key, out spot)) continue;
                    // Stacks on boxes sit slightly off-centre so the number stays readable.
                    float offsetX = spot.IsBorderSpot ? 0f : spot.Width * 0.22f;
                    float offsetY = spot.IsBorderSpot ? 0f : -spot.Height * 0.18f;
                    Vector3 local = LayoutToLocal(spot.CenterX + offsetX, spot.CenterY + offsetY, 0.008f);
                    _chips.AddStack(local, bet.Value);
                }
            }
            _chips.EndFrame();
        }
    }
}
