using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RRBeat.ImportadorMD9;

namespace RRBeat.ImportadorMD9
{
    /// <summary>
    /// Controlador de animación para modelos MD9.
    /// - Recibe el modelo de MD9PreviewLoader via OnModeloCargado()
    /// - Carga archivos .ani con CargarANI() o CargarANIPorURL()
    /// - Reproduce, pausa, detiene y hace loop de animaciones
    ///
    /// USO:
    ///     var anim = GetComponent<MD9AnimationController>();
    ///     StartCoroutine(anim.CargarANIPorURL(url));
    ///     anim.loop = true;
    /// </summary>
    public class MD9AnimationController : MonoBehaviour
    {
        [Header("Reproducción")]
        public bool reproducirAlCargar = true;
        public bool loop               = true;
        [Range(0.01f, 4f)]
        public float velocidad         = 1f;

        [Header("Debug")]
        [SerializeField] private string animacionActual = "";
        [SerializeField] private float  tiempoActual    = 0f;
        [SerializeField] private float  duracion         = 0f;
        [SerializeField] private bool   reproduciendo    = false;

        // ── Datos internos ──────────────────────────────────────────────────────
        private MD9PreviewLoader loader;

        // Mapa nombre → Transform + pose base (viene de MD9PreviewLoader.IterarNodos)
        private Dictionary<string, Transform>       boneMap      = new Dictionary<string, Transform>();
        private Dictionary<string, MD9TransformPose> poseBase    = new Dictionary<string, MD9TransformPose>();

        private ANIData animacionCargada = null;

        // ── API pública ─────────────────────────────────────────────────────────

        /// <summary>Llamado por MD9PreviewLoader cuando el modelo ya está en escena.</summary>
        public void OnModeloCargado(MD9PreviewLoader previewLoader)
        {
            loader = previewLoader;
            boneMap.Clear();
            poseBase.Clear();

            // Usamos IterarNodos del loader para obtener transform + pose base
            previewLoader.IterarNodos((nombre, transform, pose) =>
            {
                boneMap[nombre]  = transform;
                poseBase[nombre] = pose;
            });

            Debug.Log($"MD9AnimationController: {boneMap.Count} huesos mapeados.");

            if (animacionCargada != null && reproducirAlCargar)
                Reproducir();
        }

        /// <summary>Carga un ANI desde bytes.</summary>
        public void CargarANI(byte[] bytes, string nombre = "animacion.ani")
        {
            try
            {
                animacionCargada = ANIReader.Leer(bytes, nombre);
                animacionActual  = nombre;
                duracion         = animacionCargada.duration;
                tiempoActual     = 0f;
                Debug.Log($"MD9AnimationController: '{nombre}' — {animacionCargada.tracks.Count} tracks, {duracion:F2}s");

                if (reproducirAlCargar && boneMap.Count > 0)
                    Reproducir();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"MD9AnimationController: Error cargando '{nombre}': {e.Message}");
            }
        }

        /// <summary>
        /// Carga un ANI desde URL (compatible con Android/iOS via UnityWebRequest).
        /// Ejemplo: StartCoroutine(anim.CargarANIPorURL(Application.streamingAssetsPath + "/front.ani"));
        /// </summary>
        public IEnumerator CargarANIPorURL(string url)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"MD9AnimationController: No se pudo cargar '{url}': {req.error}");
                    yield break;
                }

                CargarANI(req.downloadHandler.data, System.IO.Path.GetFileName(url));
            }
        }

        public void Reproducir() => reproduciendo = true;
        public void Pausar()     => reproduciendo = false;
        public void Detener()    { reproduciendo = false; tiempoActual = 0f; ResetPose(); }

        public void IrATiempo(float tiempo)
        {
            tiempoActual = Mathf.Clamp(tiempo, 0f, duracion);
            AplicarAnimacion(tiempoActual);
        }

        // ── Update ──────────────────────────────────────────────────────────────

        void Update()
        {
            if (!reproduciendo || animacionCargada == null) return;

            tiempoActual += Time.deltaTime * velocidad;

            if (tiempoActual >= duracion)
            {
                if (loop)
                    tiempoActual = tiempoActual % Mathf.Max(duracion, 0.0001f);
                else
                {
                    tiempoActual  = duracion;
                    reproduciendo = false;
                }
            }

            AplicarAnimacion(tiempoActual);
        }

        // ── Internos ────────────────────────────────────────────────────────────

        /// <summary>
        /// Restablece todos los huesos a su pose base.
        /// Equivalente a resetPose() del visor web.
        /// </summary>
        void ResetPose()
        {
            foreach (var kvp in boneMap)
            {
                if (kvp.Value == null) continue;
                if (!poseBase.TryGetValue(kvp.Key, out MD9TransformPose pose)) continue;
                kvp.Value.localPosition = pose.Posicion;
                kvp.Value.localRotation = pose.Rotacion;
                kvp.Value.localScale    = pose.Escala;
            }
        }

        /// <summary>
        /// Samplea todos los tracks y aplica a los transforms.
        /// Equivalente a applyAnimation(time) del visor web.
        /// </summary>
        void AplicarAnimacion(float tiempo)
        {
            ResetPose();
            if (animacionCargada == null) return;

            float dur        = Mathf.Max(animacionCargada.duration, 0.0001f);
            float sampleTime = tiempo >= dur
                ? dur
                : ((tiempo % dur) + dur) % dur;

            foreach (var kvp in animacionCargada.tracks)
            {
                if (!boneMap.TryGetValue(kvp.Key, out Transform bone) || bone == null)
                    continue;

                ANITrack track = kvp.Value;

                if (ANIReader.SampleVector(track.positions, sampleTime, out Vector3 pos))
                    bone.localPosition = pos;

                if (ANIReader.SampleQuaternion(track.rotations, sampleTime, out Quaternion rot))
                    bone.localRotation = rot;

                if (ANIReader.SampleVector(track.scales, sampleTime, out Vector3 scale))
                    bone.localScale = scale;
            }
        }
    }
}
