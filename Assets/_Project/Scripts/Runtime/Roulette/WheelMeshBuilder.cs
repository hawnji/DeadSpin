using System.Collections.Generic;
using DeadSpin.Core.Wheel;
using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>Where and how one pocket is drawn: used for static wheels and every frame of a transformation.</summary>
    public struct PocketSlot
    {
        /// <summary>Centre angle in degrees, clockwise from +Z seen from above.</summary>
        public float CenterAngle;
        /// <summary>Angular width in degrees (0 = collapsed).</summary>
        public float Width;
        public PocketColor Color;
        /// <summary>0..1 multiplier on the band height (pockets "flip" through 0 when they change colour).</summary>
        public float BandScale;
        public bool Closed;
    }

    /// <summary>
    /// Generates the rotor mesh for any pocket count: one wedge-shaped pocket floor and one coloured
    /// number band per pocket, plus gold frets between pockets. Pocket i is centred at angle
    /// i * 360 / N degrees, clockwise from the rotor's +Z seen from above, matching the order in
    /// <see cref="WheelConfiguration.Pockets"/>. Lists are reused between rebuilds to avoid garbage.
    /// Submeshes: 0 pocket floors, 1 red band, 2 black band, 3 green band, 4 frets, 5 closed-pocket covers.
    /// The rotor can also be built from arbitrary <see cref="PocketSlot"/>s, which is how wheel
    /// transformations animate pockets sliding, splitting, emerging and collapsing.
    /// </summary>
    public sealed class WheelMeshBuilder
    {
        public const int SubmeshFloor = 0;
        public const int SubmeshRed = 1;
        public const int SubmeshBlack = 2;
        public const int SubmeshGreen = 3;
        public const int SubmeshFret = 4;
        public const int SubmeshClosed = 5;
        public const int SubmeshCount = 6;

        public float PocketInnerRadius = 0.2f;
        public float PocketOuterRadius = 0.31f;
        public float BandOuterRadius = 0.39f;
        public float PocketFloorHeight = 0.045f;
        public float BandHeight = 0.062f;
        public float FretHeight = 0.018f;
        public float FretWidth = 0.006f;
        /// <summary>Arc segments per pocket; more pockets need fewer segments each.</summary>
        public int MinSegmentsPerPocket = 2;
        public int TargetSegmentsAroundWheel = 180;

        private readonly List<Vector3> _vertices = new List<Vector3>(4096);
        private readonly List<Vector3> _normals = new List<Vector3>(4096);
        private readonly List<Vector2> _uvs = new List<Vector2>(4096);
        private readonly List<int>[] _triangles = new List<int>[SubmeshCount];

        public WheelMeshBuilder()
        {
            for (int i = 0; i < SubmeshCount; i++) _triangles[i] = new List<int>(4096);
        }

        /// <summary>Angle in degrees of a pocket's centre, clockwise from +Z seen from above.</summary>
        public static float PocketAngle(int index, int pocketCount)
        {
            return index * 360f / pocketCount;
        }

        /// <summary>Rotor-local point at the centre of a pocket floor.</summary>
        public Vector3 PocketCenter(int index, int pocketCount)
        {
            float radius = (PocketInnerRadius + PocketOuterRadius) * 0.5f;
            return Polar(PocketAngle(index, pocketCount), radius, PocketFloorHeight);
        }

        public static Vector3 Polar(float angleDegrees, float radius, float y)
        {
            float a = angleDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);
        }

        private readonly List<PocketSlot> _slots = new List<PocketSlot>(128);

        /// <summary>Evenly spaced slots for a configuration (the resting wheel).</summary>
        public List<PocketSlot> SlotsFor(WheelConfiguration wheel)
        {
            _slots.Clear();
            int n = wheel.PocketCount;
            float step = 360f / n;
            for (int i = 0; i < n; i++)
            {
                Pocket pocket = wheel.Pockets[i];
                _slots.Add(new PocketSlot { CenterAngle = PocketAngle(i, n), Width = step, Color = pocket.Color, BandScale = 1f, Closed = !pocket.IsActive || pocket.Weight == 0 });
            }
            return _slots;
        }

        public void Build(WheelConfiguration wheel, Mesh target)
        {
            Build(SlotsFor(wheel), target);
        }

        public void Build(IList<PocketSlot> slots, Mesh target)
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            for (int i = 0; i < SubmeshCount; i++) _triangles[i].Clear();

            int count = Mathf.Max(1, slots.Count);
            int segments = Mathf.Max(MinSegmentsPerPocket, Mathf.CeilToInt((float)TargetSegmentsAroundWheel / count));

            for (int i = 0; i < slots.Count; i++)
            {
                PocketSlot slot = slots[i];
                if (slot.Width <= 0.01f) continue;
                float a0 = slot.CenterAngle - slot.Width * 0.5f;
                float a1 = a0 + slot.Width;
                int band = BandSubmesh(slot.Color);
                float bandTop = Mathf.Lerp(PocketFloorHeight + 0.002f, BandHeight, Mathf.Clamp01(slot.BandScale));

                AddAnnulusSector(SubmeshFloor, PocketInnerRadius, PocketOuterRadius, a0, a1, PocketFloorHeight, segments);
                if (slot.Closed)
                {
                    // A dark cover plate sits just above the floor of a closed (dead) pocket.
                    AddAnnulusSector(SubmeshClosed, PocketInnerRadius + 0.004f, PocketOuterRadius - 0.004f, a0 + slot.Width * 0.08f, a1 - slot.Width * 0.08f, PocketFloorHeight + 0.006f, segments);
                }
                AddAnnulusSector(band, PocketOuterRadius, BandOuterRadius, a0, a1, bandTop, segments);
                // Inner wall of the band so pockets read as recessed.
                AddRing(band, PocketOuterRadius, a0, a1, PocketFloorHeight, bandTop, segments, inward: true);

                // Fret on the leading edge of every pocket.
                Vector3 mid = Polar(a0, (PocketInnerRadius + PocketOuterRadius) * 0.5f, PocketFloorHeight + FretHeight * 0.5f);
                AddBox(SubmeshFret, mid, Quaternion.Euler(0f, a0, 0f), new Vector3(FretWidth, FretHeight, PocketOuterRadius - PocketInnerRadius));
            }

            // Outer skirt of the rotor.
            AddRing(SubmeshFloor, BandOuterRadius, 0f, 360f, 0f, BandHeight, Mathf.Max(64, count * segments), inward: false);

            target.Clear();
            target.indexFormat = _vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            target.SetVertices(_vertices);
            target.SetNormals(_normals);
            target.SetUVs(0, _uvs);
            target.subMeshCount = SubmeshCount;
            for (int i = 0; i < SubmeshCount; i++) target.SetTriangles(_triangles[i], i, false);
            target.RecalculateBounds();
        }

        /// <summary>
        /// Builds a flat ring of coloured segments (the outer bonus ring). Each segment uses the
        /// submesh given in <paramref name="segmentSubmesh"/>; widths are proportional to weights.
        /// </summary>
        public void BuildRing(Mesh target, float innerRadius, float outerRadius, float height, IList<float> widths, IList<int> segmentSubmesh, int submeshCount)
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            for (int i = 0; i < SubmeshCount; i++) _triangles[i].Clear();

            float total = 0f;
            foreach (float w in widths) total += w;
            float angle = 0f;
            for (int i = 0; i < widths.Count; i++)
            {
                float sweep = 360f * widths[i] / total;
                int sub = Mathf.Clamp(segmentSubmesh[i], 0, SubmeshCount - 1);
                AddAnnulusSector(sub, innerRadius, outerRadius, angle, angle + sweep, height, Mathf.Max(3, Mathf.CeilToInt(sweep / 3f)));
                Vector3 mid = Polar(angle, (innerRadius + outerRadius) * 0.5f, height + 0.003f);
                AddBox(submeshCount - 1, mid, Quaternion.Euler(0f, angle, 0f), new Vector3(0.004f, 0.006f, outerRadius - innerRadius));
                angle += sweep;
            }
            AddRing(0, outerRadius, 0f, 360f, 0f, height, 120, inward: false);

            target.Clear();
            target.SetVertices(_vertices);
            target.SetNormals(_normals);
            target.SetUVs(0, _uvs);
            target.subMeshCount = submeshCount;
            for (int i = 0; i < submeshCount; i++) target.SetTriangles(_triangles[i], i, false);
            target.RecalculateBounds();
        }

        /// <summary>
        /// Builds the static bowl around the rotor: a base disc, the sloped ball track and the rim.
        /// Submeshes: 0 wood, 1 gold trim.
        /// </summary>
        public void BuildBowl(Mesh target, float trackInner, float trackOuter, float trackLow, float trackHigh, float rimOuter, float rimHeight)
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            for (int i = 0; i < SubmeshCount; i++) _triangles[i].Clear();

            const int segments = 160;
            AddAnnulusSector(0, 0f, trackInner, 0f, 360f, 0.002f, 0.002f, segments);              // floor under the rotor
            AddRing(0, trackInner, 0f, 360f, 0.002f, trackLow, segments, inward: true);              // wall up to the track
            AddAnnulusSector(0, trackInner, trackOuter, 0f, 360f, trackLow, trackHigh, segments);   // sloped ball track
            AddRing(1, trackOuter, 0f, 360f, trackHigh, rimHeight, segments, inward: true);          // inner face of the rim
            AddAnnulusSector(1, trackOuter, rimOuter, 0f, 360f, rimHeight, rimHeight, segments);     // gold rim top
            AddRing(0, rimOuter, 0f, 360f, 0f, rimHeight, segments, inward: false);                  // outside of the bowl

            target.Clear();
            target.SetVertices(_vertices);
            target.SetNormals(_normals);
            target.SetUVs(0, _uvs);
            target.subMeshCount = 2;
            target.SetTriangles(_triangles[0], 0, false);
            target.SetTriangles(_triangles[1], 1, false);
            target.RecalculateBounds();
        }

        private static int BandSubmesh(PocketColor color)
        {
            switch (color)
            {
                case PocketColor.Red: return SubmeshRed;
                case PocketColor.Black: return SubmeshBlack;
                default: return SubmeshGreen;
            }
        }

        private void AddAnnulusSector(int submesh, float r0, float r1, float a0, float a1, float y, int segments)
        {
            AddAnnulusSector(submesh, r0, r1, a0, a1, y, y, segments);
        }

        /// <summary>Flat or conical ring sector: height y0 at radius r0 rising (or falling) to y1 at r1.</summary>
        private void AddAnnulusSector(int submesh, float r0, float r1, float a0, float a1, float y0, float y1, int segments)
        {
            int start = _vertices.Count;
            float slope = Mathf.Atan2(y1 - y0, r1 - r0);
            for (int s = 0; s <= segments; s++)
            {
                float a = Mathf.Lerp(a0, a1, (float)s / segments);
                // Tilt the normal toward the centre on surfaces that rise outward (the ball track).
                Vector3 normal = (Vector3.up * Mathf.Cos(slope) - Polar(a, 1f, 0f) * Mathf.Sin(slope)).normalized;
                _vertices.Add(Polar(a, r0, y0));
                _vertices.Add(Polar(a, r1, y1));
                _normals.Add(normal);
                _normals.Add(normal);
                _uvs.Add(new Vector2((float)s / segments, 0f));
                _uvs.Add(new Vector2((float)s / segments, 1f));
            }
            List<int> tris = _triangles[submesh];
            for (int s = 0; s < segments; s++)
            {
                int i0 = start + s * 2;
                // Clockwise winding seen from above (Unity front faces are clockwise).
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 3);
                tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2);
            }
        }

        private void AddRing(int submesh, float radius, float a0, float a1, float y0, float y1, int segments, bool inward)
        {
            int start = _vertices.Count;
            for (int s = 0; s <= segments; s++)
            {
                float a = Mathf.Lerp(a0, a1, (float)s / segments);
                Vector3 normal = Polar(a, 1f, 0f) * (inward ? -1f : 1f);
                _vertices.Add(Polar(a, radius, y0));
                _vertices.Add(Polar(a, radius, y1));
                _normals.Add(normal);
                _normals.Add(normal);
                _uvs.Add(new Vector2((float)s / segments, 0f));
                _uvs.Add(new Vector2((float)s / segments, 1f));
            }
            List<int> tris = _triangles[submesh];
            for (int s = 0; s < segments; s++)
            {
                int i0 = start + s * 2;
                // Front faces must wind clockwise as seen from the side the normal points to.
                if (inward)
                {
                    tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 3);
                    tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2);
                }
                else
                {
                    tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
                    tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 1);
                }
            }
        }

        private static readonly Vector3[] BoxFaceNormals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

        private void AddBox(int submesh, Vector3 center, Quaternion rotation, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            List<int> tris = _triangles[submesh];
            foreach (Vector3 n in BoxFaceNormals)
            {
                // Two axes spanning the face.
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                int start = _vertices.Count;
                for (int k = 0; k < 4; k++)
                {
                    float su = (k == 1 || k == 2) ? 1f : -1f;
                    float sv = (k >= 2) ? 1f : -1f;
                    Vector3 local = Vector3.Scale(n + u * su + v * sv, h);
                    _vertices.Add(center + rotation * local);
                    _normals.Add(rotation * n);
                    _uvs.Add(new Vector2((su + 1f) * 0.5f, (sv + 1f) * 0.5f));
                }
                tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
                tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
            }
        }
    }
}
