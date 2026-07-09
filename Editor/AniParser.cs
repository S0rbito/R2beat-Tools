// Parser binario del formato ANI de R2Beat.
// Port directo de parseAni() de la herramienta web (main0ab9.js).
// Los cuaterniones y posiciones se usan tal cual (left-handed = Unity);
// la web les invertía componentes solo para pasar a three.js (right-handed).
//
// Layout (little-endian):
//   int32  boneCount
//   float  duration                 (en frames, a 90 fps)
//   Track x boneCount:
//     uint8  nameLength
//     char   name[nameLength]
//     int32  positionCount, rotationCount, scaleCount
//     PosKey x positionCount:   float time, float3 value
//     RotKey x rotationCount:   float time, float4 value (x,y,z,w)
//     ScaleKey x scaleCount:    float time, float3 value

using System.IO;
using System.Text;
using UnityEngine;

namespace R2Beat.ImporterEditor
{
    public static class AniParser
    {
        public static AniClip Parse(byte[] data, string name)
        {
            using (var stream = new MemoryStream(data))
            using (var reader = new BinaryReader(stream))
            {
                var clip = new AniClip { Name = name };
                int boneCount = reader.ReadInt32();
                if (boneCount < 0 || boneCount > 100000)
                    throw new InvalidDataException($"ANI '{name}': boneCount inválido ({boneCount}).");
                clip.Duration = reader.ReadSingle();

                for (int i = 0; i < boneCount; i++)
                {
                    var track = new AniTrack();
                    int nameLength = reader.ReadByte();
                    var sb = new StringBuilder(nameLength);
                    for (int j = 0; j < nameLength; j++) sb.Append((char)reader.ReadByte());
                    track.BoneName = sb.ToString();

                    int positionCount = reader.ReadInt32();
                    int rotationCount = reader.ReadInt32();
                    int scaleCount = reader.ReadInt32();

                    for (int j = 0; j < positionCount; j++)
                    {
                        track.Positions.Add(new AniVectorKey
                        {
                            Time = reader.ReadSingle(),
                            Value = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
                        });
                    }
                    for (int j = 0; j < rotationCount; j++)
                    {
                        float time = reader.ReadSingle();
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        float w = reader.ReadSingle();
                        var q = new Quaternion(x, y, z, w);
                        q.Normalize();
                        track.Rotations.Add(new AniQuaternionKey { Time = time, Value = q });
                    }
                    for (int j = 0; j < scaleCount; j++)
                    {
                        track.Scales.Add(new AniVectorKey
                        {
                            Time = reader.ReadSingle(),
                            Value = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
                        });
                    }
                    clip.Tracks.Add(track);
                }

                if (stream.Position != stream.Length)
                    throw new InvalidDataException($"ANI '{name}': longitud de datos no coincide (leídos {stream.Position} de {stream.Length}).");

                return clip;
            }
        }
    }
}
