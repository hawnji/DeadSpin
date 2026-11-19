using System;
using System.Collections.Generic;
using DeadSpin.Core.Common;
using DeadSpin.Core.Economy;
using DeadSpin.Core.Table;

namespace DeadSpin.Core.Specials
{
    /// <summary>
    /// INFLUENCE: the player's secondary resource, spent on abilities. Earned by winning difficult
    /// bets, completing challenges, discovering secrets, exploiting loopholes and predicting the Game
    /// Master's interventions.
    /// </summary>
    public sealed class InfluenceWallet
    {
        public int Current { get; private set; }
        public int TotalEarned { get; private set; }
        public int TotalSpent { get; private set; }

        public event Action<int, string> Changed;

        public InfluenceWallet(int starting = 0)
        {
            Current = Math.Max(0, starting);
        }

        public void Earn(int amount, string reason)
        {
            if (amount <= 0) return;
            Current += amount;
            TotalEarned += amount;
            Changed?.Invoke(amount, reason);
        }

        public bool CanAfford(int amount) { return amount <= Current; }

        public bool TrySpend(int amount, string reason)
        {
            if (amount < 0 || amount > Current) return false;
            if (amount == 0) return true;
            Current -= amount;
            TotalSpent += amount;
            Changed?.Invoke(-amount, reason);
            return true;
        }

        public void Restore(int current, int earned, int spent)
        {
            Current = Math.Max(0, current);
            TotalEarned = Math.Max(0, earned);
            TotalSpent = Math.Max(0, spent);
        }
    }

    /// <summary>
    /// What the player carries: owned ability upgrades, consumable charges, special chips and golden
    /// tickets, plus the per-ability cooldown and usage bookkeeping.
    /// </summary>
    public sealed class Inventory
    {
        private readonly HashSet<string> _upgrades = new HashSet<string>();
        private readonly Dictionary<string, int> _charges = new Dictionary<string, int>();
        private readonly Dictionary<ChipKind, int> _chips = new Dictionary<ChipKind, int>();
        private readonly Dictionary<string, int> _readyRound = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _runUses = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _roundUses = new Dictionary<string, int>();

        public int GoldenTickets { get; private set; }

        public IEnumerable<string> Upgrades => _upgrades;
        public IReadOnlyDictionary<string, int> Charges => _charges;
        public IReadOnlyDictionary<ChipKind, int> Chips => _chips;
        public IReadOnlyDictionary<string, int> RunUses => _runUses;

        public event Action Changed;

        public bool HasUpgrade(string abilityId) { return _upgrades.Contains(abilityId); }

        public void AddUpgrade(string abilityId)
        {
            if (_upgrades.Add(abilityId)) Changed?.Invoke();
        }

        public int ChargesOf(string abilityId)
        {
            int v;
            return _charges.TryGetValue(abilityId, out v) ? v : 0;
        }

        public void AddCharges(string abilityId, int count)
        {
            if (count <= 0) return;
            _charges[abilityId] = ChargesOf(abilityId) + count;
            Changed?.Invoke();
        }

        public bool TryConsumeCharge(string abilityId)
        {
            int have = ChargesOf(abilityId);
            if (have <= 0) return false;
            _charges[abilityId] = have - 1;
            Changed?.Invoke();
            return true;
        }

        public int ChipsOf(ChipKind kind)
        {
            if (kind == ChipKind.Standard) return int.MaxValue;
            int v;
            return _chips.TryGetValue(kind, out v) ? v : 0;
        }

        public void AddChips(ChipKind kind, int count)
        {
            if (kind == ChipKind.Standard || count <= 0) return;
            _chips[kind] = ChipsOf(kind) + count;
            Changed?.Invoke();
        }

        /// <summary>Uses one special chip placement. Standard chips are unlimited.</summary>
        public bool TryUseChip(ChipKind kind)
        {
            if (kind == ChipKind.Standard) return true;
            int have = ChipsOf(kind);
            if (have <= 0) return false;
            _chips[kind] = have - 1;
            Changed?.Invoke();
            return true;
        }

        public void AddGoldenTicket(int count = 1)
        {
            if (count <= 0) return;
            GoldenTickets += count;
            Changed?.Invoke();
        }

        public bool TryUseGoldenTicket()
        {
            if (GoldenTickets <= 0) return false;
            GoldenTickets--;
            Changed?.Invoke();
            return true;
        }

        // ---------- cooldowns and limits

        /// <summary>First round in which the ability can be used again.</summary>
        public int ReadyRound(string abilityId)
        {
            int v;
            return _readyRound.TryGetValue(abilityId, out v) ? v : 0;
        }

        public int UsesThisRun(string abilityId)
        {
            int v;
            return _runUses.TryGetValue(abilityId, out v) ? v : 0;
        }

        public int UsesThisRound(string abilityId)
        {
            int v;
            return _roundUses.TryGetValue(abilityId, out v) ? v : 0;
        }

        public void RecordUse(string abilityId, int round, int cooldown)
        {
            _runUses[abilityId] = UsesThisRun(abilityId) + 1;
            _roundUses[abilityId] = UsesThisRound(abilityId) + 1;
            if (cooldown > 0) _readyRound[abilityId] = round + cooldown;
            Changed?.Invoke();
        }

        public void NewRound()
        {
            _roundUses.Clear();
        }

        // ---------- persistence
        public Dictionary<string, int> SnapshotCharges() { return new Dictionary<string, int>(_charges); }
        public Dictionary<ChipKind, int> SnapshotChips() { return new Dictionary<ChipKind, int>(_chips); }
        public Dictionary<string, int> SnapshotReady() { return new Dictionary<string, int>(_readyRound); }

        public void Restore(IEnumerable<string> upgrades, IDictionary<string, int> charges, IDictionary<ChipKind, int> chips,
                            IDictionary<string, int> ready, IDictionary<string, int> runUses, int goldenTickets)
        {
            _upgrades.Clear();
            if (upgrades != null) foreach (string u in upgrades) _upgrades.Add(u);
            _charges.Clear();
            if (charges != null) foreach (KeyValuePair<string, int> p in charges) _charges[p.Key] = p.Value;
            _chips.Clear();
            if (chips != null) foreach (KeyValuePair<ChipKind, int> p in chips) _chips[p.Key] = p.Value;
            _readyRound.Clear();
            if (ready != null) foreach (KeyValuePair<string, int> p in ready) _readyRound[p.Key] = p.Value;
            _runUses.Clear();
            if (runUses != null) foreach (KeyValuePair<string, int> p in runUses) _runUses[p.Key] = p.Value;
            _roundUses.Clear();
            GoldenTickets = Math.Max(0, goldenTickets);
            Changed?.Invoke();
        }
    }

    public enum ShopItemKind
    {
        /// <summary>Permanent: the ability can be used for Influence from now on.</summary>
        AbilityUpgrade,
        /// <summary>Consumable charges of an ability.</summary>
        AbilityCharge,
        /// <summary>Special chips (a number of placements).</summary>
        Chips,
        InfluenceBundle,
        GoldenTicket
    }

    /// <summary>Something the Shop can sell. Prices are base prices; the shop scales them by stage.</summary>
    public sealed class ShopItem
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public ShopItemKind Kind { get; }
        public long BasePrice { get; set; }
        public string AbilityId { get; }
        public ChipKind Chip { get; }
        public int Quantity { get; set; }
        public int MinStage { get; set; } = 1;
        /// <summary>Needs this meta unlock (null: always available).</summary>
        public string UnlockId { get; set; }

        public ShopItem(string id, string name, string description, ShopItemKind kind, long basePrice, string abilityId = null, ChipKind chip = ChipKind.Standard, int quantity = 1)
        {
            Id = id;
            Name = name;
            Description = description;
            Kind = kind;
            BasePrice = basePrice;
            AbilityId = abilityId;
            Chip = chip;
            Quantity = quantity;
        }
    }

    public static class ShopCatalog
    {
        public static IReadOnlyList<ShopItem> CreateDefault()
        {
            return new[]
            {
                new ShopItem("upg_lucky_coin", "Lucky Coin", "Upgrade: add a second pocket for a number of your choice for one spin.", ShopItemKind.AbilityUpgrade, 1500, "lucky_coin") { UnlockId = "ability_lucky_coin" },
                new ShopItem("upg_rule_peek", "Rule Peek", "Upgrade: see the Game Master's next intervention before he makes it.", ShopItemKind.AbilityUpgrade, 1200, "rule_peek"),
                new ShopItem("upg_rule_cancel", "Rule Cancel", "Upgrade: cancel one announced intervention.", ShopItemKind.AbilityUpgrade, 2500, "rule_cancel") { MinStage = 2 },
                new ShopItem("upg_house_audit", "House Audit", "Upgrade: reveal every manipulated payout condition.", ShopItemKind.AbilityUpgrade, 1000, "house_audit"),
                new ShopItem("upg_chip_anchor", "Chip Anchor", "Upgrade: chips cannot be moved by table manipulation this round.", ShopItemKind.AbilityUpgrade, 1000, "chip_anchor"),
                new ShopItem("chg_bet_insurance", "Bet Insurance x2", "Two charges: half of your largest bet comes back if it loses.", ShopItemKind.AbilityCharge, 600, "bet_insurance", quantity: 2),
                new ShopItem("chg_chip_recall", "Chip Recall x2", "Two charges: return every displaced chip.", ShopItemKind.AbilityCharge, 300, "chip_recall", quantity: 2),
                new ShopItem("chg_double_down", "Double Down x2", "Two charges: double a bet after betting closes, above the table maximum.", ShopItemKind.AbilityCharge, 700, "double_down", quantity: 2),
                new ShopItem("chg_debt_freeze", "Debt Freeze", "One charge: your debt cannot increase this round.", ShopItemKind.AbilityCharge, 900, "debt_freeze") { MinStage = 2, UnlockId = "ability_debt_freeze" },
                new ShopItem("chips_magnetic", "Magnetic Chips x5", "Five placements. Slide toward neighbouring spots when the table shakes, never off it.", ShopItemKind.Chips, 250, chip: ChipKind.Magnetic, quantity: 5),
                new ShopItem("chips_sticky", "Sticky Chips x5", "Five placements. Never move, but cannot be taken back.", ShopItemKind.Chips, 400, chip: ChipKind.Sticky, quantity: 5),
                new ShopItem("chips_weighted", "Weighted Chips x5", "Five placements. Very hard to shake loose.", ShopItemKind.Chips, 350, chip: ChipKind.Weighted, quantity: 5) { UnlockId = "chips_weighted" },
                new ShopItem("chips_fragile", "Fragile Chips x5", "Five placements. Winning bets pay 10% more; they shatter if they fall.", ShopItemKind.Chips, 300, chip: ChipKind.Fragile, quantity: 5) { UnlockId = "chips_fragile" },
                new ShopItem("chips_protected", "Protected Chips x3", "Three placements. Locked in place from the moment they land.", ShopItemKind.Chips, 600, chip: ChipKind.Protected, quantity: 3) { MinStage = 2 },
                new ShopItem("influence_3", "Whispered Favours", "Three Influence.", ShopItemKind.InfluenceBundle, 900, quantity: 3),
                new ShopItem("golden_ticket", "Golden Ticket", "Cancels the Game Master's next intervention the moment he announces it.", ShopItemKind.GoldenTicket, 2000) { MinStage = 3, UnlockId = "shop_golden_ticket" }
            };
        }
    }

    /// <summary>One item on the shelf this stage.</summary>
    public sealed class ShopOffer
    {
        public ShopItem Item { get; }
        public long Price { get; }
        public int Remaining { get; set; }

        public ShopOffer(ShopItem item, long price, int remaining)
        {
            Item = item;
            Price = price;
            Remaining = remaining;
        }
    }

    /// <summary>
    /// THE SHOP: a randomised shelf of upgrades, charges and chips that restocks at the start of each
    /// stage. Prices rise 15% per stage. Every purchase goes through the Ledger.
    /// </summary>
    public sealed class Shop
    {
        public const int ShelfSize = 7;
        private readonly List<ShopItem> _catalog;
        private readonly List<ShopOffer> _shelf = new List<ShopOffer>();

        public IReadOnlyList<ShopOffer> Shelf => _shelf;
        public IReadOnlyList<ShopItem> Catalog => _catalog;
        public long TotalSpent { get; private set; }

        public event Action Changed;

        public Shop(IEnumerable<ShopItem> catalog)
        {
            _catalog = new List<ShopItem>(catalog ?? ShopCatalog.CreateDefault());
        }

        public static long PriceFor(ShopItem item, int stage)
        {
            long price = item.BasePrice * (100 + 15 * Math.Max(0, stage - 1)) / 100;
            return Math.Max(1, price / 10 * 10);
        }

        /// <summary>Restocks the shelf for a stage. Owned upgrades are not offered again.</summary>
        public void Restock(int stage, IRandomSource random, Func<string, bool> isUnlocked, Inventory inventory)
        {
            _shelf.Clear();
            var pool = new List<ShopItem>();
            foreach (ShopItem item in _catalog)
            {
                if (item.MinStage > stage) continue;
                if (item.UnlockId != null && (isUnlocked == null || !isUnlocked(item.UnlockId))) continue;
                if (item.Kind == ShopItemKind.AbilityUpgrade && inventory != null && inventory.HasUpgrade(item.AbilityId)) continue;
                pool.Add(item);
            }
            while (_shelf.Count < ShelfSize && pool.Count > 0)
            {
                int i = random.NextInt(pool.Count);
                ShopItem item = pool[i];
                pool.RemoveAt(i);
                _shelf.Add(new ShopOffer(item, PriceFor(item, stage), item.Kind == ShopItemKind.AbilityUpgrade ? 1 : 2));
            }
            Changed?.Invoke();
        }

        /// <summary>Buys one unit. Returns null on success or the reason it failed.</summary>
        public string Buy(string itemId, Ledger ledger, Inventory inventory, InfluenceWallet influence)
        {
            ShopOffer offer = null;
            foreach (ShopOffer o in _shelf) if (o.Item.Id == itemId) offer = o;
            if (offer == null) return "That is not on the shelf.";
            if (offer.Remaining <= 0) return "Sold out.";
            if (offer.Item.Kind == ShopItemKind.AbilityUpgrade && inventory.HasUpgrade(offer.Item.AbilityId)) return "You already own it.";
            if (!ledger.TryDebit(offer.Price, TransactionKind.Purchase, "Shop: " + offer.Item.Name)) return "You cannot afford it.";
            TotalSpent += offer.Price;
            offer.Remaining--;
            Grant(offer.Item, inventory, influence);
            Changed?.Invoke();
            return null;
        }

        public static void Grant(ShopItem item, Inventory inventory, InfluenceWallet influence)
        {
            switch (item.Kind)
            {
                case ShopItemKind.AbilityUpgrade: inventory.AddUpgrade(item.AbilityId); break;
                case ShopItemKind.AbilityCharge: inventory.AddCharges(item.AbilityId, item.Quantity); break;
                case ShopItemKind.Chips: inventory.AddChips(item.Chip, item.Quantity); break;
                case ShopItemKind.InfluenceBundle: influence.Earn(item.Quantity, item.Name); break;
                case ShopItemKind.GoldenTicket: inventory.AddGoldenTicket(item.Quantity); break;
            }
        }

        public void RestoreShelf(IEnumerable<ShopOffer> shelf, long totalSpent)
        {
            _shelf.Clear();
            if (shelf != null) _shelf.AddRange(shelf);
            TotalSpent = totalSpent;
            Changed?.Invoke();
        }

        public ShopItem Find(string id)
        {
            foreach (ShopItem item in _catalog) if (item.Id == id) return item;
            return null;
        }
    }
}
