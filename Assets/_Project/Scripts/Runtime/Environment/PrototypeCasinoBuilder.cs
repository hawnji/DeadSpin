using DeadSpin.Runtime.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadSpin.Runtime.Environment
{
    /// <summary>
    /// Builds the greybox roulette room from primitives. The game is played from a top-down camera
    /// over the table, so the table is the brightest, most detailed thing in the scene and the room
    /// falls away into darkness around it: a pool of warm light on felt, the Game Master across the
    /// table, and only hints of marble, velvet and gold at the edges. Every dimension is in metres.
    /// Used by the editor menu (Dead Spin > Build Prototype Scene); later phases replace the
    /// primitives with authored art while keeping the same interaction components.
    /// </summary>
    public static class PrototypeCasinoBuilder
    {
        public const float RoomWidth = 14f;   // X
        public const float RoomLength = 18f;  // Z
        public const float RoomHeight = 4.4f;
        public const float TableHeight = 0.86f;

        public static readonly Vector3 TableCenter = new Vector3(0f, 0f, 1.5f);

        private enum ColliderMode
        {
            Keep,
            Mesh,
            None
        }

        public sealed class Result
        {
            public GameObject Root;
            public Transform Table;
        }

        public static Result Build(PrototypeMaterials materials)
        {
            var result = new Result();
            var root = new GameObject("Casino_RouletteRoom");
            result.Root = root;

            BuildRoom(root.transform, materials);
            result.Table = BuildTable(root.transform, materials);
            BuildGameMaster(root.transform, materials);
            BuildLighting(root.transform, materials);
            ApplyAtmosphere();

            return result;
        }

        private static void BuildRoom(Transform parent, PrototypeMaterials m)
        {
            Transform room = Group("Room", parent);
            float halfW = RoomWidth * 0.5f;
            float halfL = RoomLength * 0.5f;

            Box("Floor", room, new Vector3(0f, -0.1f, 0f), new Vector3(RoomWidth, 0.2f, RoomLength), m.BlackMarble);
            Box("Ceiling", room, new Vector3(0f, RoomHeight + 0.1f, 0f), new Vector3(RoomWidth, 0.2f, RoomLength), m.Ceiling);

            // Walls: velvet upper, wood wainscot, gold trim strip.
            Wall(room, m, "Wall_North", new Vector3(0f, 0f, halfL), new Vector3(RoomWidth, 0f, 0f), Vector3.back);
            Wall(room, m, "Wall_South", new Vector3(0f, 0f, -halfL), new Vector3(RoomWidth, 0f, 0f), Vector3.forward);
            Wall(room, m, "Wall_East", new Vector3(halfW, 0f, 0f), new Vector3(0f, 0f, RoomLength), Vector3.left);
            Wall(room, m, "Wall_West", new Vector3(-halfW, 0f, 0f), new Vector3(0f, 0f, RoomLength), Vector3.right);

            // Pillars frame the table area.
            float[] xs = { -4.5f, 4.5f };
            float[] zs = { -4.5f, 5.5f };
            foreach (float x in xs)
            {
                foreach (float z in zs)
                {
                    Transform pillar = Group("Pillar", room);
                    pillar.localPosition = new Vector3(x, 0f, z);
                    Box("Shaft", pillar, new Vector3(0f, RoomHeight * 0.5f, 0f), new Vector3(0.6f, RoomHeight, 0.6f), m.DarkWood);
                    Box("Base", pillar, new Vector3(0f, 0.12f, 0f), new Vector3(0.78f, 0.24f, 0.78f), m.Gold);
                    Box("Capital", pillar, new Vector3(0f, RoomHeight - 0.15f, 0f), new Vector3(0.78f, 0.3f, 0.78f), m.Gold);
                }
            }

            // Velvet carpet under the table.
            Box("Carpet", room, new Vector3(TableCenter.x, 0.005f, TableCenter.z), new Vector3(6.5f, 0.01f, 5.5f), m.RedVelvet, ColliderMode.None);
        }

        private static void Wall(Transform parent, PrototypeMaterials m, string name, Vector3 center, Vector3 span, Vector3 inward)
        {
            Transform wall = Group(name, parent);
            const float thickness = 0.3f;
            bool alongX = Mathf.Abs(span.x) > 0f;
            float length = alongX ? span.x : span.z;

            Vector3 Size(float height, float depth)
            {
                return alongX ? new Vector3(length, height, depth) : new Vector3(depth, height, length);
            }

            Vector3 outward = -inward * (thickness * 0.5f);
            Box("Velvet", wall, center + outward + Vector3.up * RoomHeight * 0.5f, Size(RoomHeight, thickness), m.RedVelvet);
            Box("Wainscot", wall, center + inward * 0.03f + Vector3.up * 0.55f, Size(1.1f, 0.06f), m.DarkWood, ColliderMode.None);
            Box("Trim", wall, center + inward * 0.07f + Vector3.up * 1.12f, Size(0.05f, 0.04f), m.Gold, ColliderMode.None);
            Box("CrownMoulding", wall, center + inward * 0.06f + Vector3.up * (RoomHeight - 0.12f), Size(0.24f, 0.12f), m.DarkWood, ColliderMode.None);
        }

        private static Transform BuildTable(Transform parent, PrototypeMaterials m)
        {
            Transform table = Group("RouletteTable", parent);
            table.localPosition = TableCenter;

            // Structure.
            Box("Pedestal", table, new Vector3(0f, 0.38f, 0f), new Vector3(2.7f, 0.76f, 1.0f), m.DarkWood);
            Box("Top", table, new Vector3(0f, TableHeight - 0.05f, 0f), new Vector3(3.5f, 0.08f, 1.7f), m.DarkWood);
            Box("Felt", table, new Vector3(0f, TableHeight - 0.005f, 0f), new Vector3(3.3f, 0.01f, 1.5f), m.Felt);
            Box("Rail_Near", table, new Vector3(0f, TableHeight + 0.02f, -0.8f), new Vector3(3.5f, 0.06f, 0.1f), m.DarkWood);
            Box("Rail_Far", table, new Vector3(0f, TableHeight + 0.02f, 0.8f), new Vector3(3.5f, 0.06f, 0.1f), m.DarkWood);
            Box("Rail_Left", table, new Vector3(-1.7f, TableHeight + 0.02f, 0f), new Vector3(0.1f, 0.06f, 1.7f), m.DarkWood);
            Box("Rail_Right", table, new Vector3(1.7f, TableHeight + 0.02f, 0f), new Vector3(0.1f, 0.06f, 1.7f), m.DarkWood);
            Box("Rail_Trim", table, new Vector3(0f, TableHeight + 0.055f, -0.8f), new Vector3(3.5f, 0.01f, 0.03f), m.Gold, ColliderMode.None);

            // Wheel placeholder. Phase 2/3 replaces this with the procedural wheel.
            Transform wheel = Group("Wheel_Placeholder", table);
            wheel.localPosition = new Vector3(-1.12f, TableHeight, 0f);
            Renderer bowl = Cylinder("Bowl", wheel, new Vector3(0f, 0.04f, 0f), new Vector3(0.95f, 0.04f, 0.95f), m.DarkWood);
            Renderer rotor = Cylinder("Rotor", wheel, new Vector3(0f, 0.085f, 0f), new Vector3(0.68f, 0.012f, 0.68f), m.Gold);
            Renderer cone = Cylinder("Cone", wheel, new Vector3(0f, 0.1f, 0f), new Vector3(0.3f, 0.015f, 0.3f), m.DarkWood);
            Renderer turret = Cylinder("Turret", wheel, new Vector3(0f, 0.15f, 0f), new Vector3(0.05f, 0.05f, 0.05f), m.Gold);
            var wheelInspect = wheel.gameObject.AddComponent<InspectableObject>();
            wheelInspect.Configure("Inspect the wheel", bowl, rotor, cone, turret);
            wheelInspect.SetText("The Wheel",
                "Polished wood and gold. The pockets have no numbers yet, and the rotor is cold to the touch. " +
                "Something underneath it is built to move.");

            // Betting layout placeholder: 3 rows x 12 columns plus a zero column, outlined in gold.
            Transform layout = Group("BettingLayout_Placeholder", table);
            layout.localPosition = new Vector3(0.45f, TableHeight + 0.002f, 0.05f);
            const float layoutWidth = 2.1f;
            const float layoutDepth = 0.9f;
            Renderer layoutSurface = Box("Surface", layout, Vector3.zero, new Vector3(layoutWidth, 0.004f, layoutDepth), m.FeltLayout);
            BuildLayoutGrid(layout, m, layoutWidth, layoutDepth);
            var layoutInspect = layout.gameObject.AddComponent<InspectableObject>();
            layoutInspect.Configure("Inspect the betting layout", layoutSurface);
            layoutInspect.SetText("The Betting Layout",
                "Thirty-six boxes and a zero, ruled in gold leaf. The felt is worn smooth where others have leaned in to place their chips.");

            // Chip tray in front of the player's seat.
            Transform tray = Group("ChipTray", table);
            tray.localPosition = new Vector3(0.45f, TableHeight + 0.01f, -0.6f);
            Renderer trayBox = Box("Tray", tray, Vector3.zero, new Vector3(0.6f, 0.03f, 0.14f), m.DarkWood);
            Box("TrayLining", tray, new Vector3(0f, 0.012f, 0f), new Vector3(0.56f, 0.01f, 0.1f), m.RedVelvet, ColliderMode.None);
            var trayInspect = tray.gameObject.AddComponent<InspectableObject>();
            trayInspect.Configure("Inspect the chip tray", trayBox);
            trayInspect.SetText("Your Chip Tray", "Empty for now. The house will issue your chips when the first round begins.");

            // The player's side of the table: an empty stool edge in the foreground of the view.
            Transform seat = Group("PlayerStool", table);
            seat.localPosition = new Vector3(0.45f, 0f, -1.25f);
            Box("Stool", seat, new Vector3(0f, 0.62f, 0f), new Vector3(0.45f, 0.06f, 0.45f), m.RedVelvet);
            Box("StoolLeg", seat, new Vector3(0f, 0.3f, 0f), new Vector3(0.08f, 0.6f, 0.08f), m.Gold);

            return table;
        }

        private static void BuildLayoutGrid(Transform layout, PrototypeMaterials m, float width, float depth)
        {
            const int columns = 12;
            const int rows = 3;
            const float line = 0.008f;
            float zeroWidth = width / (columns + 1);
            float left = -width * 0.5f;
            float y = 0.004f;

            for (int c = 0; c <= columns + 1; c++)
            {
                float x = left + c * zeroWidth;
                Box("Line_V" + c, layout, new Vector3(x, y, 0f), new Vector3(line, 0.002f, depth), m.Gold, ColliderMode.None);
            }
            for (int r = 0; r <= rows; r++)
            {
                float z = -depth * 0.5f + r * (depth / rows);
                // The zero column spans all rows, so inner horizontal lines start after it.
                bool outer = r == 0 || r == rows;
                float start = outer ? left : left + zeroWidth;
                float span = width - (start - left);
                Box("Line_H" + r, layout, new Vector3(start + span * 0.5f, y, z), new Vector3(span, 0.002f, line), m.Gold, ColliderMode.None);
            }
        }

        private static void BuildGameMaster(Transform parent, PrototypeMaterials m)
        {
            Transform gm = Group("GameMaster_Placeholder", parent);
            gm.localPosition = TableCenter + new Vector3(0.2f, 0f, 1.75f);
            gm.localRotation = Quaternion.Euler(0f, 180f, 0f);

            Renderer body = Capsule("Body", gm, new Vector3(0f, 1.05f, 0f), new Vector3(0.7f, 1.05f, 0.55f), m.Shadow);
            Renderer head = Sphere("Head", gm, new Vector3(0f, 2.28f, 0f), Vector3.one * 0.34f, m.Shadow);
            Sphere("Eye_L", gm, new Vector3(-0.07f, 2.31f, 0.15f), Vector3.one * 0.035f, m.EyeGlow, ColliderMode.None);
            Sphere("Eye_R", gm, new Vector3(0.07f, 2.31f, 0.15f), Vector3.one * 0.035f, m.EyeGlow, ColliderMode.None);
            // Forearms resting on the far rail: from above, his hands are always at the edge of the table.
            Box("Forearm_L", gm, new Vector3(-0.32f, 0.97f, 0.55f), new Vector3(0.11f, 0.1f, 0.6f), m.Shadow, ColliderMode.None);
            Box("Forearm_R", gm, new Vector3(0.32f, 0.97f, 0.55f), new Vector3(0.11f, 0.1f, 0.6f), m.Shadow, ColliderMode.None);
            Box("Hand_L", gm, new Vector3(-0.3f, 0.95f, 0.9f), new Vector3(0.11f, 0.05f, 0.16f), m.Shadow, ColliderMode.None);
            Box("Hand_R", gm, new Vector3(0.3f, 0.95f, 0.9f), new Vector3(0.11f, 0.05f, 0.16f), m.Shadow, ColliderMode.None);
            Box("Ring", gm, new Vector3(0.3f, 0.98f, 0.93f), new Vector3(0.03f, 0.02f, 0.03f), m.Gold, ColliderMode.None);

            var inspect = gm.gameObject.AddComponent<InspectableObject>();
            inspect.Configure("Look at the figure", body, head);
            inspect.SetText("The Game Master",
                "He has not moved since you sat down. His hands rest on the far edge of the table, one gold ring tapping slowly. When you look up, he smiles, as if you have already agreed to something.");
        }

        private static void BuildLighting(Transform parent, PrototypeMaterials m)
        {
            Transform lights = Group("Lighting", parent);

            // Key light: a tight warm spot on the table so it is the brightest thing in the room.
            var key = new GameObject("TableSpot").AddComponent<Light>();
            key.transform.SetParent(lights, false);
            key.transform.localPosition = TableCenter + Vector3.up * (RoomHeight - 0.3f);
            key.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            key.type = LightType.Spot;
            key.spotAngle = 70f;
            key.innerSpotAngle = 45f;
            key.range = 7f;
            key.intensity = 12f;
            key.color = new Color(1f, 0.86f, 0.66f);
            key.shadows = LightShadows.Soft;

            // Rim light behind the Game Master so his silhouette separates from the dark.
            var rim = new GameObject("GameMasterRim").AddComponent<Light>();
            rim.transform.SetParent(lights, false);
            rim.transform.localPosition = TableCenter + new Vector3(0.2f, 2.6f, 3.2f);
            rim.transform.LookAt(TableCenter + new Vector3(0.2f, 1.6f, 1.75f));
            rim.type = LightType.Spot;
            rim.spotAngle = 50f;
            rim.range = 5f;
            rim.intensity = 2.5f;
            rim.color = new Color(0.75f, 0.25f, 0.2f);
            rim.shadows = LightShadows.None;

            // The chandeliers hang off to the sides so they never block the top-down view.
            Chandelier(lights, m, new Vector3(-4f, RoomHeight - 0.7f, 1.5f), 1.2f);
            Chandelier(lights, m, new Vector3(4f, RoomHeight - 0.7f, 1.5f), 1.2f);

            // Wall sconces.
            float halfW = RoomWidth * 0.5f - 0.2f;
            float[] zs = { -3f, 2f, 6.5f };
            foreach (float z in zs)
            {
                Sconce(lights, m, new Vector3(-halfW, 2.1f, z));
                Sconce(lights, m, new Vector3(halfW, 2.1f, z));
            }
        }

        private static void Chandelier(Transform parent, PrototypeMaterials m, Vector3 position, float intensity)
        {
            Transform chandelier = Group("Chandelier", parent);
            chandelier.localPosition = position;
            Cylinder("Ring", chandelier, Vector3.zero, new Vector3(1.1f, 0.02f, 1.1f), m.Gold, ColliderMode.None);
            Box("Chain", chandelier, new Vector3(0f, 0.45f, 0f), new Vector3(0.03f, 0.9f, 0.03f), m.Gold, ColliderMode.None);
            const int candles = 8;
            for (int i = 0; i < candles; i++)
            {
                float angle = i * Mathf.PI * 2f / candles;
                var offset = new Vector3(Mathf.Cos(angle) * 0.52f, 0.06f, Mathf.Sin(angle) * 0.52f);
                Sphere("Candle" + i, chandelier, offset, Vector3.one * 0.06f, m.CandleGlow, ColliderMode.None);
            }
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(chandelier, false);
            light.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = intensity;
            light.color = new Color(1f, 0.72f, 0.45f);
            light.shadows = LightShadows.None;
        }

        private static void Sconce(Transform parent, PrototypeMaterials m, Vector3 position)
        {
            Transform sconce = Group("Sconce", parent);
            sconce.localPosition = position;
            Box("Plate", sconce, Vector3.zero, new Vector3(0.12f, 0.3f, 0.12f), m.Gold, ColliderMode.None);
            Sphere("Flame", sconce, new Vector3(0f, 0.2f, 0f), Vector3.one * 0.07f, m.CandleGlow, ColliderMode.None);
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(sconce, false);
            light.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            light.type = LightType.Point;
            light.range = 3.5f;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.6f, 0.32f);
            light.shadows = LightShadows.None;
        }

        private static void ApplyAtmosphere()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.04f, 0.03f, 0.03f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.02f, 0.014f, 0.014f);
            RenderSettings.fogDensity = 0.09f;
        }

        // ---------- primitive helpers ----------

        private static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Renderer Box(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, ColliderMode collider = ColliderMode.Keep)
        {
            return Primitive(PrimitiveType.Cube, name, parent, localPosition, scale, material, collider);
        }

        /// <summary>Cylinders default to a MeshCollider: Unity's capsule collider balloons on flat, wide cylinders.</summary>
        private static Renderer Cylinder(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, ColliderMode collider = ColliderMode.Mesh)
        {
            return Primitive(PrimitiveType.Cylinder, name, parent, localPosition, scale, material, collider);
        }

        private static Renderer Sphere(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, ColliderMode collider = ColliderMode.Keep)
        {
            return Primitive(PrimitiveType.Sphere, name, parent, localPosition, scale, material, collider);
        }

        private static Renderer Capsule(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, ColliderMode collider = ColliderMode.Keep)
        {
            return Primitive(PrimitiveType.Capsule, name, parent, localPosition, scale, material, collider);
        }

        private static Renderer Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, ColliderMode colliderMode)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;

            if (colliderMode != ColliderMode.Keep)
            {
                Collider existing = go.GetComponent<Collider>();
                if (existing != null) DestroySafe(existing);
                if (colliderMode == ColliderMode.Mesh)
                {
                    var meshCollider = go.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                }
            }

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static void DestroySafe(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
