using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

// 모델별 4D 텍스처(이미지 시퀀스)를 재생한다. (예전 KangarooTextureAnimator를 범용으로 바꾼 것)
//
// 새 모델에 4D 텍스처를 붙이는 방법 (둘 중 하나):
//   1) Assets/Resources/4D_Textures/<모델 id>/ 폴더에 이미지들을 넣는다. 그 모델을 불러오면 자동 재생.
//      (모델 id = DefaultLibrary.txt의 "id". 예: Kangaroo -> 4D_Textures/Kangaroo)
//   2) DefaultLibrary.txt 항목에 "animatedTexturePath"로 폴더를 직접 지정한다.
//      Resources 기준 경로(예: "4D_Textures/MyAnim") 또는 디스크 절대 경로(예: "C:/Anim/MyAnim") 모두 가능.
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

    // 같은 시퀀스를 다시 읽지 않도록 경로별로 보관
    private readonly Dictionary<string, Texture2D[]> cache = new Dictionary<string, Texture2D[]>();

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P) && !HotkeyGuard.Blocked) TogglePlaying();

        if (!playing || frames == null || frames.Length == 0 || targetMaterials.Count == 0) return;

        timer += Time.deltaTime;
        float frameTime = 1f / fps;
        if (timer < frameTime) return;
        timer %= frameTime;
        currentFrame = (currentFrame + 1) % frames.Length;
        foreach (Material m in targetMaterials) if (m != null) m.mainTexture = frames[currentFrame];
    }

    public bool HasSequence => frames != null && frames.Length > 0;

    public void TogglePlaying()
    {
        if (!HasSequence) return;
        playing = !playing;
        Debug.Log($"[4D Texture] {(playing ? "재생" : "일시정지")}");
    }

    // 모델을 불러올 때 RuntimeMeshLoader가 호출한다. entry가 없으면(경로로 직접 불러온 모델) 재생하지 않는다.
    public void Apply(GameObject model, MeshEntry entry)
    {
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
        Texture2D[] sequence = LoadSequence(source);
        if (sequence.Length == 0)
        {
            if (!string.IsNullOrEmpty(entry.animatedTexturePath))
                Debug.LogWarning($"[4D Texture] '{entry.animatedTexturePath}'에서 이미지를 찾지 못했습니다.");
            return;
        }

        // 모델 자체의 렌더러만 (모델 아래에 붙는 버텍스 구/추천 마커는 레이어가 달라서 제외)
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            if (r.gameObject.layer == model.layer) targetMaterials.Add(r.material);
        if (targetMaterials.Count == 0) return;

        frames = sequence;
        fps = entry.animatedTextureFps > 0f ? entry.animatedTextureFps : defaultFps;
        foreach (Material m in targetMaterials) m.mainTexture = frames[0];
        Debug.Log($"[4D Texture] {entry.id}: {frames.Length}장, {fps}fps ({source})");
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
