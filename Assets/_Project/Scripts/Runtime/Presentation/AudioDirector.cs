using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Campaign;
using DeadSpin.Core.Economy;
using DeadSpin.Core.GameMaster;
using DeadSpin.Core.GameState;
using DeadSpin.Core.Specials;
using DeadSpin.Core.Table;
using DeadSpin.Core.Wheel;
using DeadSpin.Runtime.Bootstrap;
using DeadSpin.Runtime.Roulette;
using UnityEngine;

namespace DeadSpin.Runtime.Presentation
{
    /// <summary>
    /// Every sound in the prototype is synthesised at startup, so the game needs no audio assets
    /// (there is deliberately no background ambience): the ball rolling and clicking into pockets,
    /// chips, wins and losses, wheel transformations, shakes and a heartbeat when time or money
    /// runs low. Replace any cue by dropping a clip into a Resources/Audio folder with the cue's
    /// name (for example Resources/Audio/chip). The Game Master's lines play recorded voice clips
    /// from Resources/Voice/&lt;VoiceClipId&gt; when present, and a low murmur otherwise.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        private const int Rate = 44100;

        [SerializeField] private GameSession session;
        [SerializeField] private RouletteTableController table;
        [SerializeField] private BallSpinAnimator ball;
        [Range(0f, 1f)] [SerializeField] private float effectsVolume = 0.8f;
        [Range(0f, 1f)] [SerializeField] private float voiceVolume = 1f;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private AudioSource _effects;
        private AudioSource _roll;
        private AudioSource _voice;
        private AudioSource _heart;

        private void Start()
        {
            if (session == null) session = GameSession.Active;
            if (table == null) table = FindAnyObjectByType<RouletteTableController>();
            if (ball == null) ball = FindAnyObjectByType<BallSpinAnimator>();

            _effects = NewSource("Effects", false);
            _roll = NewSource("BallRoll", true);
            _voice = NewSource("Voice", false);
            _heart = NewSource("Heartbeat", true);

            Build();
            _roll.clip = Clip("roll");
            _heart.clip = Clip("heartbeat");

            if (table != null)
            {
                table.BetPlaced += OnBetPlaced;
                table.SpinStarted += OnSpinStarted;
                table.RoundResolved += OnRoundResolved;
                table.WheelChanging += OnWheelChanging;
                table.TableShaken += OnTableShaken;
                table.Spoke += OnSpoke;
            }
            if (ball != null)
            {
                ball.BallBounced += OnBounce;
                ball.BallLanded += OnLanded;
            }
            if (session != null) session.RunStarted += OnRunStarted;
            if (session != null && session.Casino != null) OnRunStarted(session.Casino);
        }

        private void OnDestroy()
        {
            if (table != null)
            {
                table.BetPlaced -= OnBetPlaced;
                table.SpinStarted -= OnSpinStarted;
                table.RoundResolved -= OnRoundResolved;
                table.WheelChanging -= OnWheelChanging;
                table.TableShaken -= OnTableShaken;
                table.Spoke -= OnSpoke;
            }
            if (ball != null)
            {
                ball.BallBounced -= OnBounce;
                ball.BallLanded -= OnLanded;
            }
            if (session != null) session.RunStarted -= OnRunStarted;
            foreach (AudioClip clip in _clips.Values) if (clip != null) Destroy(clip);
        }

        private CasinoRun _casino;

        private void OnRunStarted(CasinoRun casino)
        {
            if (_casino != null) _casino.SpecialPocketsResolved -= OnSpecials;
            _casino = casino;
            _casino.SpecialPocketsResolved += OnSpecials;
        }

        private void Update()
        {
            CasinoRun casino = session != null ? session.Casino : null;
            if (casino == null) { _heart.volume = 0f; return; }

            bool lowTime = casino.Run.Round.Phase == RoundPhase.Betting && !casino.Table.Modifiers.SilentSpin && casino.Run.Round.TimeRemaining < 6f;
            bool danger = BankruptcyForecast.Worst(casino.Warnings()) >= WarningLevel.Danger;
            float target = lowTime ? 0.9f : danger ? 0.45f : 0f;
            _heart.volume = Mathf.MoveTowards(_heart.volume, target * effectsVolume, Time.deltaTime);
            _heart.pitch = lowTime ? 1.35f : 1f;
            if (_heart.volume > 0.01f && !_heart.isPlaying) _heart.Play();
            if (_heart.volume <= 0.01f && _heart.isPlaying) _heart.Stop();
        }

        // ------------------------------------------------------------------ cues

        private void OnBetPlaced(BetSelection selection) { Play("chip", 0.7f, UnityEngine.Random.Range(0.92f, 1.1f)); }

        private void OnSpinStarted()
        {
            _roll.volume = effectsVolume * 0.6f;
            _roll.pitch = 1.1f;
            _roll.Play();
        }

        private void OnBounce() { Play("click", 0.5f, UnityEngine.Random.Range(0.85f, 1.2f)); _roll.pitch = Mathf.Max(0.6f, _roll.pitch - 0.04f); }

        private void OnLanded(int pocket) { _roll.Stop(); Play("click", 0.9f, 0.8f); }

        private void OnRoundResolved(RoundResult result)
        {
            _roll.Stop();
            if (result.TotalStaked == 0) return;
            if (result.Net > 0) Play("win", 0.8f, 1f);
            else if (result.Net < 0) Play("lose", 0.8f, 1f);
        }

        private void OnSpecials(List<SpecialPocketOutcome> outcomes)
        {
            foreach (SpecialPocketOutcome outcome in outcomes)
            {
                string cue = outcome.Definition.AudioCue;
                if (!string.IsNullOrEmpty(cue) && Resources.Load<AudioClip>("Audio/" + cue) != null) Play(cue, 1f, 1f);
                else Play(outcome.Definition.Type == SpecialPocketType.Jackpot || outcome.Definition.Type == SpecialPocketType.DebtRelief ? "win" : "transform", 0.8f, 0.8f);
            }
        }

        private void OnWheelChanging(WheelChange change) { Play("transform", 0.9f, 1f); }

        private void OnTableShaken(ShakeReport report) { Play("rumble", Mathf.Clamp01(0.4f + report.StrengthPercent / 100f), 1f); }

        private void OnSpoke(SpokenLine line)
        {
            AudioClip recorded = line.Line != null ? Resources.Load<AudioClip>("Voice/" + line.Line.VoiceClipId) : null;
            _voice.Stop();
            if (recorded != null)
            {
                _voice.clip = recorded;
                _voice.pitch = 1f;
            }
            else
            {
                _voice.clip = Clip("murmur");
                _voice.pitch = MoodPitch(line.Mood);
            }
            _voice.volume = voiceVolume * (recorded != null ? 1f : 0.5f);
            _voice.Play();
        }

        private static float MoodPitch(GameMasterMood mood)
        {
            switch (mood)
            {
                case GameMasterMood.Amused: return 1.05f;
                case GameMasterMood.Angry: return 0.8f;
                case GameMasterMood.Desperate: return 1.15f;
                case GameMasterMood.Suspicious: return 0.92f;
                default: return 0.95f;
            }
        }

        private void Play(string cue, float volume, float pitch)
        {
            AudioClip clip = Clip(cue);
            if (clip == null) return;
            _effects.pitch = pitch;
            _effects.PlayOneShot(clip, volume * effectsVolume);
        }

        private AudioClip Clip(string cue)
        {
            AudioClip clip;
            return _clips.TryGetValue(cue, out clip) ? clip : null;
        }

        private AudioSource NewSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        // ------------------------------------------------------------------ synthesis

        private void Build()
        {
            var random = new System.Random(1234);
            Add("chip", 0.08f, (t, i) => Noise(random) * Env(t, 0.002f, 0.06f) * 0.6f + Math.Sin(t * 2 * Math.PI * 3200) * Env(t, 0f, 0.03f) * 0.3f);
            Add("click", 0.05f, (t, i) => (Noise(random) * 0.5 + Math.Sin(t * 2 * Math.PI * 2400) * 0.5) * Env(t, 0f, 0.025f));
            Add("roll", 1.0f, (t, i) => Noise(random) * 0.25 * (0.7 + 0.3 * Math.Sin(t * 2 * Math.PI * 9)), true);
            Add("win", 1.2f, (t, i) =>
            {
                double[] notes = { 523.25, 659.25, 783.99, 1046.5 };
                int n = Math.Min(3, (int)(t / 0.12));
                double local = t - n * 0.12;
                return Math.Sin(t * 2 * Math.PI * notes[n]) * Env(local, 0.005f, 0.7f) * 0.45;
            });
            Add("lose", 0.9f, (t, i) => Math.Sin(t * 2 * Math.PI * (90 - t * 40)) * Env(t, 0.01f, 0.8f) * 0.8 + Noise(random) * Env(t, 0f, 0.1f) * 0.2);
            Add("transform", 1.6f, (t, i) => (Math.Sin(t * 2 * Math.PI * (60 + t * 140)) * 0.5 + Math.Sin(t * 2 * Math.PI * (63 + t * 141)) * 0.3) * Env(t, 0.2f, 1.6f) + Noise(random) * 0.08 * Env(t, 0.3f, 1.5f));
            Add("rumble", 1.0f, (t, i) => Noise(random) * Env(t, 0.05f, 1.0f) * 0.5 * (0.6 + 0.4 * Math.Sin(t * 2 * Math.PI * 7)));
            Add("heartbeat", 1.0f, (t, i) => Math.Sin(t * 2 * Math.PI * 55) * (Env(t, 0.005f, 0.15f) + Env(t - 0.25, 0.005f, 0.15f) * 0.7) * 0.9, true);
            Add("murmur", 1.4f, (t, i) =>
            {
                double syllable = 0.5 + 0.5 * Math.Sin(t * 2 * Math.PI * 4.5);
                double voice = Math.Sin(t * 2 * Math.PI * 110) * 0.5 + Math.Sin(t * 2 * Math.PI * 220) * 0.25 + Math.Sin(t * 2 * Math.PI * 330) * 0.1;
                return voice * syllable * Env(t, 0.05f, 1.4f) * 0.5;
            });

            // Recorded overrides.
            foreach (string cue in new List<string>(_clips.Keys))
            {
                AudioClip recorded = Resources.Load<AudioClip>("Audio/" + cue);
                if (recorded != null) _clips[cue] = recorded;
            }
        }

        private void Add(string cue, float seconds, Func<double, int, double> sample, bool loop = false)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                double value = sample((double)i / Rate, i);
                if (loop)
                {
                    // Fade the seam so the loop does not click.
                    double edge = Math.Min(i, count - 1 - i) / (Rate * 0.02);
                    if (edge < 1) value *= edge;
                }
                data[i] = (float)Math.Max(-1, Math.Min(1, value));
            }
            AudioClip clip = AudioClip.Create(cue, count, 1, Rate, false);
            clip.SetData(data, 0);
            _clips[cue] = clip;
        }

        private static double Noise(System.Random random) { return random.NextDouble() * 2 - 1; }

        /// <summary>Attack/decay envelope: rises over <paramref name="attack"/>, then fades to zero at <paramref name="length"/>.</summary>
        private static double Env(double t, float attack, float length)
        {
            if (t < 0 || t > length) return 0;
            double rise = attack > 0 ? Math.Min(1, t / attack) : 1;
            double fall = 1 - t / length;
            return rise * fall * fall;
        }
    }
}
