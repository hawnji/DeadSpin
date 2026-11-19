using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Table;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// Draws the physical chips on the felt with pooled chip objects. Each wager is one stack that
    /// shows its amount broken into the largest chips first, capped at a readable height. Stacks
    /// glide when they appear, move or are knocked about: new stacks fly in from the player's rack
    /// (or the hand), shaken stacks slide to their new spot, displaced chips slide onto the rail,
    /// and after a spin winning stacks are paid and swept back. Purely visual: amounts live in the
    /// core <see cref="BetBook"/>.
    /// </summary>
    public sealed class ChipStackView : MonoBehaviour
    {
        public static readonly long[] Denominations = { 5, 10, 25, 100, 500, 1000 };

        private const int MaxChipsPerStack = 14;
        private const float ChipThickness = 0.0045f;
        private const float GlideSpeed = 7f;

        private sealed class Stack
        {
            public string Key;
            public long Amount;
            public ChipKind Kind;
            public bool Locked;
            public Vector3 Position;
            public Vector3 Target;
            public float Arc;
            public readonly List<Transform> Chips = new List<Transform>();
            public bool Seen;
            public bool Lifted;
        }

        private readonly Dictionary<string, Stack> _stacks = new Dictionary<string, Stack>();
        private readonly List<Transform> _pool = new List<Transform>();
        private readonly List<string> _remove = new List<string>();
        private Material[] _materials = new Material[0];
        private Material[] _kindMaterials = new Material[0];
        private float _diameter = 0.06f;

        /// <summary>Table-local point where new chips come from (the player's rack). Set by the controller.</summary>
        public Vector3 SpawnLocal { get; set; } = new Vector3(0f, 0.05f, -0.6f);

        /// <summary>Table-local start of the rail where displaced chips end up.</summary>
        public Vector3 RailLocal { get; set; } = new Vector3(-0.9f, 0.01f, -0.55f);

        /// <summary>Selection key of a stack currently held in the player's hand (drawn at the cursor, not on the felt).</summary>
        public string LiftedKey { get; set; }

        /// <param name="materials">One material per denomination, lowest first.</param>
        public void Initialise(Material[] materials, float diameter)
        {
            _materials = materials ?? new Material[0];
            _diameter = diameter;
            foreach (Transform chip in _pool) chip.localScale = new Vector3(_diameter, ChipThickness * 0.5f, _diameter);
            foreach (Stack stack in _stacks.Values)
            {
                foreach (Transform chip in stack.Chips) chip.localScale = new Vector3(_diameter, ChipThickness * 0.5f, _diameter);
            }
        }

        public void SetKindMaterials(Material[] kinds)
        {
            _kindMaterials = kinds ?? new Material[0];
        }

        /// <summary>Brings the stacks in line with the table's bets and displaced chips.</summary>
        public void Sync(TableGame game, BettingLayoutView layout)
        {
            foreach (Stack s in _stacks.Values) s.Seen = false;

            foreach (Wager wager in game.Bets.Wagers)
            {
                Stack stack = GetOrCreate(wager.Selection.Key, SpawnLocal);
                stack.Seen = true;
                stack.Target = layout.StackLocalPosition(wager.Selection);
                stack.Lifted = wager.Selection.Key == LiftedKey;
                if (stack.Amount != wager.Amount || stack.Kind != wager.Kind || stack.Locked != wager.Locked)
                {
                    stack.Amount = wager.Amount;
                    stack.Kind = wager.Kind;
                    stack.Locked = wager.Locked;
                    Rebuild(stack);
                }
            }

            int railIndex = 0;
            foreach (DisplacedChips loose in game.Displaced)
            {
                string key = "rail:" + railIndex;
                Vector3 origin = layout.StackLocalPosition(loose.From);
                Stack stack = GetOrCreate(key, origin);
                stack.Seen = true;
                stack.Target = RailLocal + new Vector3(railIndex * _diameter * 1.3f, 0f, 0f);
                if (stack.Amount != loose.Amount || stack.Kind != loose.Kind)
                {
                    stack.Amount = loose.Amount;
                    stack.Kind = loose.Kind;
                    Rebuild(stack);
                }
                railIndex++;
            }

            _remove.Clear();
            foreach (Stack s in _stacks.Values) if (!s.Seen) _remove.Add(s.Key);
            foreach (string key in _remove) Release(key);
        }

        /// <summary>Moves a stack's current position (not its target) so it visibly slides from <paramref name="fromLocal"/>.</summary>
        public void SlideFrom(string key, Vector3 fromLocal)
        {
            Stack stack;
            if (_stacks.TryGetValue(key, out stack))
            {
                stack.Position = fromLocal;
                stack.Arc = 0.05f;
            }
        }

        /// <summary>Starts a stack at a position (e.g. where the hand dropped it) so it settles from there.</summary>
        public void Pretend(string key, Vector3 fromLocal)
        {
            _pendingSpawn[key] = fromLocal;
        }

        private readonly Dictionary<string, Vector3> _pendingSpawn = new Dictionary<string, Vector3>();

        private Stack GetOrCreate(string key, Vector3 spawn)
        {
            Stack stack;
            if (_stacks.TryGetValue(key, out stack)) return stack;
            Vector3 from;
            if (_pendingSpawn.TryGetValue(key, out from))
            {
                spawn = from;
                _pendingSpawn.Remove(key);
            }
            stack = new Stack { Key = key, Position = spawn, Target = spawn, Arc = 0.08f };
            _stacks[key] = stack;
            return stack;
        }

        private void Rebuild(Stack stack)
        {
            foreach (Transform chip in stack.Chips) Return(chip);
            stack.Chips.Clear();

            long remaining = stack.Amount;
            int placed = 0;
            for (int d = Denominations.Length - 1; d >= 0 && placed < MaxChipsPerStack; d--)
            {
                while (remaining >= Denominations[d] && placed < MaxChipsPerStack)
                {
                    remaining -= Denominations[d];
                    Transform chip = Take();
                    Renderer renderer = chip.GetComponent<Renderer>();
                    Material material = d < _materials.Length ? _materials[d] : null;
                    // Special chips show their kind on the top chip so the player can tell them apart.
                    if (stack.Kind != ChipKind.Standard && placed == 0)
                    {
                        int k = (int)stack.Kind;
                        if (k < _kindMaterials.Length && _kindMaterials[k] != null) material = _kindMaterials[k];
                    }
                    if (material != null) renderer.sharedMaterial = material;
                    stack.Chips.Add(chip);
                    placed++;
                }
            }
            // The top chip is the special one: reverse so it ends up on top.
            if (stack.Kind != ChipKind.Standard && stack.Chips.Count > 1)
            {
                Transform first = stack.Chips[0];
                stack.Chips.RemoveAt(0);
                stack.Chips.Add(first);
            }
            Layout(stack);
        }

        private void Layout(Stack stack)
        {
            for (int i = 0; i < stack.Chips.Count; i++)
            {
                Transform chip = stack.Chips[i];
                chip.localPosition = stack.Position + Vector3.up * (ChipThickness * (i + 0.5f) + stack.Arc);
                // Slight jitter so stacks look hand-placed (deterministic per height).
                chip.localRotation = Quaternion.Euler(0f, i * 37f, 0f);
                chip.localPosition += new Vector3(Mathf.Sin(i * 2.3f), 0f, Mathf.Cos(i * 1.7f)) * 0.0012f;
                chip.gameObject.SetActive(!stack.Lifted);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            foreach (Stack stack in _stacks.Values)
            {
                bool moving = (stack.Position - stack.Target).sqrMagnitude > 1e-8f || stack.Arc > 0f;
                if (!moving) continue;
                stack.Position = Vector3.Lerp(stack.Position, stack.Target, 1f - Mathf.Exp(-GlideSpeed * dt));
                if ((stack.Position - stack.Target).sqrMagnitude < 1e-7f) stack.Position = stack.Target;
                // Chips hop slightly while travelling and settle onto the felt.
                stack.Arc = Mathf.MoveTowards(stack.Arc, 0f, dt * 0.25f);
                Layout(stack);
            }
        }

        private void Release(string key)
        {
            Stack stack;
            if (!_stacks.TryGetValue(key, out stack)) return;
            foreach (Transform chip in stack.Chips) Return(chip);
            _stacks.Remove(key);
        }

        private Transform Take()
        {
            Transform chip;
            if (_pool.Count > 0)
            {
                chip = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
            }
            else
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Chip";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                chip = go.transform;
            }
            chip.localScale = new Vector3(_diameter, ChipThickness * 0.5f, _diameter);
            chip.gameObject.SetActive(true);
            return chip;
        }

        private void Return(Transform chip)
        {
            if (chip == null) return;
            chip.gameObject.SetActive(false);
            _pool.Add(chip);
        }

        /// <summary>Builds a free-standing chip stack (the hand at the cursor, the rack). Returns the chips created.</summary>
        public static void BuildLooseStack(Transform parent, List<Transform> chips, long amount, ChipKind kind, Material[] materials, Material[] kindMaterials, float diameter)
        {
            foreach (Transform t in chips) if (t != null) Destroy(t.gameObject);
            chips.Clear();
            long remaining = amount;
            int placed = 0;
            for (int d = Denominations.Length - 1; d >= 0 && placed < MaxChipsPerStack; d--)
            {
                while (remaining >= Denominations[d] && placed < MaxChipsPerStack)
                {
                    remaining -= Denominations[d];
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.name = "Chip";
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(parent, false);
                    go.transform.localScale = new Vector3(diameter, ChipThickness * 0.5f, diameter);
                    go.transform.localPosition = Vector3.up * (ChipThickness * (placed + 0.5f));
                    go.transform.localRotation = Quaternion.Euler(0f, placed * 37f, 0f);
                    Material material = d < materials.Length ? materials[d] : null;
                    int k = (int)kind;
                    if (kind != ChipKind.Standard && remaining < Denominations[d] && k < kindMaterials.Length && kindMaterials[k] != null) material = kindMaterials[k];
                    if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
                    chips.Add(go.transform);
                    placed++;
                }
            }
        }
    }
}
