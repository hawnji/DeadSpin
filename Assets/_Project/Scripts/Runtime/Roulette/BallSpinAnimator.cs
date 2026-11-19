using System;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// Animates the rotor and ball so the ball visibly settles in a pocket chosen in advance by the
    /// game rules. Nothing here decides an outcome: the path is planned backwards from the target.
    ///
    /// The ball's angle is tracked relative to the rotor. It starts fast, eases to exactly the
    /// target pocket's angle at the settle time with zero relative speed, and therefore ends locked
    /// in the pocket moving with the rotor. Bounces in the drop phase are angular and vertical
    /// wobbles whose size shrinks to zero at the settle time, so they can never change the result.
    /// </summary>
    public sealed class BallSpinAnimator : MonoBehaviour
    {
        [SerializeField] private WheelView wheel;
        [SerializeField] private Transform ball;
        [Min(0.005f)] [SerializeField] private float ballRadius = 0.011f;

        [Header("Timing (seconds)")]
        [Min(1f)] [SerializeField] private float settleTime = 6.2f;
        [Tooltip("When the ball leaves the track and starts bouncing inward.")]
        [Min(0.5f)] [SerializeField] private float dropTime = 4.3f;
        [Tooltip("Pause after settling before the result is revealed.")]
        [Min(0f)] [SerializeField] private float revealDelay = 0.6f;

        [Header("Motion")]
        [Tooltip("Rotor speed at launch, degrees per second (clockwise).")]
        [SerializeField] private float rotorLaunchSpeed = 140f;
        [Tooltip("Rotor slows with this time constant but never fully stops.")]
        [Min(0.5f)] [SerializeField] private float rotorDecay = 9f;
        [SerializeField] private float rotorIdleSpeed = 12f;
        [Tooltip("Ball speed at launch, degrees per second (counter-clockwise).")]
        [SerializeField] private float ballLaunchSpeed = 760f;
        [Range(0, 8)] [SerializeField] private int bounceCount = 4;
        [SerializeField] private float bounceHeight = 0.022f;
        [Tooltip("Largest angular wobble during the drop, degrees.")]
        [SerializeField] private float bounceScatter = 26f;

        private bool _spinning;
        private float _time;
        private float _rotorAngle;
        private float _rotorSpeed;
        private float _rotorAngleAtLaunch;

        // Ball path, relative to the rotor, in degrees.
        private float _relStart;
        private float _relTravel;
        private int _targetIndex = -1;
        private int _restingIndex = -1;
        private float _scatterSign = 1f;
        private float _scatterPhase;
        private bool _revealed;
        private Action _onSettled;

        public bool IsSpinning => _spinning;

        public void SetReferences(WheelView wheelView, Transform ballTransform)
        {
            wheel = wheelView;
            ball = ballTransform;
        }

        private void Awake()
        {
            if (wheel == null) wheel = GetComponent<WheelView>();
            if (wheel == null || ball == null)
            {
                Debug.LogError("[BallSpinAnimator] Needs a WheelView and a ball transform.", this);
                enabled = false;
                return;
            }
            _rotorSpeed = rotorIdleSpeed;
            ball.gameObject.SetActive(false);
        }

        /// <summary>
        /// Starts a spin that will land on <paramref name="targetPocketIndex"/>. <paramref name="onSettled"/>
        /// is called once the ball has visibly come to rest (plus the reveal delay).
        /// </summary>
        public void Play(int targetPocketIndex, int visualSeed, Action onSettled)
        {
            if (wheel.Configuration == null) throw new InvalidOperationException("Wheel has no configuration.");
            if (targetPocketIndex < 0 || targetPocketIndex >= wheel.Configuration.PocketCount) throw new ArgumentOutOfRangeException(nameof(targetPocketIndex));

            var random = new System.Random(visualSeed);
            _targetIndex = targetPocketIndex;
            _onSettled = onSettled;
            _time = 0f;
            _revealed = false;
            _spinning = true;
            _rotorAngleAtLaunch = _rotorAngle;
            _scatterSign = random.NextDouble() < 0.5 ? -1f : 1f;
            _scatterPhase = (float)random.NextDouble() * Mathf.PI;

            // Ball launches from a random point on the track (world angle), expressed relative to the rotor.
            float launchWorldAngle = (float)random.NextDouble() * 360f;
            _relStart = launchWorldAngle - _rotorAngle;

            float target = WheelMeshBuilder.PocketAngle(targetPocketIndex, wheel.Configuration.PocketCount);
            // Relative angular speed at launch (ball counter-clockwise, rotor clockwise).
            float launchRelativeSpeed = ballLaunchSpeed + rotorLaunchSpeed;
            // Ease-out cubic: travel = speed * T / 3. Adjust to land exactly on the target angle (mod 360).
            float nominal = launchRelativeSpeed * settleTime / 3f;
            float needed = Mathf.Repeat(_relStart - target, 360f);
            _relTravel = Mathf.Floor(nominal / 360f) * 360f + needed;

            _rotorSpeed = rotorLaunchSpeed;
            ball.gameObject.SetActive(true);
        }

        /// <summary>Abandons any spin in progress without calling its callback (run restarted).</summary>
        public void Stop()
        {
            _spinning = false;
            _onSettled = null;
            _targetIndex = -1;
        }

        /// <summary>Leaves the ball resting in a pocket (e.g. after a wheel rebuild) without a spin.</summary>
        public void RestIn(int pocketIndex)
        {
            _restingIndex = pocketIndex;
            ball.gameObject.SetActive(pocketIndex >= 0);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_spinning)
            {
                _time += dt;
                _rotorAngle = _rotorAngleAtLaunch + RotorTravel(_time);
            }
            else
            {
                _rotorSpeed = Mathf.MoveTowards(_rotorSpeed, rotorIdleSpeed, 6f * dt);
                _rotorAngle += _rotorSpeed * dt;
            }
            _rotorAngle = Mathf.Repeat(_rotorAngle, 360f);
            wheel.Rotor.localRotation = Quaternion.Euler(0f, _rotorAngle, 0f);

            if (_spinning) UpdateSpinningBall();
            else if (_restingIndex >= 0) PlaceBallInPocket(_restingIndex, 0f);
        }

        private float RotorTravel(float t)
        {
            // Exponential slow-down from the launch speed toward (but not below) the idle speed.
            float decaying = (rotorLaunchSpeed - rotorIdleSpeed) * rotorDecay * (1f - Mathf.Exp(-t / rotorDecay));
            return decaying + rotorIdleSpeed * t;
        }

        private void UpdateSpinningBall()
        {
            float u = Mathf.Clamp01(_time / settleTime);
            float eased = 1f - Mathf.Pow(1f - u, 3f);
            float rel = _relStart - _relTravel * eased;

            float pocketRadius = (wheel.Geometry.PocketInnerRadius + wheel.Geometry.PocketOuterRadius) * 0.5f;
            float floorY = wheel.Geometry.PocketFloorHeight + 0.008f + ballRadius; // rotor offset + clearance
            float trackY = wheel.BallTrackHeight + ballRadius;

            float radius;
            float height;
            if (_time < dropTime)
            {
                // Orbiting the track, drifting very slightly inward as it slows.
                float o = _time / dropTime;
                radius = wheel.BallTrackRadius - 0.01f * o * o;
                height = trackY;
            }
            else
            {
                float d = Mathf.Clamp01((_time - dropTime) / (settleTime - dropTime));
                float fade = (1f - d) * (1f - d);
                radius = Mathf.Lerp(wheel.BallTrackRadius - 0.01f, pocketRadius, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, d * 1.6f)));
                float hop = Mathf.Abs(Mathf.Sin(Mathf.PI * bounceCount * d)) * bounceHeight * fade;
                height = Mathf.Lerp(trackY, floorY, Mathf.Min(1f, d * 1.6f)) + hop;
                rel += _scatterSign * bounceScatter * Mathf.Sin(3f * Mathf.PI * d + _scatterPhase) * fade;
            }

            float worldAngle = _rotorAngle + rel;
            ball.position = wheel.transform.TransformPoint(WheelMeshBuilder.Polar(worldAngle, radius, height));
            ball.Rotate(Vector3.right, 900f * (1f - u) * Time.deltaTime, Space.Self);

            if (_time >= settleTime)
            {
                PlaceBallInPocket(_targetIndex, 0f);
                if (!_revealed && _time >= settleTime + revealDelay)
                {
                    _revealed = true;
                    _spinning = false;
                    // Hand over to idle rotation at the current speed so the rotor does not jerk.
                    _rotorSpeed = (rotorLaunchSpeed - rotorIdleSpeed) * Mathf.Exp(-_time / rotorDecay) + rotorIdleSpeed;
                    _restingIndex = _targetIndex;
                    Action callback = _onSettled;
                    _onSettled = null;
                    callback?.Invoke();
                }
            }
        }

        private void PlaceBallInPocket(int index, float lift)
        {
            if (wheel.Configuration == null || index >= wheel.Configuration.PocketCount) return;
            Vector3 local = wheel.Geometry.PocketCenter(index, wheel.Configuration.PocketCount) + Vector3.up * (0.004f + ballRadius + lift);
            ball.position = wheel.Rotor.TransformPoint(local);
        }
    }
}
