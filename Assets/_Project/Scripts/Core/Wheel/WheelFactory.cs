using System;
using System.Collections.Generic;

namespace DeadSpin.Core.Wheel
{
    /// <summary>A named wheel shape: how many numbered pockets and how many zeros.</summary>
    public readonly struct WheelPreset
    {
        public readonly int PocketCount;
        public readonly int HighestNumber;
        public readonly int Zeros;

        public WheelPreset(int highestNumber, int zeros)
        {
            HighestNumber = highestNumber;
            Zeros = zeros;
            PocketCount = highestNumber + zeros;
        }

        public string Name => PocketCount + " pockets" + (Zeros == 0 ? " (no zero)" : Zeros == 1 ? "" : " (" + Zeros + " zeros)");
    }

    /// <summary>
    /// Generates wheels of any size. Numbers 1..H are spread around the ring so consecutive numbers
    /// are never neighbours, colours alternate red/black by position (so neighbours always differ)
    /// and zeros are spaced evenly. 37 pockets with one zero returns the classic European wheel.
    /// Nothing here is a fixed table: any (numbers, zeros) pair that validates can be built.
    /// </summary>
    public static class WheelFactory
    {
        /// <summary>The configurations the Game Master and the debug menu can switch between.</summary>
        public static readonly WheelPreset[] Presets =
        {
            new WheelPreset(12, 0),
            new WheelPreset(18, 0),
            new WheelPreset(24, 0),
            new WheelPreset(36, 0),
            new WheelPreset(36, 1),
            new WheelPreset(48, 0),
            new WheelPreset(72, 0),
            new WheelPreset(99, 1)
        };

        /// <summary>Preset with this total pocket count, or a one-zero custom shape when none matches.</summary>
        public static WheelPreset PresetFor(int pocketCount)
        {
            foreach (WheelPreset preset in Presets) if (preset.PocketCount == pocketCount) return preset;
            if (pocketCount < WheelConfiguration.MinPockets) throw new ArgumentOutOfRangeException(nameof(pocketCount));
            return new WheelPreset(pocketCount - 1, 1);
        }

        public static WheelConfiguration Create(int pocketCount)
        {
            WheelPreset preset = PresetFor(pocketCount);
            return Create(preset.HighestNumber, preset.Zeros);
        }

        /// <summary>Builds a wheel showing numbers 1..<paramref name="highestNumber"/> plus <paramref name="zeros"/> zero pockets. Ids equal numbers for 1..H; zeros take ids 0, H+1, H+2...</summary>
        public static WheelConfiguration Create(int highestNumber, int zeros)
        {
            if (highestNumber < 1) throw new ArgumentOutOfRangeException(nameof(highestNumber), "A wheel needs at least one number.");
            if (zeros < 0) throw new ArgumentOutOfRangeException(nameof(zeros));
            if (highestNumber == 36 && zeros == 1) return StandardWheels.European();

            int count = highestNumber + zeros;
            var slots = new Pocket[count];

            // Zeros evenly spaced, the first at the top of the wheel.
            var zeroSlots = new HashSet<int>();
            for (int z = 0; z < zeros; z++)
            {
                int slot = (int)Math.Round((double)z * count / zeros) % count;
                while (zeroSlots.Contains(slot)) slot = (slot + 1) % count;
                zeroSlots.Add(slot);
                slots[slot] = new Pocket(z == 0 ? 0 : highestNumber + z, 0, PocketColor.Green);
            }

            var free = new List<int>(highestNumber);
            for (int i = 0; i < count; i++) if (!zeroSlots.Contains(i)) free.Add(i);

            // Stride through the free slots so number k and k+1 land far apart.
            int stride = ChooseStride(highestNumber);
            for (int k = 1; k <= highestNumber; k++)
            {
                int freeIndex = (int)((long)(k - 1) * stride % highestNumber);
                int slot = free[freeIndex];
                // Alternate colours by position among numbered slots so neighbours differ.
                PocketColor color = freeIndex % 2 == 0 ? PocketColor.Red : PocketColor.Black;
                slots[slot] = new Pocket(k, k, color);
            }

            return new WheelConfiguration(new WheelPreset(highestNumber, zeros).Name, slots);
        }

        /// <summary>A stride coprime with n near n * 0.38, so the sequence visits every slot exactly once and scatters.</summary>
        private static int ChooseStride(int n)
        {
            if (n <= 3) return 1;
            int target = Math.Max(2, (int)Math.Round(n * 0.382));
            for (int delta = 0; delta < n; delta++)
            {
                int up = target + delta;
                if (up < n && Gcd(up, n) == 1) return up;
                int down = target - delta;
                if (down > 1 && Gcd(down, n) == 1) return down;
            }
            return 1;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0) { int t = a % b; a = b; b = t; }
            return a;
        }
    }
}
