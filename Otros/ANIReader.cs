using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RRBeat.ImportadorMD9
{
    // ── Estructuras de datos ────────────────────────────────────────────────────

    public struct ANIVectorKey
    {
        public float    time;
        public Vector3  value;
    }

    public struct ANIQuaternionKey
    {
        public float      time;
        public Quaternion value;
    }

    public sealed class ANITrack
    {
        public string                  boneName;
        public List<ANIVectorKey>     positions  = new List<ANIVectorKey>();
        public List<ANIQuaternionKey> rotations  = new List<ANIQuaternionKey>();
        public List<ANIVectorKey>     scales     = new List<ANIVectorKey>();
    }

    public sealed class ANIData
    {
        public string                        name;
        public float                         duration;
        public Dictionary<string, ANITrack>  tracks = new Dictionary<string, ANITrack>();
    }

    // ── Reader ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parsea el formato binario .ani de R2Beat (little-endian).
    ///
    /// Estructura del archivo:
    ///   [Int32]   boneCount
    ///   [Float32] duration
    ///   Por cada hueso:
    ///     [UInt8]   nameLength
    ///     [chars]   boneName  (nameLength bytes ASCII)
    ///     [Int32]   positionCount
    ///     [Int32]   rotationCount
    ///     [Int32]   scaleCount
    ///     Por cada position keyframe:  time, x, y, -z  (Z se invierte)
    ///     Por cada rotation keyframe:  time, -x, -y, z, w
    ///     Por cada scale keyframe:     time, x, y, z
    /// </summary>
    public static class ANIReader
    {
        public static ANIData Leer(byte[] bytes, string nombre = "animacion.ani")
        {
            if (bytes == null || bytes.Length < 8)
                throw new ArgumentException("El archivo ANI esta vacio o incompleto.", nameof(bytes));

            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                var ani = new ANIData { name = nombre };

                int   boneCount = reader.ReadInt32();
                float duration  = reader.ReadSingle();
                ani.duration    = duration;

                for (int i = 0; i < boneCount; i++)
                {
                    // Nombre del hueso: 1 byte de longitud + N bytes ASCII
                    int    nameLength = reader.ReadByte();
                    byte[] nameBytes  = reader.ReadBytes(nameLength);
                    string boneName   = System.Text.Encoding.ASCII.GetString(nameBytes);

                    int posCount = reader.ReadInt32();
                    int rotCount = reader.ReadInt32();
                    int scaCount = reader.ReadInt32();

                    var track = new ANITrack { boneName = boneName };

                    // Posiciones — Z invertida igual que el visor web
                    for (int j = 0; j < posCount; j++)
                    {
                        float t = reader.ReadSingle();
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        track.positions.Add(new ANIVectorKey
                        {
                            time  = t,
                            value = new Vector3(x, y, -z)
                        });
                    }

                    // Rotaciones — X e Y negados, igual que convertQuaternion() del visor
                    for (int j = 0; j < rotCount; j++)
                    {
                        float t = reader.ReadSingle();
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        float w = reader.ReadSingle();
                        track.rotations.Add(new ANIQuaternionKey
                        {
                            time  = t,
                            value = new Quaternion(-x, -y, z, w)
                        });
                    }

                    // Escalas — sin transformación
                    for (int j = 0; j < scaCount; j++)
                    {
                        float t = reader.ReadSingle();
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        track.scales.Add(new ANIVectorKey
                        {
                            time  = t,
                            value = new Vector3(x, y, z)
                        });
                    }

                    ani.tracks[boneName] = track;
                }

                return ani;
            }
        }

        // ── Interpolación — misma lógica que sampleVectorKey / sampleQuaternionKey del JS ──

        /// <summary>
        /// Interpola posición o escala en el tiempo dado.
        /// Devuelve false si no hay keyframes o el tiempo está antes del primero.
        /// </summary>
        public static bool SampleVector(List<ANIVectorKey> keys, float time, out Vector3 result)
        {
            result = Vector3.zero;
            if (keys == null || keys.Count == 0) return false;
            if (time < keys[0].time)             return false;
            if (keys.Count == 1 || time <= keys[0].time)
            {
                result = keys[0].value;
                return true;
            }

            for (int i = 0; i < keys.Count - 1; i++)
            {
                ANIVectorKey a = keys[i];
                ANIVectorKey b = keys[i + 1];
                if (time <= b.time)
                {
                    float span = Mathf.Max(b.time - a.time, 0.0001f);
                    float t    = (time - a.time) / span;
                    result = Vector3.Lerp(a.value, b.value, t);
                    return true;
                }
            }

            result = keys[keys.Count - 1].value;
            return true;
        }

        /// <summary>
        /// Interpola rotación (slerp) en el tiempo dado.
        /// </summary>
        public static bool SampleQuaternion(List<ANIQuaternionKey> keys, float time, out Quaternion result)
        {
            result = Quaternion.identity;
            if (keys == null || keys.Count == 0) return false;
            if (time < keys[0].time)             return false;
            if (keys.Count == 1 || time <= keys[0].time)
            {
                result = keys[0].value;
                return true;
            }

            for (int i = 0; i < keys.Count - 1; i++)
            {
                ANIQuaternionKey a = keys[i];
                ANIQuaternionKey b = keys[i + 1];
                if (time <= b.time)
                {
                    float span = Mathf.Max(b.time - a.time, 0.0001f);
                    float t    = (time - a.time) / span;
                    result = Quaternion.Slerp(a.value, b.value, t);
                    return true;
                }
            }

            result = keys[keys.Count - 1].value;
            return true;
        }
    }
}
