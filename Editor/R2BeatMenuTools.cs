// Utilidades de menú para R2Beat.
// "Assets > R2Beat > Convertir DDS seleccionados a PNG":
//   Decodifica los .dds seleccionados en el Project y crea un .png al lado.
//   Útil para el build móvil: Unity comprime los PNG por plataforma
//   (ASTC/ETC2 en Android/iOS) con toda su tubería normal de texturas.
//   El importador MD9 usará el PNG automáticamente si borras el .dds
//   (busca por nombre base, igual que la herramienta web).

using System.IO;
using UnityEditor;
using UnityEngine;

namespace R2Beat.ImporterEditor
{
    public static class R2BeatMenuTools
    {
        [MenuItem("Assets/R2Beat/Convertir DDS seleccionados a PNG", false, 2000)]
        public static void ConvertSelectedDdsToPng()
        {
            int converted = 0;
            foreach (Object obj in Selection.objects)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(assetPath) || !assetPath.ToLowerInvariant().EndsWith(".dds")) continue;

                try
                {
                    DdsImage image = DdsDecoder.Decode(File.ReadAllBytes(assetPath));
                    var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, mipChain: false);
                    texture.SetPixels32(image.Pixels);
                    texture.Apply();
                    byte[] png = texture.EncodeToPNG();
                    Object.DestroyImmediate(texture);

                    string pngPath = Path.ChangeExtension(assetPath, ".png");
                    File.WriteAllBytes(pngPath, png);
                    converted++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"No se pudo convertir '{assetPath}': {e.Message}");
                }
            }

            if (converted > 0) AssetDatabase.Refresh();
            Debug.Log($"R2Beat: {converted} textura(s) DDS convertidas a PNG.");
        }

        [MenuItem("Assets/R2Beat/Convertir DDS seleccionados a PNG", true)]
        public static bool ValidateConvertSelectedDdsToPng()
        {
            foreach (Object obj in Selection.objects)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(assetPath) && assetPath.ToLowerInvariant().EndsWith(".dds")) return true;
            }
            return false;
        }
    }
}
