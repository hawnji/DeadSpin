using System.Collections.Generic;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// Draws chip stacks for wagers with pooled chip objects. A stack shows the amount broken down
    /// into the largest chips first (500, 100, 25, 10, 5), capped at a readable height.
    /// Purely visual: the wager amounts live in the core BetBook. Physical chips arrive in Phase 4.
    /// </summary>
    public sealed class ChipStackView : MonoBehaviour
    {
        public static readonly long[] Denominations = { 5, 10, 25, 100, 500 };

        private const int MaxChipsPerStack = 12;
        private const float ChipThickness = 0.0045f;

        private readonly List<Transform> _pool = new List<Transform>();
        private Material[] _materials = new Material[0];
        private float _diameter = 0.06f;
        private int _used;

        /// <param name="materials">One material per denomination, lowest first.</param>
        public void Initialise(Material[] materials, float diameter)
        {
            _materials = materials ?? new Material[0];
            _diameter = diameter;
        }

        public void BeginFrame()
        {
            _used = 0;
        }

        public void AddStack(Vector3 localBase, long amount)
        {
            long remaining = amount;
            int placed = 0;
            for (int d = Denominations.Length - 1; d >= 0 && placed < MaxChipsPerStack; d--)
            {
                while (remaining >= Denominations[d] && placed < MaxChipsPerStack)
                {
                    remaining -= Denominations[d];
                    Transform chip = Next();
                    chip.localPosition = localBase + Vector3.up * (ChipThickness * (placed + 0.5f));
                    // Slight jitter so stacks look hand-placed (deterministic per height).
                    chip.localRotation = Quaternion.Euler(0f, placed * 37f, 0f);
                    chip.localPosition += new Vector3(Mathf.Sin(placed * 2.3f), 0f, Mathf.Cos(placed * 1.7f)) * 0.0012f;
                    Renderer renderer = chip.GetComponent<Renderer>();
                    if (d < _materials.Length && _materials[d] != null) renderer.sharedMaterial = _materials[d];
                    placed++;
                }
            }
        }

        public void EndFrame()
        {
            for (int i = _used; i < _pool.Count; i++) _pool[i].gameObject.SetActive(false);
        }

        private Transform Next()
        {
            if (_used == _pool.Count)
            {
                GameObject chip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                chip.name = "Chip";
                Destroy(chip.GetComponent<Collider>());
                chip.transform.SetParent(transform, false);
                chip.transform.localScale = new Vector3(_diameter, ChipThickness * 0.5f, _diameter);
                _pool.Add(chip.transform);
            }
            Transform next = _pool[_used++];
            next.gameObject.SetActive(true);
            return next;
        }
    }
}
