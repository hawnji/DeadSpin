using System;
using System.Collections.Generic;
using DeadSpin.Core.Betting;
using DeadSpin.Core.Table;

namespace DeadSpin.Core.GameMaster
{
    /// <summary>
    /// What the Game Master has learned about the player: which numbers and bet types they favour,
    /// how big they bet, how the last rounds went, which abilities they lean on and which loopholes
    /// they have exploited. Fed by round results; read by the decision system and the dialogue.
    /// </summary>
    public sealed class PlayerProfile
    {
        private readonly Dictionary<int, long> _numberStakes = new Dictionary<int, long>();
        private readonly Dictionary<BetType, long> _typeStakes = new Dictionary<BetType, long>();
        private readonly long[] _dozenStakes = new long[3];
        private readonly List<long> _recentNets = new List<long>();
        private readonly Dictionary<string, int> _abilityUses = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _loopholes = new Dictionary<string, int>();

        public long TotalStaked { get; private set; }
        public int RoundsObserved { get; private set; }
        public int WinStreak { get; private set; }
        public int LossStreak { get; private set; }
        public long BiggestWin { get; private set; }
        public long LastNet { get; private set; }
        public long LastStake { get; private set; }
        public int RoundsWithoutBetting { get; private set; }

        public IReadOnlyList<long> RecentNets => _recentNets;
        public IReadOnlyDictionary<string, int> LoopholeUses => _loopholes;
        public IReadOnlyDictionary<string, int> AbilityUses => _abilityUses;

        public int TotalLoopholeExploits
        {
            get { int t = 0; foreach (int v in _loopholes.Values) t += v; return t; }
        }

        public int TotalAbilityUses
        {
            get { int t = 0; foreach (int v in _abilityUses.Values) t += v; return t; }
        }

        /// <summary>Net result of the last five rounds.</summary>
        public long RecentNet
        {
            get { long t = 0; foreach (long n in _recentNets) t += n; return t; }
        }

        public void Observe(RoundResult result, int highestNumber)
        {
            if (result == null) return;
            RoundsObserved++;
            LastNet = result.Net;
            LastStake = result.TotalStaked;
            _recentNets.Add(result.Net);
            if (_recentNets.Count > 5) _recentNets.RemoveAt(0);
            if (result.TotalStaked == 0) RoundsWithoutBetting++;
            if (result.Net > 0) { WinStreak++; LossStreak = 0; BiggestWin = Math.Max(BiggestWin, result.Net); }
            else if (result.Net < 0) { LossStreak++; WinStreak = 0; }

            foreach (BetOutcome bet in result.Bets)
            {
                TotalStaked += bet.Stake;
                Add(_typeStakes, bet.Selection.Type, bet.Stake);
                if (bet.Selection.IsNumbered && !bet.Selection.IsHidden)
                {
                    long share = bet.Stake / bet.Selection.Numbers.Length;
                    foreach (int n in bet.Selection.Numbers)
                    {
                        Add(_numberStakes, n, Math.Max(1, share));
                        int third = Math.Max(1, highestNumber / 3);
                        if (n >= 1) _dozenStakes[Math.Min(2, (n - 1) / third)] += Math.Max(1, share);
                    }
                }
                if (bet.Selection.Type == BetType.Dozen) _dozenStakes[bet.Selection.Group] += bet.Stake;
            }
            foreach (string id in result.LoopholesExploited) Add(_loopholes, id, 1);
        }

        public void RecordAbility(string abilityId)
        {
            Add(_abilityUses, abilityId, 1);
        }

        public void RecordLoophole(string ruleId)
        {
            Add(_loopholes, ruleId, 1);
        }

        private static void Add<TKey>(Dictionary<TKey, long> map, TKey key, long amount)
        {
            long v;
            map[key] = map.TryGetValue(key, out v) ? v + amount : amount;
        }

        private static void Add(Dictionary<string, int> map, string key, int amount)
        {
            int v;
            map[key] = map.TryGetValue(key, out v) ? v + amount : amount;
        }

        /// <summary>The number the player has put the most money on (0 never counts), or -1.</summary>
        public int FavoriteNumber
        {
            get
            {
                int best = -1;
                long most = 0;
                foreach (KeyValuePair<int, long> pair in _numberStakes)
                {
                    if (pair.Key <= 0) continue;
                    if (pair.Value > most || (pair.Value == most && pair.Key < best)) { most = pair.Value; best = pair.Key; }
                }
                return best;
            }
        }

        /// <summary>Share (0..1) of all stakes placed on the favourite number.</summary>
        public double FavoriteNumberShare
        {
            get
            {
                int fav = FavoriteNumber;
                long v;
                return fav < 0 || TotalStaked == 0 || !_numberStakes.TryGetValue(fav, out v) ? 0 : (double)v / TotalStaked;
            }
        }

        public int FavoriteDozen
        {
            get
            {
                int best = 0;
                for (int i = 1; i < 3; i++) if (_dozenStakes[i] > _dozenStakes[best]) best = i;
                return best;
            }
        }

        public long StakeOn(BetType type)
        {
            long v;
            return _typeStakes.TryGetValue(type, out v) ? v : 0;
        }

        /// <summary>Share (0..1) of stakes on traditional outside bets.</summary>
        public double OutsideShare
        {
            get
            {
                if (TotalStaked == 0) return 0;
                long outside = 0;
                foreach (KeyValuePair<BetType, long> pair in _typeStakes) if (pair.Key >= BetType.Red && pair.Key <= BetType.Column) outside += pair.Value;
                return (double)outside / TotalStaked;
            }
        }

        public double InsideShare
        {
            get
            {
                if (TotalStaked == 0) return 0;
                long inside = 0;
                foreach (KeyValuePair<BetType, long> pair in _typeStakes) if (pair.Key <= BetType.SixLine) inside += pair.Value;
                return (double)inside / TotalStaked;
            }
        }

        public double SpecialShare
        {
            get
            {
                if (TotalStaked == 0) return 0;
                long special = 0;
                foreach (KeyValuePair<BetType, long> pair in _typeStakes) if (pair.Key >= BetType.Neighbor) special += pair.Value;
                return (double)special / TotalStaked;
            }
        }

        public double ShareOf(BetType type)
        {
            return TotalStaked == 0 ? 0 : (double)StakeOn(type) / TotalStaked;
        }

        /// <summary>The bet type with the most money on it, for The Inspection.</summary>
        public BetType FavoriteType
        {
            get
            {
                BetType best = BetType.Red;
                long most = -1;
                foreach (KeyValuePair<BetType, long> pair in _typeStakes) if (pair.Value > most) { most = pair.Value; best = pair.Key; }
                return best;
            }
        }

        public long AverageStakePerRound => RoundsObserved == 0 ? 0 : TotalStaked / RoundsObserved;

        // ---------- persistence
        public Dictionary<int, long> SnapshotNumbers() { return new Dictionary<int, long>(_numberStakes); }
        public Dictionary<BetType, long> SnapshotTypes() { return new Dictionary<BetType, long>(_typeStakes); }
        public long[] SnapshotDozens() { return (long[])_dozenStakes.Clone(); }

        public void Restore(Dictionary<int, long> numbers, Dictionary<BetType, long> types, long[] dozens, IEnumerable<long> recent,
                            int rounds, long totalStaked, int winStreak, int lossStreak, long biggestWin, int loopholes, int abilities,
                            long lastNet = 0, long lastStake = 0, int roundsWithoutBetting = 0)
        {
            LastNet = lastNet;
            LastStake = lastStake;
            RoundsWithoutBetting = roundsWithoutBetting;
            _numberStakes.Clear();
            if (numbers != null) foreach (KeyValuePair<int, long> p in numbers) _numberStakes[p.Key] = p.Value;
            _typeStakes.Clear();
            if (types != null) foreach (KeyValuePair<BetType, long> p in types) _typeStakes[p.Key] = p.Value;
            for (int i = 0; i < 3; i++) _dozenStakes[i] = dozens != null && i < dozens.Length ? dozens[i] : 0;
            _recentNets.Clear();
            if (recent != null) _recentNets.AddRange(recent);
            RoundsObserved = rounds;
            TotalStaked = totalStaked;
            WinStreak = winStreak;
            LossStreak = lossStreak;
            BiggestWin = biggestWin;
            _loopholes.Clear();
            if (loopholes > 0) _loopholes["restored"] = loopholes;
            _abilityUses.Clear();
            if (abilities > 0) _abilityUses["restored"] = abilities;
        }
    }
}
