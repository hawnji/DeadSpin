using DeadSpin.Core.Betting;
using DeadSpin.Runtime.Interaction;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>
    /// A clickable betting spot on the felt (or a box on the special-bet strip). Hovering lights up
    /// every number the bet covers and shows its payout and odds; clicking hands the spot to the
    /// table controller, which places chips, drops the chips in the player's hand, or picks a
    /// stack up to move it.
    /// </summary>
    public sealed class BetSpotTarget : Interactable
    {
        private BettingLayoutView _layout;

        /// <summary>The bet this spot represents, or null for a special-bet box.</summary>
        public BetSelection Selection { get; private set; }

        /// <summary>For special-bet boxes: which special bet. Only meaningful when <see cref="IsSpecial"/>.</summary>
        public BetType SpecialType { get; private set; }

        public bool IsSpecial { get; private set; }

        public void Bind(BettingLayoutView layout, BetSelection selection)
        {
            _layout = layout;
            Selection = selection;
            IsSpecial = false;
        }

        public void BindSpecial(BettingLayoutView layout, BetType type)
        {
            _layout = layout;
            Selection = null;
            SpecialType = type;
            IsSpecial = true;
        }

        public override string Prompt
        {
            get
            {
                if (_layout == null) return base.Prompt;
                return IsSpecial ? _layout.DescribeSpecial(SpecialType) : _layout.DescribeSpot(Selection);
            }
        }

        public override bool CanInteract(Interactor interactor)
        {
            return base.CanInteract(interactor) && _layout != null && (IsSpecial || Selection != null);
        }

        public override void Interact(Interactor interactor)
        {
            if (IsSpecial) _layout.HandleSpecialClicked(SpecialType);
            else _layout.HandleSpotClicked(Selection);
        }

        protected override void OnFocusChanged(bool focused)
        {
            if (_layout == null) return;
            if (IsSpecial) _layout.HighlightSpecial(SpecialType, focused);
            else _layout.SetCoverageHighlight(Selection, focused);
        }
    }
}
