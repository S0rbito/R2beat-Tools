// R2Beat MD9/ANI data model
// Estructuras en memoria para los formatos MD9 (modelo) y ANI (animación) de R2Beat.
// Convención de coordenadas: los archivos usan Direct3D left-handed Y-up,
// que es la MISMA convención de Unity, por lo que los datos se usan tal cual
// (sin invertir Z). Solo se invierte la V de los UV (v' = 1 - v) porque el
// origen de textura de D3D es arriba-izquierda y el de Unity abajo-izquierda.

using System.Collections.Generic;
using UnityEngine;

namespace R2Beat.ImporterEditor
{
    public class Md9Material
    {
        public Color Diffuse = Color.white;
        public Color Ambient = Color.white;
        public Color Specular = Color.black;
        public Color Emissive = Color.black;
        public float Power;
        public string TextureName = "";
        public byte[] Extra; // 16 bytes adicionales del formato "nuevo" (cabecera -1)
    }

    public class Md9Part
    {
        public string Name = "";
        /// <summary>Matriz local 4x4 tal como viene del archivo (16 floats).</summary>
        public float[] MatrixRaw = new float[16];
        public int VertexCount;
        public int FaceCount;
        public int MaterialId;
        public int ParentId = -1;
        public float[] BoundingBox = new float[6];

        // Geometría local a la parte (relativa a su "hueso").
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public int[] Indices;

        /// <summary>Matriz local convertida a Matrix4x4 de Unity (column-major).</summary>
        public Matrix4x4 LocalMatrix
        {
            get
            {
                // El array MD9 es row-major estilo D3D (convención fila-vector).
                // Interpretarlo como column-major equivale a transponerlo, que es
                // exactamente la conversión a la convención columna-vector de Unity.
                var m = new Matrix4x4();
                for (int c = 0; c < 4; c++)
                {
                    m.SetColumn(c, new Vector4(
                        MatrixRaw[c * 4 + 0],
                        MatrixRaw[c * 4 + 1],
                        MatrixRaw[c * 4 + 2],
                        MatrixRaw[c * 4 + 3]));
                }
                return m;
            }
        }
    }

    public class Md9Model
    {
        public string Name = "";
        public bool NewFormat;
        public List<Md9Material> Materials = new List<Md9Material>();
        public List<Md9Part> Parts = new List<Md9Part>();
        public int TotalVertices;
        public int TotalFaces;
    }

    public struct AniVectorKey
    {
        public float Time; // en frames (90 fps)
        public Vector3 Value;
    }

    public struct AniQuaternionKey
    {
        public float Time; // en frames (90 fps)
        public Quaternion Value;
    }

    public class AniTrack
    {
        public string BoneName = "";
        public List<AniVectorKey> Positions = new List<AniVectorKey>();
        public List<AniQuaternionKey> Rotations = new List<AniQuaternionKey>();
        public List<AniVectorKey> Scales = new List<AniVectorKey>();
    }

    public class AniClip
    {
        public const float Fps = 90f; // ANIMATION_FPS de la herramienta web

        public string Name = "";
        /// <summary>Duración en frames (unidad del archivo). Segundos = Duration / Fps.</summary>
        public float Duration;
        public List<AniTrack> Tracks = new List<AniTrack>();
    }
}
