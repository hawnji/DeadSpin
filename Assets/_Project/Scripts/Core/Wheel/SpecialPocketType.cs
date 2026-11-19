namespace DeadSpin.Core.Wheel
{
    /// <summary>
    /// Extra behaviour a pocket triggers when the ball lands in it, on top of normal bet settlement.
    /// The effects themselves live in <c>DeadSpin.Core.Specials.SpecialPocketCatalog</c>.
    /// </summary>
    public enum SpecialPocketType
    {
        None,
        Skull,
        Jackpot,
        Mystery,
        Bomb,
        Hourglass,
        Tax,
        Influence,
        DebtRelief,
        Curse,
        GoldenTicket
    }
}
