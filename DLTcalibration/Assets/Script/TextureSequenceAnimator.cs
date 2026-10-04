using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Video;

// 모델별 4D 텍스처(이미지 시퀀스 또는 비디오)를 재생한다. (예전 KangarooTextureAnimator를 범용으로 바꾼 것)
//
// 새 모델에 4D 텍스처를 붙이는 방법 (둘 중 하나):
//   1) Assets/Resources/4D_Textures/<모델 id>/ 폴더에 이미지들을 넣는다. 그 모델을 불러오면 자동 재생.
//      (모델 id = DefaultLibrary.txt의 "id". 예: Kangaroo -> 4D_Textures/Kangaroo)
//   2) DefaultLibrary.txt 항목에 "animatedTexturePath"로 폴더를 직접 지정한다.
//      Resources 기준 경로(예: "4D_Textures/MyAnim") 또는 디스크 절대 경로(예: "C:/Anim/MyAnim") 모두 가능.
//      mp4/mov/webm 비디오는 StreamingAssets 기준 경로(예: "StreamingAssets/VideoTextures/MyAnim.mp4")도 가능.
// 재생 순서는 파일 이름 순서(숫자는 크기 순: frame_2 < frame_10). 속도는 "animatedTextureFps"(없으면 defaultFps).
// 시퀀스가 없는 모델은 원래 텍스처를 그대로 쓴다.
public class TextureSequenceAnimator : MonoBehaviour
{
    public static TextureSequenceAnimator Instance;

    [Header("Sequence")]
    public string resourceRoot = "4D_Textures"; // 모델 id 폴더를 찾는 Resources 하위 경로
    public float defaultFps = 24f;
    public bool playing = true;                  // P 키로 재생/일시정지

    private Texture2D[] frames;
    private float fps;
    private readonly List<Material> targetMaterials = new List<Material>();
    private float timer;
    private int currentFrame;
    private VideoPlayer videoPlayer;
    private bool videoReady;
    private string videoSource;

    // 같은 시퀀스를 다시 읽지 않도록 경로별로 보관
    private readonly Dictionary<string, Texture2D[]> cache = new Dictionary<string, Texture2D[]>();

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P) && !HotkeyGuard.Blocked) TogglePlaying();

        if (IsVideoMode)
        {
            // 일부 플랫폼은 Prepare 완료 직후 첫 Update에서 texture를 만든다.
            if (videoReady && videoPlayer.texture != null) ApplyVideoTexture();
            return;
        }

        if (!playing || frames == null || frames.Length == 0 || targetMaterials.Count == 0) return;

        timer += Time.deltaTime;
        float frameTime = 1f / fps;
        if (timer < frameTime) return;
        timer %= frameTime;
        currentFrame = (currentFrame + 1) % frames.Length;
        foreach (Material m in targetMaterials) if (m != null) m.mainTexture = frames[currentFrame];
    }

    public bool HasSequence => IsVideoMode ? videoReady : frames != null && frames.Length > 0;
    public bool IsVideoMode => videoPlayer != null && !string.IsNullOrEmpty(videoSource);
    public bool IsVideoReady => IsVideoMode && videoReady;
    public long CurrentVideoFrame => IsVideoMode ? videoPlayer.frame : -1;
    public double CurrentVideoTime => IsVideoMode ? videoPlayer.time : 0.0;
    public Texture CurrentTexture => IsVideoMode ? videoPlayer.texture :
        (frames != null && frames.Length > 0 ? frames[currentFrame] : null);

    public void TogglePlaying()
    {
        if (!HasSequence) return;
        playing = !playing;
        if (IsVideoMode)
        {
            if (playing) videoPlayer.Play();
            else videoPlayer.Pause();
        }
        Debug.Log($"[4D Texture] {(playing ? "재생" : "일시정지")}");
    }

    // 모델을 불러올 때 RuntimeMeshLoader가 호출한다. entry가 없으면(경로로 직접 불러온 모델) 재생하지 않는다.
    public void Apply(GameObject model, MeshEntry entry)
    {
        ResetVideo();
        frames = null;
        // 이전 모델용으로 만든 재질 복사본(r.material)은 모델이 지워져도 남으므로 정리
        foreach (Material m in targetMaterials) if (m != null) Destroy(m);
        targetMaterials.Clear();
        timer = 0f;
        currentFrame = 0;
        if (model == null || entry == null) return;

        string source = !string.IsNullOrEmpty(entry.animatedTexturePath)
            ? entry.animatedTexturePath
            : $"{resourceRoot}/{entry.id}";

        if (IsVideoSource(source))
        {
            CollectTargetMaterials(model);
            if (targetMaterials.Count == 0) return;
            StartVideo(source, entry.id);
            return;
        }

        Texture2D[] sequence = LoadSequence(source);
        if (sequence.Length == 0)
        {
            if (!string.IsNullOrEmpty(entry.animatedTexturePath))
                Debug.LogWarning($"[4D Texture] '{entry.animatedTexturePath}'에서 이미지를 찾지 못했습니다.");
            return;
        }

        CollectTargetMaterials(model);
        if (targetMaterials.Count == 0) return;
        frames = sequence;
        fps = entry.animatedTextureFps > 0f ? entry.animatedTextureFps : defaultFps;
        foreach (Material m in targetMaterials) m.mainTexture = frames[0];
        Debug.Log($"[4D Texture] {entry.id}: {frames.Length}장, {fps}fps ({source})");
    }

    private void CollectTargetMaterials(GameObject model)
    {
        // 모델 자체의 렌더러만 (모델 아래에 붙는 버텍스 구/추천 마커는 레이어가 달라서 제외)
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            if (renderer.gameObject.layer == model.layer) targetMaterials.Add(renderer.material);
    }

    private static bool IsVideoSource(string source)
    {
        string extension = Path.GetExtension(source);
        return extension.Equals(".mp4", System.StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mov", System.StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webm", System.StringComparison.OrdinalIgnoreCase);
    }

    private void StartVideo(string source, string modelId)
    {
        string path = ResolveVideoPath(source);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[4D Texture Video] '{path}'에서 비디오를 찾지 못했습니다.");
            return;
        }

        videoSource = path;
        videoReady = false;
        videoPlayer = gameObject.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.skipOnDrop = false;
        videoPlayer.isLooping = true;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.renderMode = VideoRenderMode.APIOnly;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.url = new System.Uri(path).AbsoluteUri;
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.Prepare();
        Debug.Log($"[4D Texture Video] {modelId}: 준비 중 ({source})");
    }

    private static string ResolveVideoPath(string source)
    {
        if (Path.IsPathRooted(source)) return Path.GetFullPath(source);

        string normalized = source.Replace('\\', '/');
        const string streamingPrefix = "StreamingAssets/";
        if (normalized.StartsWith(streamingPrefix, System.StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(streamingPrefix.Length);

        return Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, normalized));
    }

    private void OnVideoPrepared(VideoPlayer player)
    {
        videoReady = true;
        ApplyVideoTexture();
        if (playing) player.Play();

        double duration = player.frameRate > 0.0 ? player.frameCount / player.frameRate : 0.0;
        Debug.Log($"[4D Texture Video] 준비 완료: {player.width}x{player.height}, " +
                  $"{player.frameRate:F2}fps, {duration:F2}s, {player.frameCount} frames");
    }

    private void OnVideoError(VideoPlayer player, string message)
    {
        videoReady = false;
        Debug.LogError($"[4D Texture Video] 재생 오류 ({videoSource}): {message}");
    }

    private void ApplyVideoTexture()
    {
        if (videoPlayer == null || videoPlayer.texture == null) return;
        foreach (Material material in targetMaterials)
            if (material != null && material.mainTexture != videoPlayer.texture)
                material.mainTexture = videoPlayer.texture;
    }

    private void ResetVideo()
    {
        videoReady = false;
        videoSource = null;
        if (videoPlayer == null) return;

        videoPlayer.prepareCompleted -= OnVideoPrepared;
        videoPlayer.errorReceived -= OnVideoError;
        videoPlayer.Stop();
        Destroy(videoPlayer);
        videoPlayer = null;
    }

    void OnDestroy()
    {
        ResetVideo();
        if (Instance == this) Instance = null;
    }

    private Texture2D[] LoadSequence(string source)
    {
        if (cache.TryGetValue(source, out Texture2D[] cached)) return cached;

        Texture2D[] loaded;
        if (Path.IsPathRooted(source))
        {
            // 디스크 폴더: png/jpg를 직접 읽는다 (외부 모델용)
            loaded = Directory.Exists(source)
                ? Directory.GetFiles(source)
                    .Where(f => f.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpg", System.StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpeg", System.StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => NaturalKey(Path.GetFileNameWithoutExtension(f)))
                    .Select(f =>
                    {
                        var tex = new Texture2D(2, 2) { name = Path.GetFileNameWithoutExtension(f) };
                        tex.LoadImage(File.ReadAllBytes(f));
                        return tex;
                    })
                    .ToArray()
                : new Texture2D[0];
        }
        else
        {
            loaded = Resources.LoadAll<Texture2D>(source).OrderBy(t => NaturalKey(t.name)).ToArray();
        }

        cache[source] = loaded;
        return loaded;
    }

    // 이름 속 숫자를 자릿수 맞춰 채워서 frame_2 < frame_10 순서가 되게 한다.
    private static string NaturalKey(string name)
    {
        return Regex.Replace(name, @"\d+", m => m.Value.PadLeft(10, '0'));
    }
}
