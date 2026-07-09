// Importador de archivos .md9 de R2Beat para Unity (ScriptedImporter).
//
// Al colocar un .md9 dentro de Assets, Unity genera automáticamente:
//   - Un prefab con la jerarquía de partes/huesos (parentId) y sus transforms.
//   - Un Mesh por parte (vértices locales, normales, UVs, índices).
//   - Un Material por material MD9 (URP Unlit/Lit o Standard como fallback),
//     con doble cara y manejo de alpha igual que el visor web.
//   - Las texturas .dds referenciadas por los materiales, decodificadas
//     (DXT1/3/5 o 32bpp) e incrustadas como sub-assets.
//   - Un AnimationClip por cada .ani de la misma carpeta (el md9 "busca" sus
//     compañeros igual que la herramienta web), con curvas de posición,
//     rotación y escala por hueso a 90 fps.
//
// Si el .ani o los .dds cambian en disco, el .md9 se reimporta solo
// (DependsOnSourceAsset).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace R2Beat.ImporterEditor
{
    [ScriptedImporter(3, "md9")]
    public class Md9Importer : ScriptedImporter
    {
        public enum ShaderMode
        {
            UnlitAutomatico,   // como el visor web (MeshBasicMaterial)
            LitAutomatico      // con iluminación
        }

        public enum AniDiscovery
        {
            TodosEnLaCarpeta,      // importa todos los .ani de la carpeta del .md9
            SoloMismoNombreBase    // solo front.ani para front.md9
        }

        [Header("Modelo")]
        [Tooltip("Factor de escala aplicado a la raíz. Los mapas de R2Beat usan unidades grandes; prueba 0.01 si el modelo resulta enorme en metros de Unity.")]
        public float escala = 1f;

        [Header("Materiales")]
        public ShaderMode shader = ShaderMode.UnlitAutomatico;
        [Tooltip("Comprimir texturas al importar (ASTC en Android/iOS, DXT en escritorio). Requiere dimensiones múltiplos de 4.")]
        public bool comprimirTexturas = false;

        [Header("Animaciones")]
        public bool importarAnimaciones = true;
        public AniDiscovery buscarAni = AniDiscovery.TodosEnLaCarpeta;
        [Tooltip("Clips 'Legacy' + componente Animation en la raíz (reproducción inmediata). Desactiva para usar Animator/AnimatorController.")]
        public bool animacionLegacy = true;
        public bool animacionEnLoop = true;
        [Tooltip("Reproducir automáticamente el primer clip al entrar en Play (solo Legacy).")]
        public bool reproducirAlIniciar = true;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string assetPath = ctx.assetPath;
            string directory = Path.GetDirectoryName(assetPath);
            string baseName = Path.GetFileNameWithoutExtension(assetPath);

            Md9Model model;
            try
            {
                model = Md9Parser.Parse(File.ReadAllBytes(assetPath), baseName);
            }
            catch (Exception e)
            {
                ctx.LogImportError($"No se pudo leer el MD9 '{assetPath}': {e.Message}");
                return;
            }

            // ---------- Raíz y jerarquía ----------
            var root = new GameObject(baseName);
            float rootScale = Mathf.Approximately(escala, 0f) ? 1f : escala;
            root.transform.localScale = Vector3.one * rootScale;

            var nodes = new GameObject[model.Parts.Count];
            for (int i = 0; i < model.Parts.Count; i++)
                nodes[i] = new GameObject(model.Parts[i].Name);

            for (int i = 0; i < model.Parts.Count; i++)
            {
                var part = model.Parts[i];
                Transform parent = root.transform;
                if (part.ParentId >= 0 && part.ParentId < model.Parts.Count && part.ParentId != i
                    && !CreatesCycle(model, i))
                {
                    parent = nodes[part.ParentId].transform;
                }
                nodes[i].transform.SetParent(parent, worldPositionStays: false);
                ApplyLocalMatrix(nodes[i].transform, part.LocalMatrix);
            }

            // ---------- Texturas ----------
            var textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            var textureHasAlpha = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            _texturesByPath.Clear();

            // ---------- Materiales ----------
            var materials = new Material[Mathf.Max(model.Materials.Count, 1)];
            if (model.Materials.Count == 0)
            {
                materials[0] = CreateMaterial(ctx, new Md9Material(), 0, directory, textureCache, textureHasAlpha);
            }
            else
            {
                for (int i = 0; i < model.Materials.Count; i++)
                    materials[i] = CreateMaterial(ctx, model.Materials[i], i, directory, textureCache, textureHasAlpha);
            }

            // ---------- Meshes ----------
            for (int i = 0; i < model.Parts.Count; i++)
            {
                var part = model.Parts[i];
                if (part.VertexCount <= 0 || part.FaceCount <= 0) continue; // hueso sin geometría

                var mesh = new Mesh { name = $"{part.Name}" };
                if (part.VertexCount > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = part.Vertices;
                mesh.uv = part.Uvs;
                if (HasValidNormals(part.Normals)) mesh.normals = part.Normals;
                mesh.triangles = part.Indices;
                if (mesh.normals == null || mesh.normals.Length == 0) mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                ctx.AddObjectToAsset($"mesh_{i}_{part.Name}", mesh);

                var filter = nodes[i].AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = nodes[i].AddComponent<MeshRenderer>();
                int materialId = (part.MaterialId >= 0 && part.MaterialId < materials.Length) ? part.MaterialId : 0;
                renderer.sharedMaterial = materials[materialId];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            // ---------- Animaciones (.ani en la misma carpeta) ----------
            var clips = new List<AnimationClip>();
            if (importarAnimaciones && !string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                // Mapa nombre -> nodo (como la herramienta web: si hay nombres
                // duplicados, gana el último).
                var nodesByName = new Dictionary<string, Transform>();
                for (int i = 0; i < model.Parts.Count; i++)
                    nodesByName[model.Parts[i].Name] = nodes[i].transform;

                IEnumerable<string> aniPaths = Directory.GetFiles(directory)
                    .Where(p => p.EndsWith(".ani", StringComparison.OrdinalIgnoreCase));
                if (buscarAni == AniDiscovery.SoloMismoNombreBase)
                {
                    aniPaths = aniPaths.Where(p =>
                        string.Equals(Path.GetFileNameWithoutExtension(p), baseName, StringComparison.OrdinalIgnoreCase));
                }

                foreach (string aniPath in aniPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    RegisterDependency(ctx, aniPath);
                    AniClip ani;
                    try
                    {
                        ani = AniParser.Parse(File.ReadAllBytes(aniPath), Path.GetFileNameWithoutExtension(aniPath));
                    }
                    catch (Exception e)
                    {
                        ctx.LogImportWarning($"No se pudo leer '{Path.GetFileName(aniPath)}': {e.Message}");
                        continue;
                    }

                    AnimationClip clip = BuildClip(ani, root.transform, nodesByName);
                    ctx.AddObjectToAsset($"clip_{clip.name}", clip);
                    clips.Add(clip);
                }
            }

            if (clips.Count > 0)
            {
                if (animacionLegacy)
                {
                    var animation = root.AddComponent<Animation>();
                    animation.clip = clips[0];
                    foreach (var clip in clips) animation.AddClip(clip, clip.name);
                    animation.playAutomatically = reproducirAlIniciar;
                    animation.cullingType = AnimationCullingType.AlwaysAnimate;
                }
                else
                {
                    root.AddComponent<Animator>(); // asigna tu AnimatorController con los clips generados
                }
            }

            ctx.AddObjectToAsset("root", root);
            ctx.SetMainObject(root);
        }

        // ==================== Transforms ====================

        private static bool CreatesCycle(Md9Model model, int index)
        {
            int steps = 0;
            int current = model.Parts[index].ParentId;
            while (current >= 0 && current < model.Parts.Count)
            {
                if (current == index || ++steps > model.Parts.Count) return true;
                current = model.Parts[current].ParentId;
            }
            return false;
        }

        /// <summary>Descompone la matriz local (como Matrix4.decompose de three.js) y la aplica al transform.</summary>
        private static void ApplyLocalMatrix(Transform t, Matrix4x4 m)
        {
            Vector3 position = m.GetColumn(3);

            Vector3 c0 = m.GetColumn(0);
            Vector3 c1 = m.GetColumn(1);
            Vector3 c2 = m.GetColumn(2);
            float sx = c0.magnitude;
            float sy = c1.magnitude;
            float sz = c2.magnitude;
            if (m.determinant < 0f) sx = -sx;

            Quaternion rotation = Quaternion.identity;
            if (Mathf.Abs(sx) > 1e-8f && sy > 1e-8f && sz > 1e-8f)
            {
                Vector3 up = c1 / sy;
                Vector3 forward = c2 / sz;
                if (forward.sqrMagnitude > 1e-10f && Vector3.Cross(forward, up).sqrMagnitude > 1e-10f)
                    rotation = Quaternion.LookRotation(forward, up);
            }
            else
            {
                sx = sx == 0f ? 1f : sx;
                sy = sy == 0f ? 1f : sy;
                sz = sz == 0f ? 1f : sz;
            }

            t.localPosition = position;
            t.localRotation = rotation;
            t.localScale = new Vector3(sx, sy, sz);
        }

        private static bool HasValidNormals(Vector3[] normals)
        {
            if (normals == null || normals.Length == 0) return false;
            for (int i = 0; i < normals.Length; i++)
                if (normals[i].sqrMagnitude > 1e-6f) return true;
            return false;
        }

        // ==================== Materiales y texturas ====================

        private Material CreateMaterial(AssetImportContext ctx, Md9Material source, int index, string directory,
            Dictionary<string, Texture2D> textureCache, Dictionary<string, bool> textureHasAlpha)
        {
            Texture2D texture = null;
            bool hasAlphaTexture = false;
            if (!string.IsNullOrEmpty(source.TextureName))
            {
                texture = LoadTexture(ctx, source.TextureName, directory, textureCache, textureHasAlpha);
                if (texture != null) hasAlphaTexture = textureHasAlpha[source.TextureName];
            }

            bool transparent = source.Diffuse.a < 0.999f;    // igual que el visor web
            bool alphaClip = hasAlphaTexture;

            Shader shaderAsset = FindShader(out bool isUrp, out bool isLit);
            var material = new Material(shaderAsset)
            {
                name = !string.IsNullOrEmpty(source.TextureName)
                    ? Path.GetFileNameWithoutExtension(source.TextureName)
                    : $"Material {index}"
            };

            Color baseColor = texture != null ? Color.white : new Color(
                source.Diffuse.r > 0f ? source.Diffuse.r : 0.72f,
                source.Diffuse.g > 0f ? source.Diffuse.g : 0.72f,
                source.Diffuse.b > 0f ? source.Diffuse.b : 0.72f,
                1f);
            baseColor.a = source.Diffuse.a > 0f ? source.Diffuse.a : 1f;

            if (isUrp)
            {
                material.SetColor("_BaseColor", baseColor);
                if (texture != null) material.SetTexture("_BaseMap", texture);
                material.SetFloat("_Cull", (float)CullMode.Off); // DoubleSide, como la web

                if (isLit)
                {
                    material.SetFloat("_Smoothness", 0f);
                    material.SetFloat("_SpecularHighlights", 0f);
                }

                if (alphaClip)
                {
                    material.SetFloat("_AlphaClip", 1f);
                    material.SetFloat("_Cutoff", 0.05f);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                }

                if (transparent)
                {
                    material.SetFloat("_Surface", 1f);
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = (int)RenderQueue.Transparent;
                }
            }
            else
            {
                // Built-in (Standard). Nota: Standard no permite desactivar el culling.
                material.SetColor("_Color", baseColor);
                if (texture != null) material.SetTexture("_MainTex", texture);
                material.SetFloat("_Glossiness", 0f);

                if (transparent)
                {
                    material.SetFloat("_Mode", 3f); // Transparent
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.renderQueue = (int)RenderQueue.Transparent;
                }
                else if (alphaClip)
                {
                    material.SetFloat("_Mode", 1f); // Cutout
                    material.SetFloat("_Cutoff", 0.05f);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                }
            }

            ctx.AddObjectToAsset($"mat_{index}_{material.name}", material);
            return material;
        }

        private Shader FindShader(out bool isUrp, out bool isLit)
        {
            isLit = shader == ShaderMode.LitAutomatico;
            string urpName = isLit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit";
            Shader s = Shader.Find(urpName);
            if (s != null)
            {
                isUrp = true;
                return s;
            }
            isUrp = false;
            return Shader.Find("Standard");
        }

        // Deduplica cuando dos nombres de textura distintos resuelven al mismo archivo.
        private readonly Dictionary<string, (Texture2D texture, bool hasAlpha)> _texturesByPath =
            new Dictionary<string, (Texture2D, bool)>(StringComparer.OrdinalIgnoreCase);

        private Texture2D LoadTexture(AssetImportContext ctx, string textureName, string directory,
            Dictionary<string, Texture2D> cache, Dictionary<string, bool> hasAlphaMap)
        {
            if (cache.TryGetValue(textureName, out var cached)) return cached;

            string path = FindTextureFile(directory, textureName);
            Texture2D texture = null;
            bool hasAlpha = false;

            if (path == null)
            {
                ctx.LogImportWarning($"Textura no encontrada junto al MD9: '{textureName}'.");
            }
            else if (_texturesByPath.TryGetValue(path, out var shared))
            {
                texture = shared.texture;
                hasAlpha = shared.hasAlpha;
            }
            else
            {
                RegisterDependency(ctx, path);
                try
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".dds")
                    {
                        DdsImage image = DdsDecoder.Decode(File.ReadAllBytes(path));
                        texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, mipChain: true);
                        texture.SetPixels32(image.Pixels);
                        hasAlpha = image.HasAlpha;
                    }
                    else // png / jpg / bmp / tga (los que Unity sabe decodificar por bytes)
                    {
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true);
                        if (!texture.LoadImage(File.ReadAllBytes(path)))
                            throw new InvalidDataException("formato de imagen no soportado");
                        hasAlpha = HasAnyAlpha(texture);
                    }

                    texture.name = Path.GetFileNameWithoutExtension(path);
                    texture.wrapMode = TextureWrapMode.Repeat;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.alphaIsTransparency = hasAlpha;
                    texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);

                    if (comprimirTexturas && texture.width % 4 == 0 && texture.height % 4 == 0)
                    {
                        TextureFormat format = ChooseCompressedFormat(ctx.selectedBuildTarget, hasAlpha);
                        EditorUtility.CompressTexture(texture, format, TextureCompressionQuality.Normal);
                    }

                    ctx.AddObjectToAsset($"tex_{texture.name}", texture);
                    _texturesByPath[path] = (texture, hasAlpha);
                }
                catch (Exception e)
                {
                    ctx.LogImportWarning($"No se pudo decodificar la textura '{Path.GetFileName(path)}': {e.Message}");
                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                    texture = null;
                }
            }

            cache[textureName] = texture;
            hasAlphaMap[textureName] = hasAlpha;
            return texture;
        }

        /// <summary>Replica findCompatibleTexture() de la web: nombre exacto y, si no, mismo nombre base con otra extensión.</summary>
        private static string FindTextureFile(string directory, string textureName)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;
            string wanted = Path.GetFileName(textureName);
            string wantedBase = Path.GetFileNameWithoutExtension(wanted);
            string[] preferredExtensions = { ".dds", ".png", ".jpg", ".jpeg", ".bmp", ".tga" };

            string byBase = null;
            int byBaseRank = int.MaxValue;
            foreach (string file in Directory.GetFiles(directory))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(fileName, wanted, StringComparison.OrdinalIgnoreCase)) return file;
                if (string.Equals(Path.GetFileNameWithoutExtension(fileName), wantedBase, StringComparison.OrdinalIgnoreCase))
                {
                    int rank = Array.FindIndex(preferredExtensions,
                        e => string.Equals(e, Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase));
                    if (rank < 0) continue;
                    if (rank < byBaseRank)
                    {
                        byBaseRank = rank;
                        byBase = file;
                    }
                }
            }
            return byBase;
        }

        private static bool HasAnyAlpha(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a < 255) return true;
            return false;
        }

        private static TextureFormat ChooseCompressedFormat(BuildTarget target, bool hasAlpha)
        {
            switch (target)
            {
                case BuildTarget.Android:
                case BuildTarget.iOS:
                    return TextureFormat.ASTC_6x6;
                default:
                    return hasAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1;
            }
        }

        private static void RegisterDependency(AssetImportContext ctx, string absoluteOrAssetPath)
        {
            string projectRelative = FileUtil.GetProjectRelativePath(absoluteOrAssetPath.Replace('\\', '/'));
            if (string.IsNullOrEmpty(projectRelative))
            {
                // Ya era relativa al proyecto (los ScriptedImporter reciben rutas "Assets/...").
                projectRelative = absoluteOrAssetPath.Replace('\\', '/');
            }
            if (projectRelative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                projectRelative.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                ctx.DependsOnSourceAsset(projectRelative);
            }
        }

        // ==================== Animación ====================

        private AnimationClip BuildClip(AniClip ani, Transform root, Dictionary<string, Transform> nodesByName)
        {
            var clip = new AnimationClip
            {
                name = ani.Name,
                legacy = animacionLegacy,
                frameRate = AniClip.Fps,
                wrapMode = animacionEnLoop ? WrapMode.Loop : WrapMode.Default
            };

            int missing = 0;
            foreach (var track in ani.Tracks)
            {
                if (!nodesByName.TryGetValue(track.BoneName, out Transform node))
                {
                    missing++;
                    continue;
                }
                string path = AnimationUtility.CalculateTransformPath(node, root);

                if (track.Positions.Count > 0)
                {
                    SetVectorCurves(clip, path, "localPosition",
                        track.Positions.Select(k => (k.Time / AniClip.Fps, k.Value)).ToList());
                }
                if (track.Rotations.Count > 0)
                {
                    var times = track.Rotations.Select(k => k.Time / AniClip.Fps).ToArray();
                    clip.SetCurve(path, typeof(Transform), "localRotation.x", BuildLinearCurve(times, track.Rotations.Select(k => k.Value.x).ToArray()));
                    clip.SetCurve(path, typeof(Transform), "localRotation.y", BuildLinearCurve(times, track.Rotations.Select(k => k.Value.y).ToArray()));
                    clip.SetCurve(path, typeof(Transform), "localRotation.z", BuildLinearCurve(times, track.Rotations.Select(k => k.Value.z).ToArray()));
                    clip.SetCurve(path, typeof(Transform), "localRotation.w", BuildLinearCurve(times, track.Rotations.Select(k => k.Value.w).ToArray()));
                }
                if (track.Scales.Count > 0)
                {
                    SetVectorCurves(clip, path, "localScale",
                        track.Scales.Select(k => (k.Time / AniClip.Fps, k.Value)).ToList());
                }
            }

            if (missing > 0)
                Debug.LogWarning($"ANI '{ani.Name}': {missing} pista(s) sin hueso/parte correspondiente en el modelo.");

            clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static void SetVectorCurves(AnimationClip clip, string path, string property, List<(float time, Vector3 value)> keys)
        {
            var times = keys.Select(k => k.time).ToArray();
            clip.SetCurve(path, typeof(Transform), property + ".x", BuildLinearCurve(times, keys.Select(k => k.value.x).ToArray()));
            clip.SetCurve(path, typeof(Transform), property + ".y", BuildLinearCurve(times, keys.Select(k => k.value.y).ToArray()));
            clip.SetCurve(path, typeof(Transform), property + ".z", BuildLinearCurve(times, keys.Select(k => k.value.z).ToArray()));
        }

        /// <summary>Crea una curva con tangentes lineales (el visor web interpola linealmente).</summary>
        private static AnimationCurve BuildLinearCurve(float[] times, float[] values)
        {
            var keyframes = new Keyframe[times.Length];
            for (int i = 0; i < times.Length; i++)
            {
                float inTangent = 0f, outTangent = 0f;
                if (i > 0)
                {
                    float dt = times[i] - times[i - 1];
                    inTangent = dt > 1e-6f ? (values[i] - values[i - 1]) / dt : 0f;
                }
                if (i < times.Length - 1)
                {
                    float dt = times[i + 1] - times[i];
                    outTangent = dt > 1e-6f ? (values[i + 1] - values[i]) / dt : 0f;
                }
                keyframes[i] = new Keyframe(times[i], values[i], inTangent, outTangent);
            }
            return new AnimationCurve(keyframes);
        }
    }
}
