using System;
using System.Collections.Generic;
using DeadSpin.Core.Common;

namespace DeadSpin.Core.Wheel
{
    /// <summary>
    /// Pure, composable wheel modifications. Each takes a configuration and returns a
    /// <see cref="WheelChange"/> holding a NEW validated configuration (or a refusal with a reason);
    /// nothing is mutated. Rules, Game Master interventions, abilities and special pockets all
    /// change the wheel only through these functions, so every change is validated in one place.
    /// </summary>
    public static class WheelTransforms
    {
        // ---------- Rearrangement ----------

        /// <summary>Number Shuffle: every pocket moves to a random new slot (Fisher-Yates with the seeded RNG).</summary>
        public static WheelChange Shuffle(WheelConfiguration wheel, IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var pockets = Copy(wheel);
            for (int i = pockets.Count - 1; i > 0; i--)
            {
                int j = random.NextInt(i + 1);
                Pocket t = pockets[i]; pockets[i] = pockets[j]; pockets[j] = t;
            }
            return Finish(wheel, pockets, WheelChangeKind.Rearrange, "The numbers rearrange themselves.");
        }

        /// <summary>Move numbers: two pockets swap places.</summary>
        public static WheelChange Swap(WheelConfiguration wheel, int indexA, int indexB)
        {
            var pockets = Copy(wheel);
            if (!InRange(pockets, indexA) || !InRange(pockets, indexB) || indexA == indexB) return WheelChange.Refused(wheel, "Those pockets cannot swap.");
            Pocket t = pockets[indexA]; pockets[indexA] = pockets[indexB]; pockets[indexB] = t;
            return Finish(wheel, pockets, WheelChangeKind.Rearrange, pockets[indexB].Number + " and " + pockets[indexA].Number + " trade places.");
        }

        /// <summary>A section of the wheel rotates: pockets [start, start+length) shift by <paramref name="steps"/> within the section.</summary>
        public static WheelChange RotateSection(WheelConfiguration wheel, int start, int length, int steps)
        {
            int n = wheel.PocketCount;
            if (length < 2 || length > n) return WheelChange.Refused(wheel, "That section cannot rotate.");
            var pockets = Copy(wheel);
            var section = new Pocket[length];
            for (int i = 0; i < length; i++) section[i] = pockets[Mod(start + i, n)];
            for (int i = 0; i < length; i++) pockets[Mod(start + Mod(i + steps, length), n)] = section[i];
            return Finish(wheel, pockets, WheelChangeKind.Rearrange, "A section of the wheel turns.");
        }

        // ---------- Clone / remove / add ----------

        /// <summary>Number Clone: the pocket at <paramref name="index"/> splits into <paramref name="copies"/> + 1 pockets showing the same number, inserted beside it.</summary>
        public static WheelChange Clone(WheelConfiguration wheel, int index, int copies = 1)
        {
            if (!InRange(wheel.Pockets, index) || copies < 1) return WheelChange.Refused(wheel, "Nothing to clone.");
            if (wheel.PocketCount + copies > WheelConfiguration.MaxPockets) return WheelChange.Refused(wheel, "The wheel cannot hold more pockets.");
            var pockets = Copy(wheel);
            Pocket source = pockets[index];
            int nextId = wheel.NextId();
            var sources = new Dictionary<int, int>();
            for (int c = 0; c < copies; c++)
            {
                var clone = new Pocket(nextId, source.Number, source.Color, source.Weight, source.IsActive);
                sources[nextId] = source.Id;
                nextId++;
                pockets.Insert(index + 1 + c, clone);
            }
            string what = copies == 1 ? "a twin" : copies + " copies";
            return Finish(wheel, pockets, WheelChangeKind.Clone, source.Number + " splits into " + what + ".", sources);
        }

        /// <summary>Clones every pocket showing <paramref name="number"/> until it appears <paramref name="totalCopies"/> times (e.g. "13 appears in four pockets"), spreading the clones around the wheel.</summary>
        public static WheelChange CloneNumber(WheelConfiguration wheel, int number, int totalCopies)
        {
            int existing = wheel.CountOf(number);
            if (existing == 0) return WheelChange.Refused(wheel, number + " is not on the wheel.");
            if (totalCopies <= existing) return WheelChange.Refused(wheel, number + " already appears " + existing + " times.");
            int toAdd = totalCopies - existing;
            if (wheel.PocketCount + toAdd > WheelConfiguration.MaxPockets) return WheelChange.Refused(wheel, "The wheel cannot hold more pockets.");

            var pockets = Copy(wheel);
            Pocket source = pockets[wheel.IndicesOfNumber(number)[0]];
            int nextId = wheel.NextId();
            var sources = new Dictionary<int, int>();
            for (int c = 0; c < toAdd; c++)
            {
                // Spread clones evenly: insert at fractions of the way round.
                int at = (int)((long)pockets.Count * (c + 1) / (toAdd + 1));
                var clone = new Pocket(nextId, source.Number, source.Color, source.Weight, source.IsActive);
                sources[nextId] = source.Id;
                nextId++;
                pockets.Insert(at, clone);
            }
            return Finish(wheel, pockets, WheelChangeKind.Clone, number + " now appears in " + totalCopies + " pockets.", sources);
        }

        /// <summary>Removes a single pocket.</summary>
        public static WheelChange RemoveAt(WheelConfiguration wheel, int index)
        {
            if (!InRange(wheel.Pockets, index)) return WheelChange.Refused(wheel, "No such pocket.");
            var pockets = Copy(wheel);
            Pocket removed = pockets[index];
            pockets.RemoveAt(index);
            return Finish(wheel, pockets, WheelChangeKind.Remove, "Pocket " + removed.Number + " collapses into the wheel.");
        }

        /// <summary>Number Removal: every pocket showing <paramref name="number"/> is removed.</summary>
        public static WheelChange RemoveNumber(WheelConfiguration wheel, int number)
        {
            var pockets = Copy(wheel);
            int removed = pockets.RemoveAll(p => p.Number == number);
            if (removed == 0) return WheelChange.Refused(wheel, number + " is not on the wheel.");
            return Finish(wheel, pockets, WheelChangeKind.Remove, number + " is gone from the wheel.");
        }

        /// <summary>Number Addition: adds <paramref name="count"/> new numbers above the current highest, spaced around the wheel. The layout grows to match.</summary>
        public static WheelChange AddNumbers(WheelConfiguration wheel, int count)
        {
            if (count < 1) return WheelChange.Refused(wheel, "Nothing to add.");
            if (wheel.PocketCount + count > WheelConfiguration.MaxPockets) return WheelChange.Refused(wheel, "The wheel cannot hold more pockets.");
            if (wheel.HighestNumber + count > WheelConfiguration.MaxNumber) return WheelChange.Refused(wheel, "The layout cannot show more numbers.");
            var pockets = Copy(wheel);
            int nextId = wheel.NextId();
            int next = wheel.HighestNumber + 1;
            for (int c = 0; c < count; c++)
            {
                int at = (int)((long)pockets.Count * (c + 1) / (count + 1));
                // Pick the colour that differs from the pocket it will follow, so neighbours stay alternating where possible.
                PocketColor before = pockets[Mod(at - 1, pockets.Count)].Color;
                PocketColor color = before == PocketColor.Red ? PocketColor.Black : PocketColor.Red;
                pockets.Insert(at, new Pocket(nextId++, next++, color));
            }
            return Finish(wheel, pockets, WheelChangeKind.Add, count + " new numbers emerge from the wheel.");
        }

        /// <summary>
        /// Change wheel size: rebuilds the wheel at a new pocket count, keeping the id of every number
        /// that survives so those pockets visibly slide to their new slots instead of vanishing.
        /// Special pockets and weights are not carried over (a full transformation resets them).
        /// </summary>
        public static WheelChange Resize(WheelConfiguration wheel, int pocketCount)
        {
            if (pocketCount < WheelConfiguration.MinPockets || pocketCount > WheelConfiguration.MaxPockets) return WheelChange.Refused(wheel, "Unsupported pocket count.");
            if (pocketCount == wheel.PocketCount) return WheelChange.Refused(wheel, "The wheel already has " + pocketCount + " pockets.");
            WheelConfiguration fresh = WheelFactory.Create(pocketCount);

            // Reuse the old id for the first pocket showing each surviving number; everything else gets new ids.
            var oldIdByNumber = new Dictionary<int, int>();
            foreach (Pocket p in wheel.Pockets) if (!oldIdByNumber.ContainsKey(p.Number)) oldIdByNumber[p.Number] = p.Id;
            int nextId = Math.Max(wheel.NextId(), fresh.NextId());
            var used = new HashSet<int>();
            var pockets = new List<Pocket>(fresh.PocketCount);
            foreach (Pocket p in fresh.Pockets)
            {
                int id;
                if (!oldIdByNumber.TryGetValue(p.Number, out id) || used.Contains(id)) id = nextId++;
                used.Add(id);
                pockets.Add(p.WithId(id));
            }
            var result = new WheelConfiguration(fresh.Name, pockets, wheel.Ring);
            return FinishConfig(wheel, result, WheelChangeKind.Resize, "The wheel becomes " + pocketCount + " pockets.", null);
        }

        // ---------- Appearance and weight ----------

        /// <summary>Color Reversal: red and black swap everywhere. Zeros stay green.</summary>
        public static WheelChange ReverseColors(WheelConfiguration wheel)
        {
            var pockets = Copy(wheel);
            for (int i = 0; i < pockets.Count; i++)
            {
                PocketColor c = pockets[i].Color;
                if (c == PocketColor.Red) pockets[i] = pockets[i].WithColor(PocketColor.Black);
                else if (c == PocketColor.Black) pockets[i] = pockets[i].WithColor(PocketColor.Red);
            }
            return Finish(wheel, pockets, WheelChangeKind.Recolor, "Red becomes black. Black becomes red.");
        }

        /// <summary>Sets the weight of every pocket showing a number (Lucky Numbers &gt; 1, Magnetic Pockets, Dead Numbers = 0 via <see cref="SetActive"/>).</summary>
        public static WheelChange SetNumberWeight(WheelConfiguration wheel, int number, int weight)
        {
            if (weight < 0 || weight > 10) return WheelChange.Refused(wheel, "Weights must be between 0 and 10.");
            if (!wheel.Contains(number)) return WheelChange.Refused(wheel, number + " is not on the wheel.");
            var pockets = Copy(wheel);
            for (int i = 0; i < pockets.Count; i++) if (pockets[i].Number == number) pockets[i] = pockets[i].WithWeight(weight);
            return Finish(wheel, pockets, WheelChangeKind.Reweight, number + " now weighs x" + weight + ".");
        }

        public static WheelChange SetPocketWeight(WheelConfiguration wheel, int index, int weight)
        {
            if (!InRange(wheel.Pockets, index)) return WheelChange.Refused(wheel, "No such pocket.");
            if (weight < 0 || weight > 10) return WheelChange.Refused(wheel, "Weights must be between 0 and 10.");
            var pockets = Copy(wheel);
            pockets[index] = pockets[index].WithWeight(weight);
            return Finish(wheel, pockets, WheelChangeKind.Reweight, "Pocket " + pockets[index].Number + " is now weighted x" + weight + ".");
        }

        /// <summary>Dead Numbers: closes (or reopens) every pocket showing a number. A closed pocket can never win.</summary>
        public static WheelChange SetActive(WheelConfiguration wheel, int number, bool active)
        {
            if (!wheel.Contains(number)) return WheelChange.Refused(wheel, number + " is not on the wheel.");
            var pockets = Copy(wheel);
            for (int i = 0; i < pockets.Count; i++) if (pockets[i].Number == number) pockets[i] = pockets[i].WithActive(active);
            return Finish(wheel, pockets, WheelChangeKind.Reweight, active ? number + " is alive again." : number + " is dead. It cannot win.");
        }

        /// <summary>Uneven Number Distribution: every pocket gets a random weight between 1 and <paramref name="maxWeight"/>.</summary>
        public static WheelChange RandomWeights(WheelConfiguration wheel, IRandomSource random, int maxWeight = 3)
        {
            if (maxWeight < 1 || maxWeight > 10) return WheelChange.Refused(wheel, "Weights must be between 1 and 10.");
            var pockets = Copy(wheel);
            for (int i = 0; i < pockets.Count; i++) pockets[i] = pockets[i].WithWeight(1 + random.NextInt(maxWeight));
            return Finish(wheel, pockets, WheelChangeKind.Reweight, "The pockets are no longer equal.");
        }

        /// <summary>Resets every weight to 1 and reopens every pocket.</summary>
        public static WheelChange NormalizeWeights(WheelConfiguration wheel)
        {
            var pockets = Copy(wheel);
            for (int i = 0; i < pockets.Count; i++) pockets[i] = pockets[i].WithWeight(1).WithActive(true);
            return Finish(wheel, pockets, WheelChangeKind.Reweight, "Every pocket is equal again.");
        }

        // ---------- Special pockets ----------

        /// <summary>Special Number Insertion: marks the pocket at <paramref name="index"/> with a special type.</summary>
        public static WheelChange SetSpecial(WheelConfiguration wheel, int index, SpecialPocketType special)
        {
            if (!InRange(wheel.Pockets, index)) return WheelChange.Refused(wheel, "No such pocket.");
            var pockets = Copy(wheel);
            pockets[index] = pockets[index].WithSpecial(special);
            string text = special == SpecialPocketType.None ? "Pocket " + pockets[index].Number + " is ordinary again." : "Pocket " + pockets[index].Number + " becomes a " + special + " pocket.";
            return Finish(wheel, pockets, WheelChangeKind.Special, text);
        }

        /// <summary>Adds a brand-new pocket showing <paramref name="number"/> with a special type, beside a random pocket.</summary>
        public static WheelChange InsertSpecial(WheelConfiguration wheel, SpecialPocketType special, IRandomSource random)
        {
            if (wheel.PocketCount + 1 > WheelConfiguration.MaxPockets) return WheelChange.Refused(wheel, "The wheel cannot hold more pockets.");
            var pockets = Copy(wheel);
            int at = random.NextInt(pockets.Count);
            Pocket host = pockets[at];
            int id = wheel.NextId();
            pockets.Insert(at + 1, new Pocket(id, host.Number, host.Color, 1, true, special));
            var sources = new Dictionary<int, int> { { id, host.Id } };
            return Finish(wheel, pockets, WheelChangeKind.Special, "A " + special + " pocket splits off " + host.Number + ".", sources);
        }

        /// <summary>Random Pocket Replacement: a random non-zero pocket is replaced by another number from the wheel.</summary>
        public static WheelChange ReplaceRandom(WheelConfiguration wheel, IRandomSource random)
        {
            var candidates = new List<int>();
            for (int i = 0; i < wheel.PocketCount; i++) if (!wheel.Pockets[i].IsZero) candidates.Add(i);
            if (candidates.Count < 2) return WheelChange.Refused(wheel, "Nothing to replace.");
            int target = candidates[random.NextInt(candidates.Count)];
            int donor = target;
            while (donor == target) donor = candidates[random.NextInt(candidates.Count)];
            var pockets = Copy(wheel);
            Pocket old = pockets[target];
            Pocket d = pockets[donor];
            pockets[target] = new Pocket(old.Id, d.Number, d.Color, old.Weight, old.IsActive, old.Special, old.PayoutPercent);
            return Finish(wheel, pockets, WheelChangeKind.Replace, old.Number + " is replaced by another " + d.Number + ".");
        }

        /// <summary>Changes the payout percentage of every pocket of one colour (e.g. Red Tax at the pocket level).</summary>
        public static WheelChange SetPayoutPercent(WheelConfiguration wheel, int index, int percent)
        {
            if (!InRange(wheel.Pockets, index)) return WheelChange.Refused(wheel, "No such pocket.");
            if (percent < 0 || percent > 500) return WheelChange.Refused(wheel, "Payout percent must be 0-500.");
            var pockets = Copy(wheel);
            pockets[index] = pockets[index].WithPayoutPercent(percent);
            return Finish(wheel, pockets, WheelChangeKind.Special, "Pocket " + pockets[index].Number + " now pays " + percent + "%.");
        }

        // ---------- Rings ----------

        /// <summary>Independent Rotating Rings: activates (or removes, with null) the outer bonus ring.</summary>
        public static WheelChange SetRing(WheelConfiguration wheel, WheelRing ring)
        {
            if (ring == null && wheel.Ring == null) return WheelChange.Refused(wheel, "No ring to remove.");
            WheelConfiguration result = wheel.WithRing(ring);
            return FinishConfig(wheel, result, WheelChangeKind.Ring, ring != null ? "An outer ring locks into place around the wheel." : "The outer ring retracts.", null);
        }

        // ---------- Helpers ----------

        private static List<Pocket> Copy(WheelConfiguration wheel)
        {
            if (wheel == null) throw new ArgumentNullException(nameof(wheel));
            return new List<Pocket>(wheel.Pockets);
        }

        private static bool InRange<T>(IReadOnlyList<T> list, int index) { return index >= 0 && index < list.Count; }
        private static bool InRange<T>(List<T> list, int index) { return index >= 0 && index < list.Count; }

        private static int Mod(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }

        private static WheelChange Finish(WheelConfiguration before, List<Pocket> pockets, WheelChangeKind kind, string description, IReadOnlyDictionary<int, int> sources = null)
        {
            return FinishConfig(before, before.With(pockets), kind, description, sources);
        }

        private static WheelChange FinishConfig(WheelConfiguration before, WheelConfiguration after, WheelChangeKind kind, string description, IReadOnlyDictionary<int, int> sources)
        {
            IReadOnlyList<string> errors = after.Validate();
            if (errors.Count > 0) return WheelChange.Refused(before, string.Join(" ", errors));
            return new WheelChange(before, after, kind, description, sources);
        }
    }
}
