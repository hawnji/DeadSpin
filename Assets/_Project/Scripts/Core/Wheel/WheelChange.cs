using System.Collections.Generic;

namespace DeadSpin.Core.Wheel
{
    /// <summary>What kind of physical change the wheel goes through, so the view can pick an animation.</summary>
    public enum WheelChangeKind
    {
        None,
        Rearrange,
        Clone,
        Remove,
        Add,
        Resize,
        Recolor,
        Reweight,
        Special,
        Ring,
        Replace
    }

    /// <summary>
    /// The result of a wheel transformation: the configuration before and after, plus a pocket-level
    /// diff matched by pocket id. The view animates from <see cref="Before"/> to <see cref="After"/>
    /// using the diff (pockets slide, split from their source, emerge or collapse) and then rebuilds
    /// from <see cref="After"/>, which is the only configuration the rules use.
    /// </summary>
    public sealed class WheelChange
    {
        public WheelConfiguration Before { get; }
        public WheelConfiguration After { get; }
        public WheelChangeKind Kind { get; }
        public string Description { get; }

        /// <summary>For pockets added by cloning: new pocket id → id of the pocket it split from.</summary>
        public IReadOnlyDictionary<int, int> CloneSources { get; }

        public bool Succeeded => After != null && Kind != WheelChangeKind.None;

        /// <summary>Why the change was refused, when <see cref="Succeeded"/> is false.</summary>
        public string Error { get; }

        public WheelChange(WheelConfiguration before, WheelConfiguration after, WheelChangeKind kind, string description, IReadOnlyDictionary<int, int> cloneSources = null)
        {
            Before = before;
            After = after;
            Kind = kind;
            Description = description;
            CloneSources = cloneSources ?? new Dictionary<int, int>();
        }

        private WheelChange(WheelConfiguration before, string error)
        {
            Before = before;
            After = null;
            Kind = WheelChangeKind.None;
            Error = error;
            Description = error;
            CloneSources = new Dictionary<int, int>();
        }

        public static WheelChange Refused(WheelConfiguration before, string error)
        {
            return new WheelChange(before, error);
        }

        /// <summary>Pocket-level diff between the two configurations.</summary>
        public WheelDiff Diff()
        {
            return WheelDiff.Compute(Before, After, CloneSources);
        }
    }

    /// <summary>One pocket's movement between two configurations.</summary>
    public readonly struct PocketMove
    {
        public readonly int Id;
        /// <summary>Index before the change, or -1 if the pocket is new.</summary>
        public readonly int FromIndex;
        /// <summary>Index after the change, or -1 if the pocket was removed.</summary>
        public readonly int ToIndex;
        /// <summary>For new pockets, the index (before the change) of the pocket they emerge from, or -1.</summary>
        public readonly int SourceIndex;

        public PocketMove(int id, int fromIndex, int toIndex, int sourceIndex)
        {
            Id = id;
            FromIndex = fromIndex;
            ToIndex = toIndex;
            SourceIndex = sourceIndex;
        }

        public bool IsAdded => FromIndex < 0;
        public bool IsRemoved => ToIndex < 0;
    }

    /// <summary>Matches pockets by id across two configurations.</summary>
    public sealed class WheelDiff
    {
        public IReadOnlyList<PocketMove> Moves { get; }
        public int Added { get; }
        public int Removed { get; }
        public int Moved { get; }

        private WheelDiff(List<PocketMove> moves, int added, int removed, int moved)
        {
            Moves = moves;
            Added = added;
            Removed = removed;
            Moved = moved;
        }

        public static WheelDiff Compute(WheelConfiguration before, WheelConfiguration after, IReadOnlyDictionary<int, int> cloneSources = null)
        {
            var moves = new List<PocketMove>();
            int added = 0, removed = 0, moved = 0;
            if (before == null || after == null) return new WheelDiff(moves, 0, 0, 0);

            var afterIndex = new Dictionary<int, int>();
            for (int i = 0; i < after.PocketCount; i++) afterIndex[after.Pockets[i].Id] = i;
            var beforeIndex = new Dictionary<int, int>();
            for (int i = 0; i < before.PocketCount; i++) beforeIndex[before.Pockets[i].Id] = i;

            for (int i = 0; i < before.PocketCount; i++)
            {
                int id = before.Pockets[i].Id;
                int to;
                if (afterIndex.TryGetValue(id, out to))
                {
                    moves.Add(new PocketMove(id, i, to, -1));
                    if (to != i) moved++;
                }
                else
                {
                    moves.Add(new PocketMove(id, i, -1, -1));
                    removed++;
                }
            }
            for (int i = 0; i < after.PocketCount; i++)
            {
                int id = after.Pockets[i].Id;
                if (beforeIndex.ContainsKey(id)) continue;
                int source = -1;
                int sourceId;
                if (cloneSources != null && cloneSources.TryGetValue(id, out sourceId))
                {
                    int s;
                    if (beforeIndex.TryGetValue(sourceId, out s)) source = s;
                }
                moves.Add(new PocketMove(id, -1, i, source));
                added++;
            }
            return new WheelDiff(moves, added, removed, moved);
        }
    }
}
