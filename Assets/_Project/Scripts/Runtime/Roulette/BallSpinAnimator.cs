using System;
using System.Collections.Generic;
using DeadSpin.Core.Table;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// Animates the rotor, the outer ring and one or more balls so every ball visibly settles in a
    /// pocket chosen in advance by the game rules. Nothing here decides an outcome: each path is
    /// planned backwards from its target.
    ///
    /// Each ball's angle is tracked relative to the rotor. It starts fast, eases to exactly its
    /// target pocket's angle at the settle time with zero relative speed, and therefore ends locked
    /// in the pocket moving with the rotor. Bounces in the drop phase are angular and vertical
    /// wobbles whose size shrinks to zero at the settle time, so they can never change the result.
    /// The outer ring is planned the same way so its pointer stops on the chosen segment.
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
        [Tooltip("Extra settle time per additional ball, so balls land one after another.")]
        [Min(0f)] [SerializeField] private float ballStagger = 0.7f;

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
        [Tooltip("Outer ring launch speed, degrees per second (opposite to the rotor).")]
        [SerializeField] private float ringLaunchSpeed = 220f;

        private sealed class BallPath
        {
            public Transform Transform;
            public int Target = -1;
            public float RelStart;
            public float RelTravel;
            public float Settle;
            public float Drop;
            public float ScatterSign = 1f;
            public float ScatterPhase;
            public bool Landed;
        }

        private readonly List<BallPath> _balls = new List<BallPath>();
        private bool _spinning;
        private float _time;
        private float _rotorAngle;
        private float _rotorSpeed;
        private float _rotorAngleAtLaunch;
        private float _direction = 1f;
        private float _speed = 1f;
        private float _totalSettle;
        private bool _revealed;
        private Action _onSettled;

        private float _ringAngle;
        private float _ringStart;
        private float _ringTravel;
        private bool _ringSpinning;

        private readonly List<int> _restingIndices = new List<int>();

        public bool IsSpinning => _spinning;

        /// <summary>Raised each time a ball drops into its pocket (index of the ball), for audio.</summary>
        public event Action<int> BallLanded;
        /// <summary>Raised on each bounce during the drop, for audio.</summary>
        public event Action BallBounced;

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
            _balls.Add(new BallPath { Transform = ball });
        }

        /// <summary>Single-ball spin (kept for tools and tests).</summary>
        public void Play(int targetPocketIndex, int visualSeed, Action onSettled)
        {
            Play(new SpinPlan(new[] { targetPocketIndex }, -1, false, 1f), visualSeed, onSettled);
        }

        /// <summary>
        /// Starts a spin that lands every ball on its planned pocket and the ring on its planned segment.
        /// <paramref name="onSettled"/> is called once every ball has visibly come to rest (plus the reveal delay).
        /// </summary>
        public void Play(SpinPlan plan, int visualSeed, Action onSettled)
        {
            if (wheel.Configuration == null) throw new InvalidOperationException("Wheel has no configuration.");
            if (plan == null || plan.PocketIndices.Count == 0) throw new ArgumentException("Spin plan has no balls.", nameof(plan));

            var random = new System.Random(visualSeed);
            _onSettled = onSettled;
            _time = 0f;
            _revealed = false;
            _spinning = true;
            _restingIndices.Clear();
            _direction = plan.Reverse ? -1f : 1f;
            _speed = Mathf.Clamp(plan.Speed, 0.4f, 2.5f);
            _rotorAngleAtLaunch = _rotorAngle;
            _rotorSpeed = rotorLaunchSpeed * _direction;

            EnsureBalls(plan.PocketIndices.Count);
            int count = wheel.Configuration.PocketCount;
            _totalSettle = 0f;
            for (int b = 0; b < _balls.Count; b++)
            {
                BallPath path = _balls[b];
                bool used = b < plan.PocketIndices.Count;
                path.Transform.gameObject.SetActive(used);
                path.Landed = false;
                if (!used) { path.Target = -1; continue; }

                int target = plan.PocketIndices[b];
                if (target < 0 || target >= count) throw new ArgumentOutOfRangeException(nameof(plan), "Pocket index " + target + " is not on the wheel.");
                path.Target = target;
                path.Settle = (settleTime + ballStagger * b) / _speed;
                path.Drop = (dropTime + ballStagger * b) / _speed;
                path.ScatterSign = random.NextDouble() < 0.5 ? -1f : 1f;
                path.ScatterPhase = (float)random.NextDouble() * Mathf.PI;

                // Ball launches from a random point on the track (world angle), expressed relative to the rotor.
                float launchWorldAngle = (float)random.NextDouble() * 360f;
                path.RelStart = launchWorldAngle - _rotorAngle;
                float targetAngle = WheelMeshBuilder.PocketAngle(target, count);
                // Relative angular speed at launch (ball against the rotor). Ease-out cubic: travel = speed * T / 3.
                float launchRelativeSpeed = ballLaunchSpeed + rotorLaunchSpeed;
                float nominal = launchRelativeSpeed * path.Settle * _speed / 3f;
                float needed = Mathf.Repeat((path.RelStart - targetAngle) * _direction, 360f);
                path.RelTravel = (Mathf.Floor(nominal / 360f) * 360f + needed) * _direction;
                _totalSettle = Mathf.Max(_totalSettle, path.Settle);
            }

            // Outer ring: spins opposite to the rotor and stops with the chosen segment under the pointer (ring-local angle 0 at +Z).
            _ringSpinning = plan.RingSegmentIndex >= 0 && wheel.Ring != null;
            if (_ringSpinning)
            {
                float segmentAngle = wheel.RingSegmentAngle(plan.RingSegmentIndex);
                // Pointer sits at world angle 0: ring angle must end at -segmentAngle (mod 360).
                _ringStart = _ringAngle;
                float nominal = ringLaunchSpeed * _totalSettle / 3f;
                float needed = Mathf.Repeat((_ringStart + segmentAngle) * _direction, 360f);
                _ringTravel = -(Mathf.Floor(nominal / 360f) * 360f + needed) * _direction;
            }
        }

        private void EnsureBalls(int count)
        {
            while (_balls.Count < count)
            {
                Transform extra = Instantiate(ball.gameObject, ball.parent).transform;
                extra.name = "Ball " + (_balls.Count + 1);
                _balls.Add(new BallPath { Transform = extra });
            }
        }

        /// <summary>Abandons any spin in progress without calling its callback (run restarted).</summary>
        public void Stop()
        {
            _spinning = false;
            _ringSpinning = false;
            _onSettled = null;
            foreach (BallPath path in _balls) path.Target = -1;
        }

        /// <summary>Leaves the ball resting in a pocket (e.g. after a wheel rebuild) without a spin. -1 hides it.</summary>
        public void RestIn(int pocketIndex)
        {
            _restingIndices.Clear();
            if (pocketIndex >= 0) _restingIndices.Add(pocketIndex);
            for (int b = 0; b < _balls.Count; b++) _balls[b].Transform.gameObject.SetActive(b == 0 && pocketIndex >= 0);
        }

        /// <summary>Hides every ball (e.g. while the wheel transforms).</summary>
        public void HideBalls()
        {
            RestIn(-1);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_spinning)
            {
                _time += dt;
                _rotorAngle = _rotorAngleAtLaunch + RotorTravel(_time * _speed) * _direction;
                if (_ringSpinning)
                {
                    float u = Mathf.Clamp01(_time / _totalSettle);
                    _ringAngle = _ringStart + _ringTravel * (1f - Mathf.Pow(1f - u, 3f));
                }
            }
            else
            {
                _rotorSpeed = Mathf.MoveTowards(_rotorSpeed, rotorIdleSpeed * _direction, 6f * dt);
                _rotorAngle += _rotorSpeed * dt;
            }
            _rotorAngle = Mathf.Repeat(_rotorAngle, 360f);
            if (wheel.Rotor != null) wheel.Rotor.localRotation = Quaternion.Euler(0f, _rotorAngle, 0f);
            if (wheel.Ring != null) wheel.Ring.localRotation = Quaternion.Euler(0f, _ringAngle, 0f);

            if (_spinning) UpdateSpinningBalls();
            else
            {
                for (int i = 0; i < _restingIndices.Count && i < _balls.Count; i++) PlaceBallInPocket(_balls[i].Transform, _restingIndices[i], i);
            }
        }

        private float RotorTravel(float t)
        {
            // Exponential slow-down from the launch speed toward (but not below) the idle speed.
            float decaying = (rotorLaunchSpeed - rotorIdleSpeed) * rotorDecay * (1f - Mathf.Exp(-t / rotorDecay));
            return decaying + rotorIdleSpeed * t;
        }

        private void UpdateSpinningBalls()
        {
            bool allLanded = true;
            for (int b = 0; b < _balls.Count; b++)
            {
                BallPath path = _balls[b];
                if (path.Target < 0) continue;
                UpdateBall(path, b);
                if (!path.Landed) allLanded = false;
            }

            if (allLanded && !_revealed && _time >= _totalSettle + revealDelay)
            {
                _revealed = true;
                _spinning = false;
                _ringSpinning = false;
                // Hand over to idle rotation at the current speed so the rotor does not jerk.
                _rotorSpeed = ((rotorLaunchSpeed - rotorIdleSpeed) * Mathf.Exp(-_time * _speed / rotorDecay) + rotorIdleSpeed) * _direction;
                _restingIndices.Clear();
                foreach (BallPath path in _balls) if (path.Target >= 0) _restingIndices.Add(path.Target);
                Action callback = _onSettled;
                _onSettled = null;
                callback?.Invoke();
            }
        }

        private void UpdateBall(BallPath path, int index)
        {
            if (_time >= path.Settle)
            {
                PlaceBallInPocket(path.Transform, path.Target, index);
                if (!path.Landed)
                {
                    path.Landed = true;
                    BallLanded?.Invoke(index);
                }
                return;
            }

            float u = Mathf.Clamp01(_time / path.Settle);
            float eased = 1f - Mathf.Pow(1f - u, 3f);
            float rel = path.RelStart - path.RelTravel * eased;

            float pocketRadius = (wheel.Geometry.PocketInnerRadius + wheel.Geometry.PocketOuterRadius) * 0.5f;
            float floorY = wheel.Geometry.PocketFloorHeight + 0.008f + ballRadius; // rotor offset + clearance
            float trackY = wheel.BallTrackHeight + ballRadius;

            float radius;
            float height;
            if (_time < path.Drop)
            {
                // Orbiting the track, drifting very slightly inward as it slows.
                float o = _time / path.Drop;
                radius = wheel.BallTrackRadius - 0.01f * o * o;
                height = trackY;
            }
            else
            {
                float d = Mathf.Clamp01((_time - path.Drop) / (path.Settle - path.Drop));
                float fade = (1f - d) * (1f - d);
                radius = Mathf.Lerp(wheel.BallTrackRadius - 0.01f, pocketRadius, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, d * 1.6f)));
                float phase = Mathf.PI * bounceCount * d;
                float hop = Mathf.Abs(Mathf.Sin(phase)) * bounceHeight * fade;
                height = Mathf.Lerp(trackY, floorY, Mathf.Min(1f, d * 1.6f)) + hop;
                rel += path.ScatterSign * bounceScatter * Mathf.Sin(3f * Mathf.PI * d + path.ScatterPhase) * fade;

                // A bounce is when the hop crosses zero.
                float prevD = Mathf.Clamp01((_time - Time.deltaTime - path.Drop) / (path.Settle - path.Drop));
                if (Mathf.FloorToInt(bounceCount * d) != Mathf.FloorToInt(bounceCount * prevD)) BallBounced?.Invoke();
            }

            float worldAngle = _rotorAngle + rel;
            path.Transform.position = wheel.transform.TransformPoint(WheelMeshBuilder.Polar(worldAngle, radius, height));
            path.Transform.Rotate(Vector3.right, 900f * (1f - u) * Time.deltaTime, Space.Self);
        }

        private void PlaceBallInPocket(Transform ballTransform, int index, int stackOrder)
        {
            if (wheel.Configuration == null || index < 0 || index >= wheel.Configuration.PocketCount) return;
            // Two balls in the same pocket sit side by side.
            Vector3 local = wheel.Geometry.PocketCenter(index, wheel.Configuration.PocketCount) + Vector3.up * (0.004f + ballRadius);
            if (stackOrder > 0)
            {
                float angle = WheelMeshBuilder.PocketAngle(index, wheel.Configuration.PocketCount);
                local += WheelMeshBuilder.Polar(angle, 1f, 0f) * (ballRadius * 1.6f * stackOrder) * ((stackOrder % 2 == 0) ? -1f : 1f);
            }
            ballTransform.position = wheel.Rotor.TransformPoint(local);
        }
    }
}
