// Parser binario del formato MD9 de R2Beat.
// Port directo de parseMd9() de la herramienta web (main0ab9.js), sin los
// flips de Z que la web necesita para three.js (Unity comparte la convención
// left-handed de Direct3D).
//
// Layout del archivo (little-endian):
//   int32  primerValor            (-1 => formato nuevo; si no, es materialCount)
//   [int32 materialCount]         (solo formato nuevo)
//   Material x materialCount:
//     float4 diffuse, float4 ambient, float4 specular, float4 emissive
//     float  power
//     char   textureName[32]      (termina en 0 o 0xCD)
//     byte   extra[16]            (solo formato nuevo)
//   int32  partCount
//   PartHeader x partCount:
//     char    name[32]
//     float   matrix[16]          (row-major D3D, local al padre)
//     int32   vertexCount, faceCount, materialId, parentId
//     float   boundingBox[6]
//   int32  totalVertices, int32 totalFaces
//   Vertex x totalVertices:  float3 pos, float3 normal, float2 uv   (locales a su parte)
//   uint16 indices[totalFaces * 3]                                  (locales a su parte)

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace R2Beat.ImporterEditor
{
    public static class Md9Parser
    {
        public static Md9Model Parse(byte[] data, string name)
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var model = new Md9Model { Name = name };

                int first = reader.ReadInt32();
                model.NewFormat = first == -1;
                int materialCount = model.NewFormat ? reader.ReadInt32() : first;
                if (materialCount < 0 || materialCount > 100000)
                    throw new InvalidDataException($"MD9 '{name}': materialCount inválido ({materialCount}).");

                for (int i = 0; i < materialCount; i++)
                {
                    var material = new Md9Material
                    {
                        Diffuse = ReadColor(reader),
                        Ambient = ReadColor(reader),
                        Specular = ReadColor(reader),
                        Emissive = ReadColor(reader),
                        Power = reader.ReadSingle(),
                        TextureName = ReadFixedName(reader, 32, 0xCD)
                    };
                    if (model.NewFormat) material.Extra = reader.ReadBytes(16);
                    model.Materials.Add(material);
                }

                int partCount = reader.ReadInt32();
                if (partCount < 0 || partCount > 100000)
                    throw new InvalidDataException($"MD9 '{name}': partCount inválido ({partCount}).");

                for (int i = 0; i < partCount; i++)
                {
                    var part = new Md9Part();
                    part.Name = ReadFixedName(reader, 32, 0xCD);
                    if (string.IsNullOrEmpty(part.Name)) part.Name = $"Submesh {i}";
                    for (int j = 0; j < 16; j++) part.MatrixRaw[j] = reader.ReadSingle();
                    part.VertexCount = reader.ReadInt32();
                    part.FaceCount = reader.ReadInt32();
                    part.MaterialId = reader.ReadInt32();
                    part.ParentId = reader.ReadInt32();
                    for (int j = 0; j < 6; j++) part.BoundingBox[j] = reader.ReadSingle();
                    model.Parts.Add(part);
                }

                int declaredTotalVertices = reader.ReadInt32();
                int declaredTotalFaces = reader.ReadInt32();
                int headerTotalVertices = 0;
                int headerTotalFaces = 0;
                foreach (var part in model.Parts)
                {
                    headerTotalVertices += part.VertexCount;
                    headerTotalFaces += part.FaceCount;
                }
                int totalVertices = headerTotalVertices != 0 ? headerTotalVertices : declaredTotalVertices;
                int totalFaces = headerTotalFaces != 0 ? headerTotalFaces : declaredTotalFaces;
                if (declaredTotalVertices != totalVertices || declaredTotalFaces != totalFaces)
                {
                    Debug.LogWarning(
                        $"MD9 '{name}': conteos declarados ({declaredTotalVertices}/{declaredTotalFaces}) " +
                        $"difieren de los de cabecera ({headerTotalVertices}/{headerTotalFaces}). Se usan los de cabecera.");
                }
                model.TotalVertices = totalVertices;
                model.TotalFaces = totalFaces;

                // Bloque global de vértices (cada parte ocupa un rango consecutivo).
                var positions = new Vector3[totalVertices];
                var normals = new Vector3[totalVertices];
                var uvs = new Vector2[totalVertices];
                for (int i = 0; i < totalVertices; i++)
                {
                    positions[i] = ReadVector3(reader);         // tal cual (LH = Unity)
                    normals[i] = ReadVector3(reader);
                    float u = reader.ReadSingle();
                    float v = reader.ReadSingle();
                    uvs[i] = new Vector2(u, 1f - v);            // origen D3D arriba → Unity abajo
                }

                int totalIndices = totalFaces * 3;
                var allIndices = new ushort[totalIndices];
                for (int i = 0; i < totalIndices; i++) allIndices[i] = reader.ReadUInt16();

                // Repartir vértices/índices por parte. Los índices del archivo ya son
                // locales a cada parte (0..vertexCount-1), igual que en la web.
                int vertexCursor = 0;
                int indexCursor = 0;
                foreach (var part in model.Parts)
                {
                    part.Vertices = new Vector3[part.VertexCount];
                    part.Normals = new Vector3[part.VertexCount];
                    part.Uvs = new Vector2[part.VertexCount];
                    Array.Copy(positions, vertexCursor, part.Vertices, 0, part.VertexCount);
                    Array.Copy(normals, vertexCursor, part.Normals, 0, part.VertexCount);
                    Array.Copy(uvs, vertexCursor, part.Uvs, 0, part.VertexCount);

                    int indexCount = part.FaceCount * 3;
                    part.Indices = new int[indexCount];
                    for (int i = 0; i < indexCount; i++) part.Indices[i] = allIndices[indexCursor + i];

                    vertexCursor += part.VertexCount;
                    indexCursor += indexCount;
                }

                if (vertexCursor != headerTotalVertices || indexCursor != totalIndices)
                    throw new InvalidDataException($"MD9 '{name}': conteo de vértices o índices inconsistente.");

                return model;
            }
        }

        private static Color ReadColor(BinaryReader reader)
        {
            return new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        /// <summary>Lee un nombre de longitud fija; corta en 0 o en el byte centinela (0xCD relleno de MSVC).</summary>
        internal static string ReadFixedName(BinaryReader reader, int length, byte sentinel)
        {
            byte[] bytes = reader.ReadBytes(length);
            var sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                if (b == 0 || b == sentinel) break;
                if (b >= 32 && b < 127) sb.Append((char)b);
            }
            return sb.ToString().Trim();
        }
    }
}
