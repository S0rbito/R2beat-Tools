using System.Collections.Generic;
using UnityEngine;

namespace RRBeat.ImportadorMD9
{
    // ── Tipos de soporte requeridos por MD9PreviewLoader ───────────────────────

    /// <summary>
    /// Pose base de un hueso (posición, rotación y escala en el momento de carga).
    /// MD9PreviewLoader la guarda y MD9AnimationController la usa para resetPose().
    /// </summary>
    public readonly struct MD9TransformPose
    {
        public readonly Vector3    Posicion;
        public readonly Quaternion Rotacion;
        public readonly Vector3    Escala;

        public MD9TransformPose(Vector3 posicion, Quaternion rotacion, Vector3 escala)
        {
            Posicion = posicion;
            Rotacion = rotacion;
            Escala   = escala;
        }
    }

    /// <summary>
    /// ScriptableObject que envuelve los bytes crudos de un archivo .md9.
    /// Permite asignarlo en el Inspector sin usar TextAsset (que requiere extensión .bytes).
    /// </summary>
    public sealed class MD9RawBytesAsset : ScriptableObject
    {
        [HideInInspector]
        public byte[] Bytes = System.Array.Empty<byte>();
    }


    public sealed class MD9ModelData
    {
        public string Nombre;
        public bool FormatoNuevo;
        public readonly List<MD9MaterialData> Materiales = new List<MD9MaterialData>();
        public readonly List<MD9PartData> Partes = new List<MD9PartData>();
        public int TotalVertices;
        public int TotalCaras;
        public Bounds Bounds;
    }

    public sealed class MD9MaterialData
    {
        public Color Difuso;
        public Color Ambiente;
        public Color Especular;
        public Color Emisivo;
        public float Brillo;
        public string NombreTextura;
        public byte[] Extra;
    }

    public sealed class MD9PartData
    {
        public string Nombre;
        public Matrix4x4 MatrizLocal;
        public Vector3 PosicionLocal;
        public Quaternion RotacionLocal;
        public Vector3 EscalaLocal;
        public int CantidadVertices;
        public int CantidadCaras;
        public int MaterialId;
        public int PadreId;
        public Vector3[] VerticesLocales;
        public Vector3[] Normales;
        public Vector2[] UVs;
        public int[] Indices;
        public Bounds Bounds;
    }
}