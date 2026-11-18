using DeadSpin.Core.GameState;
using DeadSpin.Runtime.Bootstrap;
using UnityEngine;

namespace DeadSpin.Runtime.Interaction
{
    /// <summary>
    /// The player's seat at the roulette table. Using it switches the game to AtTable mode:
    /// the camera glides to <see cref="ViewPoint"/>, movement locks and the cursor is freed so the
    /// player can point at the wheel and betting layout. Back (Esc/Tab) stands up again.
    /// </summary>
    public sealed class RouletteTableStation : Interactable
    {
        [Tooltip("Camera pose used while seated. Its forward axis should look down at the betting layout.")]
        [SerializeField] private Transform viewPoint;
        [SerializeField] private GameSession session;

        public Transform ViewPoint => viewPoint;

        public void SetViewPoint(Transform point)
        {
            viewPoint = point;
        }

        private void Start()
        {
            if (viewPoint == null)
            {
                Debug.LogError("[RouletteTableStation] No view point assigned; the seat cannot be used.", this);
            }
        }

        public override bool CanInteract(Interactor interactor)
        {
            GameSession activeSession = ResolveSession();
            return base.CanInteract(interactor)
                && viewPoint != null
                && activeSession != null
                && activeSession.Mode == GameMode.Exploring;
        }

        public override void Interact(Interactor interactor)
        {
            GameSession activeSession = ResolveSession();
            if (activeSession == null) return;
            activeSession.TrySitAtTable(this);
        }

        private GameSession ResolveSession()
        {
            if (session == null) session = GameSession.Active;
            return session;
        }
    }
}
