using UnityEngine;

namespace DeadSpin.Runtime.UI
{
    /// <summary>
    /// Shared IMGUI look for the prototype HUDs: dark translucent panels, gold headings, bone text.
    /// Create inside OnGUI (GUI.skin is only valid there) and dispose when the owner is destroyed.
    /// Also owns the resolution scaling so every HUD lays out in 1080p reference units.
    /// </summary>
    public sealed class HudSkin
    {
        public const float ReferenceHeight = 1080f;

        public static readonly Color Gold = new Color(0.86f, 0.70f, 0.38f);
        public static readonly Color Bone = new Color(0.90f, 0.87f, 0.80f);
        public static readonly Color Blood = new Color(0.80f, 0.22f, 0.20f);
        public static readonly Color Win = new Color(0.55f, 0.85f, 0.45f);
        public static readonly Color PocketRed = new Color(0.62f, 0.08f, 0.07f);
        public static readonly Color PocketBlack = new Color(0.08f, 0.08f, 0.08f);
        public static readonly Color PocketGreen = new Color(0.05f, 0.42f, 0.18f);

        public GUIStyle Panel { get; }
        public GUIStyle Label { get; }
        public GUIStyle Value { get; }
        public GUIStyle Title { get; }
        public GUIStyle Banner { get; }
        public GUIStyle Prompt { get; }
        public GUIStyle PromptShadow { get; }
        public GUIStyle Tab { get; }
        public GUIStyle TabActive { get; }
        public GUIStyle Button { get; }
        public GUIStyle Small { get; }
        public GUIStyle SmallCentered { get; }
        public GUIStyle Badge { get; }

        public Texture2D PanelTexture { get; }
        public Texture2D ButtonTexture { get; }
        public Texture2D ButtonHoverTexture { get; }

        public HudSkin()
        {
            PanelTexture = SolidTexture(new Color(0.05f, 0.03f, 0.03f, 0.82f));
            ButtonTexture = SolidTexture(new Color(0.22f, 0.07f, 0.07f, 0.95f));
            ButtonHoverTexture = SolidTexture(new Color(0.38f, 0.12f, 0.10f, 0.95f));

            Panel = new GUIStyle(GUI.skin.box);
            Panel.normal.background = PanelTexture;
            Panel.border = new RectOffset(0, 0, 0, 0);

            Label = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            Label.normal.textColor = Gold;

            Value = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleRight };
            Value.normal.textColor = Bone;

            Title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true };
            Title.normal.textColor = Gold;

            Banner = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Banner.normal.textColor = Gold;

            Small = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            Small.normal.textColor = Bone;

            SmallCentered = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter };

            Prompt = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold, wordWrap = false };
            Prompt.normal.textColor = Gold;
            PromptShadow = new GUIStyle(Prompt);
            PromptShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);

            Button = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            Button.normal.background = ButtonTexture;
            Button.hover.background = ButtonHoverTexture;
            Button.active.background = ButtonHoverTexture;
            Button.normal.textColor = Bone;
            Button.hover.textColor = Gold;
            Button.active.textColor = Gold;

            Tab = new GUIStyle(GUI.skin.button) { fontSize = 17 };
            Tab.normal.background = PanelTexture;
            Tab.hover.background = ButtonHoverTexture;
            Tab.active.background = ButtonHoverTexture;
            Tab.normal.textColor = new Color(Bone.r, Bone.g, Bone.b, 0.7f);
            Tab.hover.textColor = Gold;
            TabActive = new GUIStyle(Tab) { fontStyle = FontStyle.Bold };
            TabActive.normal.background = ButtonTexture;
            TabActive.normal.textColor = Gold;

            Badge = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Badge.normal.textColor = Color.white;
        }

        /// <summary>UI scale for the current screen: 1 at 1080p, times the player's UI scale setting.</summary>
        public static float Scale(float uiScale)
        {
            return Screen.height / ReferenceHeight * uiScale;
        }

        /// <summary>Converts a screen-pixel pointer (origin bottom-left) into scaled GUI space (origin top-left).</summary>
        public static Vector2 PointerToGui(Vector2 pointer, float scale)
        {
            return new Vector2(pointer.x / scale, (Screen.height - pointer.y) / scale);
        }

        public static void DrawSolid(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public void Dispose()
        {
            Destroy(PanelTexture);
            Destroy(ButtonTexture);
            Destroy(ButtonHoverTexture);
        }

        private static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void Destroy(Texture2D texture)
        {
            if (texture != null) Object.Destroy(texture);
        }
    }
}
