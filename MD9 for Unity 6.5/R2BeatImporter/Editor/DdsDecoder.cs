// Decodificador DDS (DXT1 / DXT3 / DXT5 / sin comprimir 32 bpp) → Color32[].
// Port de decodeDdsToCanvas() de la herramienta web (main0ab9.js).
// Las filas se escriben invertidas verticalmente porque Unity coloca el
// origen de la textura abajo-izquierda (el DDS se almacena de arriba a abajo).

using System;
using System.IO;
using UnityEngine;

namespace R2Beat.ImporterEditor
{
    public class DdsImage
    {
        public int Width;
        public int Height;
        public Color32[] Pixels; // fila 0 = abajo (convención de Unity)
        public bool HasAlpha;
    }

    public static class DdsDecoder
    {
        public static DdsImage Decode(byte[] data)
        {
            if (data.Length < 128 || ReadUInt32(data, 0) != 0x20534444u || ReadUInt32(data, 4) != 124)
                throw new InvalidDataException("Archivo DDS inválido.");

            int height = (int)ReadUInt32(data, 12);
            int width = (int)ReadUInt32(data, 16);
            uint pixelFlags = ReadUInt32(data, 80);
            string fourCc = "" + (char)data[84] + (char)data[85] + (char)data[86] + (char)data[87];

            var image = new DdsImage
            {
                Width = width,
                Height = height,
                Pixels = new Color32[width * height]
            };

            if ((pixelFlags & 0x4) != 0) // DDPF_FOURCC
            {
                int blockSize;
                Action<byte[], int, DdsImage, int, int> decoder;
                switch (fourCc)
                {
                    case "DXT1": blockSize = 8; decoder = DecodeDxt1Block; break;
                    case "DXT3": blockSize = 16; decoder = DecodeDxt3Block; break;
                    case "DXT5": blockSize = 16; decoder = DecodeDxt5Block; break;
                    default: throw new InvalidDataException($"Formato DDS no soportado: {fourCc}");
                }
                int offset = 128;
                int blocksY = (height + 3) / 4;
                int blocksX = (width + 3) / 4;
                for (int blockY = 0; blockY < blocksY; blockY++)
                {
                    for (int blockX = 0; blockX < blocksX; blockX++)
                    {
                        decoder(data, offset, image, blockX * 4, blockY * 4);
                        offset += blockSize;
                    }
                }
            }
            else
            {
                DecodeUncompressed(data, image);
            }

            return image;
        }

        // ---- DXT ----

        private static void DecodeDxt1Block(byte[] data, int offset, DdsImage image, int startX, int startY)
        {
            Color32[] colors = DecodeDxtColors(
                (ushort)(data[offset] | (data[offset + 1] << 8)),
                (ushort)(data[offset + 2] | (data[offset + 3] << 8)),
                allowTransparent: true);
            uint codes = ReadUInt32(data, offset + 4);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    var color = colors[(codes >> (2 * (y * 4 + x))) & 3];
                    WritePixel(image, startX + x, startY + y, color.r, color.g, color.b, color.a);
                }
        }

        private static void DecodeDxt3Block(byte[] data, int offset, DdsImage image, int startX, int startY)
        {
            Color32[] colors = DecodeDxtColors(
                (ushort)(data[offset + 8] | (data[offset + 9] << 8)),
                (ushort)(data[offset + 10] | (data[offset + 11] << 8)),
                allowTransparent: false);
            uint codes = ReadUInt32(data, offset + 12);
            for (int y = 0; y < 4; y++)
            {
                int alphaRow = data[offset + y * 2] | (data[offset + y * 2 + 1] << 8);
                for (int x = 0; x < 4; x++)
                {
                    var color = colors[(codes >> (2 * (y * 4 + x))) & 3];
                    byte alpha = (byte)(((alphaRow >> (x * 4)) & 0xF) * 17);
                    WritePixel(image, startX + x, startY + y, color.r, color.g, color.b, alpha);
                }
            }
        }

        private static void DecodeDxt5Block(byte[] data, int offset, DdsImage image, int startX, int startY)
        {
            byte[] alphas = DecodeDxt5Alphas(data[offset], data[offset + 1]);
            ulong alphaBits = 0;
            for (int i = 0; i < 6; i++) alphaBits |= (ulong)data[offset + 2 + i] << (i * 8);
            Color32[] colors = DecodeDxtColors(
                (ushort)(data[offset + 8] | (data[offset + 9] << 8)),
                (ushort)(data[offset + 10] | (data[offset + 11] << 8)),
                allowTransparent: false);
            uint codes = ReadUInt32(data, offset + 12);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int index = y * 4 + x;
                    var color = colors[(codes >> (2 * index)) & 3];
                    byte alpha = alphas[(alphaBits >> (index * 3)) & 0x7];
                    WritePixel(image, startX + x, startY + y, color.r, color.g, color.b, alpha);
                }
        }

        private static Color32[] DecodeDxtColors(ushort color0, ushort color1, bool allowTransparent)
        {
            Color32 c0 = DecodeRgb565(color0);
            Color32 c1 = DecodeRgb565(color1);
            var colors = new Color32[4];
            colors[0] = c0;
            colors[1] = c1;
            if (allowTransparent && color0 <= color1)
            {
                colors[2] = new Color32(
                    (byte)Mathf.RoundToInt((c0.r + c1.r) / 2f),
                    (byte)Mathf.RoundToInt((c0.g + c1.g) / 2f),
                    (byte)Mathf.RoundToInt((c0.b + c1.b) / 2f), 255);
                colors[3] = new Color32(0, 0, 0, 0);
            }
            else
            {
                colors[2] = new Color32(
                    (byte)Mathf.RoundToInt((2f * c0.r + c1.r) / 3f),
                    (byte)Mathf.RoundToInt((2f * c0.g + c1.g) / 3f),
                    (byte)Mathf.RoundToInt((2f * c0.b + c1.b) / 3f), 255);
                colors[3] = new Color32(
                    (byte)Mathf.RoundToInt((c0.r + 2f * c1.r) / 3f),
                    (byte)Mathf.RoundToInt((c0.g + 2f * c1.g) / 3f),
                    (byte)Mathf.RoundToInt((c0.b + 2f * c1.b) / 3f), 255);
            }
            return colors;
        }

        private static Color32 DecodeRgb565(ushort value)
        {
            return new Color32(
                (byte)Mathf.RoundToInt(((value >> 11) & 0x1F) * 255f / 31f),
                (byte)Mathf.RoundToInt(((value >> 5) & 0x3F) * 255f / 63f),
                (byte)Mathf.RoundToInt((value & 0x1F) * 255f / 31f),
                255);
        }

        private static byte[] DecodeDxt5Alphas(byte alpha0, byte alpha1)
        {
            var alphas = new byte[8];
            alphas[0] = alpha0;
            alphas[1] = alpha1;
            if (alpha0 > alpha1)
            {
                for (int i = 1; i <= 6; i++)
                    alphas[i + 1] = (byte)Mathf.RoundToInt(((7 - i) * alpha0 + i * alpha1) / 7f);
            }
            else
            {
                for (int i = 1; i <= 4; i++)
                    alphas[i + 1] = (byte)Mathf.RoundToInt(((5 - i) * alpha0 + i * alpha1) / 5f);
                alphas[6] = 0;
                alphas[7] = 255;
            }
            return alphas;
        }

        // ---- Sin comprimir ----

        private static void DecodeUncompressed(byte[] data, DdsImage image)
        {
            uint rgbBitCount = ReadUInt32(data, 88);
            uint rMask = ReadUInt32(data, 92);
            uint gMask = ReadUInt32(data, 96);
            uint bMask = ReadUInt32(data, 100);
            uint aMask = ReadUInt32(data, 104);
            if (rgbBitCount != 32)
                throw new InvalidDataException($"Profundidad DDS no soportada: {rgbBitCount} bpp (solo 32 bpp sin comprimir).");

            int offset = 128;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    uint value = ReadUInt32(data, offset);
                    byte a = aMask != 0 ? ExtractMaskedByte(value, aMask) : (byte)255;
                    WritePixel(image, x, y,
                        ExtractMaskedByte(value, rMask),
                        ExtractMaskedByte(value, gMask),
                        ExtractMaskedByte(value, bMask),
                        a);
                    offset += 4;
                }
            }
        }

        private static byte ExtractMaskedByte(uint value, uint mask)
        {
            if (mask == 0) return 0;
            int shift = 0;
            uint m = mask;
            while ((m & 1) == 0) { m >>= 1; shift++; }
            uint max = mask >> shift;
            return (byte)Mathf.RoundToInt(((value & mask) >> shift) * 255f / max);
        }

        // ---- Utilidades ----

        private static void WritePixel(DdsImage image, int x, int y, byte r, byte g, byte b, byte a)
        {
            if (x >= image.Width || y >= image.Height) return;
            // Invertir Y: el DDS va de arriba a abajo, Unity de abajo a arriba.
            int index = (image.Height - 1 - y) * image.Width + x;
            image.Pixels[index] = new Color32(r, g, b, a);
            if (a < 255) image.HasAlpha = true;
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }
    }
}
