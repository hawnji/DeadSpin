using DeadSpin.Core.Betting;
using DeadSpin.Runtime.Interaction;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// A clickable betting spot on the felt. Hovering lights up every number the bet covers and
    /// shows its payout and odds; clicking places (or with Shift, removes) the selected chip.
    /// </summary>
    public sealed class BetSpotTarget : Interactable
    {
        private BettingLayoutView _layout;

        public BetSelection Selection { get; private set; }

        public void Bind(BettingLayoutView layout, BetSelection selection)
        {
            _layout = layout;
            Selection = selection;
        }

        public override string Prompt => _layout != null ? _layout.DescribeSpot(Selection) : base.Prompt;

        public override bool CanInteract(Interactor interactor)
        {
            return base.CanInteract(interactor) && _layout != null && Selection != null;
        }

        public override void Interact(Interactor interactor)
        {
            _layout.HandleSpotClicked(Selection);
        }

        protected override void OnFocusChanged(bool focused)
        {
            if (_layout != null) _layout.SetCoverageHighlight(Selection, focused);
        }
    }
}
