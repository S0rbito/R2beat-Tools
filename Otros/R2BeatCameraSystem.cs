using System.Collections;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using UnityEngine.Networking;

public class R2BeatCameraSystem : MonoBehaviour
{
    [Header("Configuración")]
    public string basePath = "rnr_script/camera";
    public float scale = 0.01f;
    public float fps = 60f;
    public bool playIntroOnStart = true;
    public bool autoLoopAfterIntro = true;

    [Header("Debug")]
    public bool showGizmos = true;
    [SerializeField] private string currentAnimationId = "";
    [SerializeField] private int currentFrame = 0;
    [SerializeField] private int totalFrames = 0;

    struct CameraFrame
    {
        public Vector3 position;
        public Vector3 target;
    }

    private Dictionary<string, string> cameraIdMap = new Dictionary<string, string>();
    private List<CameraFrame> frames = new List<CameraFrame>();
    private float fov = 45f;
    private bool isPlaying = false;
    private bool isLooping = false;
    private float elapsedTime = 0f;
    private float totalDuration = 0f;
    private bool isReady = false;

    void Start()
    {
        StartCoroutine(LoadCameraIdMapCoroutine(() =>
        {
            isReady = true;
            if (playIntroOnStart)
                StartCoroutine(PlayIntroCoroutine());
        }));
    }

    // --- API pública ---
    public void PlayIntro()     { StartCoroutine(PlayIntroCoroutine()); }
    public void PlayIntroLoop() { StartCoroutine(PlayIntroLoopCoroutine()); }
    public void PlayById(string id) { StartCoroutine(PlayByIdCoroutine(id)); }
    public void Stop() { isPlaying = false; isLooping = false; }

    IEnumerator PlayIntroCoroutine()
    {
        yield return StartCoroutine(LoadXMLCoroutine(BuildURL("intro.xml"), "Intro", false));
    }

    IEnumerator PlayIntroLoopCoroutine()
    {
        yield return StartCoroutine(LoadXMLCoroutine(BuildURL("intro_loop.xml"), "IntroLoop", true));
    }

    IEnumerator PlayByIdCoroutine(string cameraId)
    {
        while (!isReady) yield return null;

        if (!cameraIdMap.ContainsKey(cameraId))
        {
            Debug.LogError($"R2BeatCameraSystem: Id '{cameraId}' no encontrado");
            yield break;
        }

        string url = BuildURLFromRelative(cameraIdMap[cameraId]);
        yield return StartCoroutine(LoadXMLCoroutine(url, cameraId, false));
    }

    void Update()
    {
        if (!isPlaying || frames.Count == 0) return;

        elapsedTime += Time.deltaTime;
        currentFrame = Mathf.Clamp(Mathf.FloorToInt(elapsedTime * fps), 0, frames.Count - 1);

        if (elapsedTime >= totalDuration)
        {
            if (isLooping)
            {
                elapsedTime = 0f;
            }
            else
            {
                isPlaying = false;
                ApplyFrames(frames.Count - 1, frames.Count - 1, 0f);
                Debug.Log($"R2BeatCameraSystem: '{currentAnimationId}' finalizada.");
                if (autoLoopAfterIntro && currentAnimationId == "Intro")
                    StartCoroutine(PlayIntroLoopCoroutine());
                return;
            }
        }

        float frameFloat = elapsedTime * fps;
        int frameA = Mathf.Clamp(Mathf.FloorToInt(frameFloat), 0, frames.Count - 1);
        int frameB = Mathf.Clamp(frameA + 1, 0, frames.Count - 1);
        ApplyFrames(frameA, frameB, frameFloat - frameA);
    }

    void ApplyFrames(int a, int b, float t)
    {
        transform.position = Vector3.Lerp(frames[a].position, frames[b].position, t);
        transform.LookAt(Vector3.Lerp(frames[a].target, frames[b].target, t));
    }

    IEnumerator LoadCameraIdMapCoroutine(System.Action onDone)
    {
        string url = BuildURL("cameraid.txt");
        Debug.Log("R2BeatCameraSystem: Cargando cameraid desde: " + url);

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"R2BeatCameraSystem: Error cargando cameraid.txt: {req.error}\nURL: {url}");
                yield break;
            }

            string raw = req.downloadHandler.text.Trim();
            if (raw.StartsWith("<?xml"))
            {
                int end = raw.IndexOf("?>");
                if (end != -1) raw = raw.Substring(end + 2).Trim();
            }

            XmlDocument doc = new XmlDocument();
            try { doc.LoadXml($"<Root>{raw}</Root>"); }
            catch (System.Exception e)
            {
                Debug.LogError($"R2BeatCameraSystem: Error parseando cameraid.txt: {e.Message}");
                yield break;
            }

            cameraIdMap.Clear();
            foreach (XmlNode node in doc.SelectNodes("Root/Camera"))
            {
                string id   = node.Attributes["Id"]?.Value;
                string file = node.Attributes["File"]?.Value;
                if (id != null && file != null) cameraIdMap[id] = file;
            }

            Debug.Log($"R2BeatCameraSystem: {cameraIdMap.Count} entradas cargadas");
        }

        onDone?.Invoke();
    }

    IEnumerator LoadXMLCoroutine(string url, string animId, bool loop)
    {
        Debug.Log("R2BeatCameraSystem: Cargando XML desde: " + url);

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"R2BeatCameraSystem: Error cargando '{animId}': {req.error}\nURL: {url}");
                yield break;
            }

            XmlDocument doc = new XmlDocument();
            try { doc.LoadXml(req.downloadHandler.text); }
            catch (System.Exception e)
            {
                Debug.LogError($"R2BeatCameraSystem: Error parseando '{animId}': {e.Message}");
                yield break;
            }

            XmlNode root = doc.SelectSingleNode("Camera");
            if (root == null) { Debug.LogError("Sin nodo <Camera>"); yield break; }

            XmlNode fovNode = root.SelectSingleNode("FOV");
            if (fovNode != null)
                float.TryParse(fovNode.InnerText.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out fov);

            frames.Clear();
            foreach (XmlNode posNode in root.SelectNodes("Pos"))
            {
                string camAttr = posNode.Attributes["Camera"]?.Value;
                string tgtAttr = posNode.Attributes["Target"]?.Value;
                if (camAttr == null || tgtAttr == null) continue;
                frames.Add(new CameraFrame
                {
                    position = ParseVec3(camAttr) * scale,
                    target   = ParseVec3(tgtAttr) * scale
                });
            }

            totalDuration      = frames.Count / fps;
            currentAnimationId = animId;
            totalFrames        = frames.Count;
            elapsedTime        = 0f;
            isPlaying          = true;
            isLooping          = loop;

            Camera cam = GetComponent<Camera>();
            if (cam != null) cam.fieldOfView = fov * Mathf.Rad2Deg;

            Debug.Log($"R2BeatCameraSystem: '{animId}' — {frames.Count} frames ({totalDuration:F2}s) loop={loop}");
        }
    }

    // Devuelve URL válida para UnityWebRequest en todas las plataformas
    string BuildURL(string fileName)
    {
        string relative = basePath + "/" + fileName;
#if UNITY_ANDROID && !UNITY_EDITOR
        return Application.streamingAssetsPath + "/" + relative;
#else
        return "file://" + Application.streamingAssetsPath + "/" + relative;
#endif
    }

    string BuildURLFromRelative(string relativePath)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Application.streamingAssetsPath + "/" + relativePath;
#else
        return "file://" + Application.streamingAssetsPath + "/" + relativePath;
#endif
    }

    Vector3 ParseVec3(string s)
    {
        string[] parts = s.Trim().Split(' ');
        float x = 0, y = 0, z = 0;
        var style   = System.Globalization.NumberStyles.Float;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (parts.Length >= 3)
        {
            float.TryParse(parts[0], style, culture, out x);
            float.TryParse(parts[1], style, culture, out y);
            float.TryParse(parts[2], style, culture, out z);
        }
        return new Vector3(x, y, -z);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos || frames.Count == 0) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < frames.Count - 1; i++)
            Gizmos.DrawLine(frames[i].position, frames[i + 1].position);
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(frames[0].position, 0.5f);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(frames[frames.Count - 1].position, 0.5f);
    }
}