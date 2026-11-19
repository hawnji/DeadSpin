using System.Collections.Generic;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.Progression;
using DeadSpin.Runtime.Bootstrap;
using DeadSpin.Runtime.Roulette;
using UnityEngine;

namespace DeadSpin.Runtime.Presentation
{
    /// <summary>
    /// The casino reacts to the night: lights take the stage's tint, flicker more as the stages
    /// pass, drop to a single pool of light during a blackout rule, and turn red at the final table.
    /// From the middle stages, names appear scratched into the walls; later, the player's own.
    /// All of it reads <see cref="EnvironmentState"/> from the current stage, so it is tuned as data.
    /// </summary>
    public sealed class EnvironmentDirector : MonoBehaviour
    {
        private sealed class LightState
        {
            public Light Light;
            public float Intensity;
            public Color Colour;
            public bool IsKey;
            public float FlickerUntil;
        }

        private static readonly string[] Names =
        {
            "M. OKAFOR", "J. VALE", "R. ESTEVEZ", "D. HOLM", "A. CHEN", "T. MBEKI", "S. NOVAK", "L. MARCHETTI", "K. IBRAHIM", "P. DUVAL",
            "E. LINDQVIST", "N. ROSSI", "H. TANAKA", "G. FERREIRA", "C. MORROW", "V. ADEYEMI", "O. KOWALSKI", "F. BRENNAN"
        };

        [SerializeField] private GameSession session;
        [Tooltip("Room size used to place the names on the walls.")]
        [SerializeField] private Vector2 roomSize = new Vector2(14f, 18f);
        [SerializeField] private Material textMaterial;

        private readonly List<LightState> _lights = new List<LightState>();
        private Transform _wallNames;
        private bool _namesBuilt;
        private bool _playerNameBuilt;
        private Color _ambientBase;

        public void SetTextMaterial(Material material) { textMaterial = material; }

        private void Start()
        {
            if (session == null) session = GameSession.Active;
            // Walk the scene's roots instead of FindObjectsByType, whose sort-mode overload is deprecated in Unity 6.6.
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    _lights.Add(new LightState { Light = light, Intensity = light.intensity, Colour = light.color, IsKey = light.name == "TableSpot" });
                }
            }
            _ambientBase = RenderSettings.ambientLight;
            if (session != null) session.RunStarted += OnRunStarted;
        }

        private void OnDestroy()
        {
            if (session != null) session.RunStarted -= OnRunStarted;
        }

        private void OnRunStarted(CasinoRun casino)
        {
            if (_wallNames != null) Destroy(_wallNames.gameObject);
            _wallNames = null;
            _namesBuilt = false;
            _playerNameBuilt = false;
        }

        private void Update()
        {
            CasinoRun casino = session != null ? session.Casino : null;
            EnvironmentState state = casino != null ? casino.Environment : null;
            float flicker = state != null ? state.LightFlicker : 0f;
            Color tint = state != null ? ParseHex(state.AmbientTint) : Color.white;
            bool blackout = casino != null && casino.Table.Modifiers.Blackout;
            bool final = casino != null && casino.CurrentFinalPhase != FinalPhase.None;
            float t = Time.time;

            foreach (LightState s in _lights)
            {
                if (s.Light == null) continue;
                if (!s.IsKey && flicker > 0f && t > s.FlickerUntil && Random.value < flicker * Time.deltaTime * 0.8f)
                {
                    s.FlickerUntil = t + Random.Range(0.05f, 0.35f);
                }
                float target = s.Intensity;
                if (t < s.FlickerUntil) target *= Random.Range(0.05f, 0.4f);
                if (blackout && !s.IsKey) target *= 0.08f;
                if (final && !s.IsKey) target *= 0.6f;
                Color colour = s.Colour * tint;
                if (final) colour = Color.Lerp(colour, new Color(1f, 0.25f, 0.2f), s.IsKey ? 0.25f : 0.5f);
                s.Light.intensity = Mathf.Lerp(s.Light.intensity, target, Time.deltaTime * 12f);
                s.Light.color = Color.Lerp(s.Light.color, colour, Time.deltaTime * 2f);
            }
            RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, _ambientBase * tint * (blackout ? 0.2f : 1f), Time.deltaTime * 2f);

            if (state != null)
            {
                if (state.WallMarkings && !_namesBuilt) BuildNames(casino);
                if (state.PlayerNames && !_playerNameBuilt) BuildPlayerName();
            }
        }

        private void BuildNames(CasinoRun casino)
        {
            _namesBuilt = true;
            _wallNames = new GameObject("WallNames").transform;
            _wallNames.SetParent(transform, false);
            var random = new System.Random(unchecked((int)casino.Run.Random.Seed));
            for (int i = 0; i < 40; i++)
            {
                string name = Names[random.Next(Names.Length)];
                bool left = random.Next(2) == 0;
                float x = (left ? -1f : 1f) * (roomSize.x * 0.5f - 0.08f);
                float z = -roomSize.y * 0.5f + 1f + (float)random.NextDouble() * (roomSize.y - 2f);
                float y = 1.3f + (float)random.NextDouble() * 1.8f;
                var colour = new Color(0.55f, 0.45f, 0.35f, 0.5f + (float)random.NextDouble() * 0.4f);
                AddWallText(name, new Vector3(x, y, z), left ? -90f : 90f, 0.09f + (float)random.NextDouble() * 0.05f, colour);
            }
        }

        private void BuildPlayerName()
        {
            _playerNameBuilt = true;
            if (_wallNames == null)
            {
                _wallNames = new GameObject("WallNames").transform;
                _wallNames.SetParent(transform, false);
            }
            AddWallText("YOU", new Vector3(-roomSize.x * 0.5f + 0.08f, 1.9f, 1.5f), -90f, 0.22f, new Color(0.7f, 0.1f, 0.08f, 0.9f));
            AddWallText("YOU", new Vector3(roomSize.x * 0.5f - 0.08f, 1.6f, 3.5f), 90f, 0.18f, new Color(0.7f, 0.1f, 0.08f, 0.9f));
        }

        private void AddWallText(string text, Vector3 position, float yaw, float height, Color colour)
        {
            TextMesh label = WorldTextFont.CreateFlatLabel(_wallNames, "Name_" + text, text, position, 0f, height, colour, textMaterial);
            // Stand the label up against the wall.
            label.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static Color ParseHex(string hex)
        {
            Color colour;
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out colour))
            {
                // Normalise so the brightest channel is 1: tint changes the hue, not the overall level.
                float max = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
                return max > 0f ? colour / max : Color.white;
            }
            return Color.white;
        }
    }
}
