using DeadSpin.Core.Campaign;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Progression;
using DeadSpin.Core.Table;
using DeadSpin.Runtime.Bootstrap;
using DeadSpin.Runtime.Roulette;
using UnityEngine;

namespace DeadSpin.Runtime.Presentation
{
    /// <summary>
    /// Brings the Game Master's figure across the table to life without any animation assets:
    /// breathing, a ring tapping on the rail, a nod when he speaks, eyes coloured by his mood, a
    /// lean toward the wheel while it spins, and growing larger and closer as the final table nears.
    /// Finds its parts by name (Head, Eye_L, Eye_R, Hand_L, Hand_R, Body) under its own transform.
    /// </summary>
    public sealed class GameMasterView : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private GameSession session;
        [SerializeField] private RouletteTableController table;
        [Min(0f)] [SerializeField] private float breathAmount = 0.015f;
        [Min(0f)] [SerializeField] private float tapSpeed = 1.6f;

        private Transform _head;
        private Transform _body;
        private Transform _handLeft;
        private Transform _handRight;
        private Renderer[] _eyes = new Renderer[0];
        private Vector3 _headBase;
        private Vector3 _bodyScale;
        private Vector3 _handLeftBase;
        private Vector3 _handRightBase;
        private Vector3 _rootBase;
        private Vector3 _rootScale;
        private MaterialPropertyBlock _block;
        private float _nodTime = -10f;
        private float _clenchTime = -10f;
        private Color _eyeColour = new Color(1f, 0.15f, 0.08f);

        private void Awake()
        {
            _head = Find("Head");
            _body = Find("Body");
            _handLeft = Find("Hand_L");
            _handRight = Find("Hand_R");
            var eyes = new System.Collections.Generic.List<Renderer>();
            foreach (string name in new[] { "Eye_L", "Eye_R" })
            {
                Transform eye = Find(name);
                if (eye != null && eye.GetComponent<Renderer>() != null) eyes.Add(eye.GetComponent<Renderer>());
            }
            _eyes = eyes.ToArray();
            if (_head != null) _headBase = _head.localPosition;
            if (_body != null) _bodyScale = _body.localScale;
            if (_handLeft != null) _handLeftBase = _handLeft.localPosition;
            if (_handRight != null) _handRightBase = _handRight.localPosition;
            _rootBase = transform.localPosition;
            _rootScale = transform.localScale;
            _block = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (session == null) session = GameSession.Active;
            if (table == null) table = FindFirstObjectByType<RouletteTableController>();
            if (table != null)
            {
                table.Spoke += OnSpoke;
                table.RoundResolved += OnRoundResolved;
            }
        }

        private void OnDestroy()
        {
            if (table == null) return;
            table.Spoke -= OnSpoke;
            table.RoundResolved -= OnRoundResolved;
        }

        private void OnSpoke(SpokenLine line) { _nodTime = Time.time; }

        private void OnRoundResolved(RoundResult result)
        {
            if (result.Net > 0) _clenchTime = Time.time;
        }

        private Transform Find(string name)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true)) if (child.name == name) return child;
            return null;
        }

        private void Update()
        {
            CasinoRun casino = session != null ? session.Casino : null;
            float t = Time.time;

            // Breathing.
            if (_body != null) _body.localScale = new Vector3(_bodyScale.x * (1f + Mathf.Sin(t * 1.3f) * breathAmount), _bodyScale.y, _bodyScale.z);

            // Nod when he speaks; lean toward the wheel while it spins.
            float nod = Mathf.Exp(-(t - _nodTime) * 4f) * Mathf.Sin((t - _nodTime) * 14f) * 0.03f;
            float lean = casino != null && (casino.Run.Round.Phase == RoundPhase.Spinning || casino.Run.Round.Phase == RoundPhase.BettingClosed) ? 0.08f : 0f;
            if (_head != null) _head.localPosition = Vector3.Lerp(_head.localPosition, _headBase + new Vector3(0f, nod, lean), Time.deltaTime * 6f);

            // The ring hand taps; both hands clench briefly after the player wins.
            float clench = Mathf.Clamp01(1f - (t - _clenchTime) / 1.2f);
            float tap = Mathf.Max(0f, Mathf.Sin(t * tapSpeed * Mathf.PI)) * 0.02f * (1f - clench);
            if (_handRight != null) _handRight.localPosition = _handRightBase + new Vector3(-0.03f * clench, tap, 0f);
            if (_handLeft != null) _handLeft.localPosition = _handLeftBase + new Vector3(0.03f * clench, 0f, 0f);

            if (casino == null) return;

            // He grows and draws closer as the night goes on.
            float progress = Mathf.Clamp01((casino.Stage - 1) / 4f);
            if (casino.CurrentFinalPhase != FinalPhase.None) progress = 1.15f;
            transform.localScale = Vector3.Lerp(transform.localScale, _rootScale * (1f + progress * 0.12f), Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, _rootBase + transform.forward * (progress * 0.15f), Time.deltaTime);

            // Eyes follow his mood, flaring when he speaks.
            Color target = MoodEyes(casino.Mood) * (1f + Mathf.Exp(-(t - _nodTime) * 3f) * 2f);
            _eyeColour = Color.Lerp(_eyeColour, target, Time.deltaTime * 4f);
            foreach (Renderer eye in _eyes)
            {
                eye.GetPropertyBlock(_block);
                _block.SetColor(EmissionColorId, _eyeColour * 4f);
                eye.SetPropertyBlock(_block);
            }
        }

        private static Color MoodEyes(GameMasterMood mood)
        {
            switch (mood)
            {
                case GameMasterMood.Amused: return new Color(1f, 0.55f, 0.1f);
                case GameMasterMood.Irritated: return new Color(1f, 0.3f, 0.05f);
                case GameMasterMood.Suspicious: return new Color(0.5f, 0.6f, 1f);
                case GameMasterMood.Angry: return new Color(1f, 0.02f, 0.02f);
                case GameMasterMood.Desperate: return new Color(0.9f, 0.1f, 0.9f);
                default: return new Color(1f, 0.15f, 0.08f);
            }
        }
    }
}
