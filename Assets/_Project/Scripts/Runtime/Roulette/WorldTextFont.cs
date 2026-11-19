using UnityEngine;

namespace DeadSpin.Runtime.Roulette
{
    /// <summary>Locates Unity's built-in font for TextMesh labels (renamed in Unity 2022.2).</summary>
    public static class WorldTextFont
    {
        private static Font _font;

        /// <summary>Global correction if labels come out larger or smaller than intended on a given Unity version.</summary>
        public static float LabelScale = 1f;

        public static Font Get()
        {
            if (_font != null) return _font;
            _font = TryLoad("LegacyRuntime.ttf") ?? TryLoad("Arial.ttf");
            if (_font == null) Debug.LogError("[WorldTextFont] No built-in font found for table labels.");
            return _font;
        }

        private static Font TryLoad(string name)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(name);
            }
            catch (System.ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Creates a flat label lying on a surface, readable from above with its top towards +Z of <paramref name="parent"/>.</summary>
        public static TextMesh CreateFlatLabel(Transform parent, string name, string text, Vector3 localPosition, float yawDegrees, float height, Color color, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(90f, yawDegrees, 0f);

            var mesh = go.AddComponent<TextMesh>();
            Font font = Get();
            mesh.font = font;
            mesh.text = text;
            mesh.fontSize = 64;
            // A TextMesh line is about fontSize * characterSize / 10 world units tall. A high font size
            // keeps glyphs crisp; characterSize then brings the label to the requested height.
            mesh.characterSize = height * 10f / mesh.fontSize * LabelScale;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material != null ? material : (font != null ? font.material : null);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return mesh;
        }
    }
}
