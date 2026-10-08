using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RacingSim.Core
{
    /// <summary>
    /// Создаёт материалы в рантайме, работает и со встроенным рендером (Standard),
    /// и с URP (Universal Render Pipeline/Lit), если он подключён.
    /// </summary>
    public static class MaterialFactory
    {
        static Shader litShader;
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        static Shader Lit
        {
            get
            {
                if (litShader != null) return litShader;
                if (GraphicsSettings.currentRenderPipeline != null)
                    litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (litShader == null) litShader = Shader.Find("Standard");
                if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (litShader == null) litShader = Shader.Find("Unlit/Color");
                return litShader;
            }
        }

        public static Material Create(string key, Color color, float smoothness = 0.3f, float metallic = 0f,
            Texture2D texture = null, Vector2? tiling = null, Color? emission = null)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var m = new Material(Lit) { name = key };
            SetColor(m, color);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (texture != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
                var t = tiling ?? Vector2.one;
                if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", t);
                if (m.HasProperty("_MainTex")) m.SetTextureScale("_MainTex", t);
            }
            if (emission.HasValue && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
            }
            Cache[key] = m;
            return m;
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>Процедурная шумовая текстура (асфальт, трава, гравий).</summary>
        public static Texture2D NoiseTexture(string key, Color a, Color b, float scale, int size = 256, bool stripes = false)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = key,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            var px = new Color[size * size];
            var rnd = new System.Random(key.GetHashCode());
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // тайлящийся шум: сумма перлина по тору + мелкое зерно
                float u = (float)x / size, v = (float)y / size;
                float n = TileNoise(u, v, scale) * 0.7f + TileNoise(u, v, scale * 4f) * 0.3f;
                n += ((float)rnd.NextDouble() - 0.5f) * 0.25f;
                if (stripes) n = n * 0.4f + (Mathf.Sin(v * Mathf.PI * 2f * 4f) > 0f ? 0.6f : 0f);
                px[y * size + x] = Color.Lerp(a, b, Mathf.Clamp01(n));
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static float TileNoise(float u, float v, float scale)
        {
            float a = u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
            float r = scale / (Mathf.PI * 2f);
            // 2D-тор → 2 пары координат, усреднённые
            float n1 = Mathf.PerlinNoise(Mathf.Cos(a) * r + 100f, Mathf.Sin(a) * r + 100f);
            float n2 = Mathf.PerlinNoise(Mathf.Cos(b) * r + 300f, Mathf.Sin(b) * r + 300f);
            float n3 = Mathf.PerlinNoise(Mathf.Cos(a) * r + Mathf.Cos(b) * r + 500f, Mathf.Sin(a) * r + Mathf.Sin(b) * r + 500f);
            return (n1 + n2 + 2f * n3) * 0.25f;
        }
    }
}
