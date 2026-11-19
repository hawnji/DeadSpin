using System;
using System.Collections.Generic;
using UnityEngine;
using DeadSpin.Runtime.Roulette;
using UnityEngine.Rendering;

namespace DeadSpin.Runtime.Environment
{
    /// <summary>
    /// Creates the greybox material palette (black marble, dark wood, red velvet, gold, felt).
    /// Works with URP (preferred) and falls back to the built-in Standard shader. An optional
    /// persist callback lets the editor tool save each material as an asset.
    /// </summary>
    public sealed class PrototypeMaterials
    {
        private static readonly string[] LitShaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Standard"
        };

        private readonly Func<Material, Material> _persist;
        private readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();
        private readonly Shader _shader;

        public PrototypeMaterials(Func<Material, Material> persist = null)
        {
            _persist = persist;
            _shader = FindLitShader();
        }

        public Material BlackMarble => Get("BlackMarble", new Color(0.035f, 0.035f, 0.04f), 0.88f, 0f);
        public Material DarkWood => Get("DarkWood", new Color(0.13f, 0.07f, 0.04f), 0.45f, 0f);
        public Material RedVelvet => Get("RedVelvet", new Color(0.26f, 0.025f, 0.03f), 0.12f, 0f);
        public Material Gold => Get("Gold", new Color(0.78f, 0.58f, 0.26f), 0.75f, 1f);
        public Material Felt => Get("TableFelt", new Color(0.04f, 0.17f, 0.10f), 0.05f, 0f);
        public Material FeltLayout => Get("TableFeltLayout", new Color(0.06f, 0.22f, 0.13f), 0.05f, 0f);
        public Material Ceiling => Get("Ceiling", new Color(0.03f, 0.02f, 0.02f), 0.2f, 0f);
        public Material Shadow => Get("ShadowCloth", new Color(0.015f, 0.012f, 0.012f), 0.1f, 0f);
        // Wheel materials render both faces so procedural geometry never shows holes.
        public Material PocketRed => DoubleSided(Get("PocketRed", new Color(0.42f, 0.03f, 0.03f), 0.55f, 0f));
        public Material PocketBlack => DoubleSided(Get("PocketBlack", new Color(0.02f, 0.02f, 0.022f), 0.6f, 0f));
        public Material PocketGreen => DoubleSided(Get("PocketGreen", new Color(0.02f, 0.25f, 0.1f), 0.55f, 0f));
        public Material PocketFloor => DoubleSided(Get("PocketFloor", new Color(0.09f, 0.05f, 0.03f), 0.5f, 0f));
        public Material FretGold => DoubleSided(Get("FretGold", new Color(0.8f, 0.6f, 0.28f), 0.8f, 1f));
        public Material ChipRed => Get("ChipRed", new Color(0.55f, 0.06f, 0.05f), 0.5f, 0f);
        public Material ChipBlue => Get("ChipBlue", new Color(0.08f, 0.16f, 0.45f), 0.5f, 0f);
        public Material ChipGreen => Get("ChipGreen", new Color(0.05f, 0.35f, 0.15f), 0.5f, 0f);
        public Material ChipBlack => Get("ChipBlack", new Color(0.05f, 0.05f, 0.05f), 0.5f, 0f);
        public Material ChipPurple => Get("ChipPurple", new Color(0.32f, 0.08f, 0.4f), 0.5f, 0f);
        public Material Ivory => Get("Ivory", new Color(0.92f, 0.9f, 0.84f), 0.9f, 0f);
        public Material CandleGlow => GetEmissive("CandleGlow", new Color(1f, 0.72f, 0.38f), 3.5f);
        public Material EyeGlow => GetEmissive("EyeGlow", new Color(1f, 0.15f, 0.08f), 6f);

        /// <summary>
        /// Depth-tested unlit material for TextMesh labels, bound to the built-in font's texture.
        /// Falls back to Unity's font material (which ignores depth) if the shader is missing.
        /// </summary>
        public Material WorldText
        {
            get
            {
                Material material;
                if (_cache.TryGetValue("WorldText", out material)) return material;
                Font font = WorldTextFont.Get();
                Shader shader = Shader.Find("DeadSpin/WorldText");
                if (shader == null || font == null)
                {
                    Debug.LogWarning("[PrototypeMaterials] DeadSpin/WorldText shader or built-in font missing; labels will use the default font material.");
                    return font != null ? font.material : null;
                }
                material = new Material(shader) { name = "Proto_WorldText" };
                material.mainTexture = font.material.mainTexture;
                material.color = Color.white;
                return Store("WorldText", material);
            }
        }

        private Material Get(string name, Color color, float smoothness, float metallic)
        {
            Material material;
            if (_cache.TryGetValue(name, out material)) return material;

            material = new Material(_shader) { name = "Proto_" + name };
            material.color = color;
            SetIfPresent(material, "_Smoothness", smoothness); // URP
            SetIfPresent(material, "_Glossiness", smoothness); // Built-in
            SetIfPresent(material, "_Metallic", metallic);
            // Enables the emission channel so focus highlights (Interactable) can glow.
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            return Store(name, material);
        }

        private Material GetEmissive(string name, Color color, float intensity)
        {
            Material material;
            if (_cache.TryGetValue(name, out material)) return material;

            material = new Material(_shader) { name = "Proto_" + name };
            material.color = color * 0.5f;
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * intensity);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return Store(name, material);
        }

        private Material Store(string name, Material material)
        {
            if (_persist != null)
            {
                Material persisted = _persist(material);
                if (persisted != null) material = persisted;
            }
            _cache[name] = material;
            return material;
        }

        private static Material DoubleSided(Material material)
        {
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f); // URP Lit: Render Face = Both
            material.doubleSidedGI = true;
            return material;
        }

        private static void SetIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static Shader FindLitShader()
        {
            bool usingSrp = GraphicsSettings.currentRenderPipeline != null;
            foreach (string shaderName in LitShaderNames)
            {
                if (!usingSrp && shaderName.StartsWith("Universal", StringComparison.Ordinal)) continue;
                Shader shader = Shader.Find(shaderName);
                if (shader != null) return shader;
            }

            Shader fallback = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            if (fallback == null)
            {
                throw new InvalidOperationException("[PrototypeMaterials] No lit shader found. Install URP or enable the built-in render pipeline.");
            }
            Debug.LogWarning("[PrototypeMaterials] Preferred lit shader not found; using " + fallback.name + ".");
            return fallback;
        }
    }
}
