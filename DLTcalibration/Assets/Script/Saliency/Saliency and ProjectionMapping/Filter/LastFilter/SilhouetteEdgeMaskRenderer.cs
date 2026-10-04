using UnityEngine;
using System.IO;

public class SilhouetteEdgeMaskRenderer : MonoBehaviour
{
    [Header("Shaders")]
    public Shader solidColorShader;
    public Shader edgeDetectShader;

    [Header("References")]
    public MeshFilter meshFilter;
    public Camera mainCamera; // 변수명 유지

    [HideInInspector] public RenderTexture silhouetteMask;
    [HideInInspector] public RenderTexture edgeMask;

    [Range(0.01f, 1.0f)] public float edgeThreshold = 0.1f;

    private Material edgeMat;
    private Material solidMat;
    private bool isInitialized = false;

    void Awake() { }
    void Start() { }

    public void Init()
    {
        // ★ [수정] 여기서 MeshFilter나 Camera를 미리 찾지 않습니다.
        // 캘리브레이터가 Render()를 호출할 때 넣어줄 것이기 때문입니다.

        if (solidColorShader == null) solidColorShader = Shader.Find("Hidden/SilhouetteSolidColor");
        if (edgeDetectShader == null) edgeDetectShader = Shader.Find("Hidden/SilhouetteEdgeShader");

        if (solidColorShader == null || edgeDetectShader == null)
        {
            Debug.LogError("[Silhouette] 쉐이더 오류: Resources 폴더를 확인하세요.");
            return;
        }

        if (solidMat == null) solidMat = new Material(solidColorShader);
        if (edgeMat == null) edgeMat = new Material(edgeDetectShader);

        edgeMat.SetFloat("_EdgeThreshold", edgeThreshold);

        isInitialized = true;
    }

    public void Render(Camera targetCam, MeshFilter targetMesh)
    {
        if (!isInitialized) Init();
        if (!isInitialized) return;

        // ★ [수정] 렌더링 직전에 할당하고 검사합니다.
        this.mainCamera = targetCam;
        this.meshFilter = targetMesh;

        if (meshFilter == null || mainCamera == null)
        {
            // 아직 데이터가 안 들어왔으면 조용히 리턴 (에러 로그 남발 방지)
            return;
        }

        // 마스크는 이 카메라의 픽셀 해상도로 만든다. 마스크 조회(SaliencyUtils.FilterVerticesByEdge)가
        // camera.WorldToScreenPoint 픽셀 좌표를 그대로 쓰기 때문.
        // (예전에는 Screen 크기, 즉 Display 1 해상도로 만들어서 프로젝터 픽셀과 전혀 맞지 않았음)
        int w = mainCamera.pixelWidth;
        int h = mainCamera.pixelHeight;

        if (silhouetteMask == null || silhouetteMask.width != w || silhouetteMask.height != h)
        {
            if (silhouetteMask != null) silhouetteMask.Release();
            silhouetteMask = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "SilhouetteMask" };
        }

        if (edgeMask == null || edgeMask.width != w || edgeMask.height != h)
        {
            if (edgeMask != null) edgeMask.Release();
            edgeMask = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "EdgeMask" };
        }

        var oldRT = RenderTexture.active;
        RenderTexture.active = silhouetteMask;
        GL.Clear(true, true, Color.black);

        GL.PushMatrix();

        Matrix4x4 vp = mainCamera.projectionMatrix * mainCamera.worldToCameraMatrix;
        GL.LoadProjectionMatrix(vp);

        if (solidMat.SetPass(0))
        {
            Graphics.DrawMeshNow(meshFilter.sharedMesh, meshFilter.transform.localToWorldMatrix);
        }

        GL.PopMatrix();
        RenderTexture.active = oldRT;

        if (edgeMat != null)
        {
            edgeMat.SetFloat("_EdgeThreshold", edgeThreshold);
            Graphics.Blit(silhouetteMask, edgeMask, edgeMat);
        }
    }

    public RenderTexture GetEdgeMask() => edgeMask;

    public RenderTexture GetSilhouetteMask() => silhouetteMask;

    // --- 저장 유틸리티 (기존 유지) ---
    public void SaveRenderTextureToPNG(RenderTexture rt, string filename)
    {
        if (rt == null) return;

        var oldRT = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();

        RenderTexture.active = oldRT;

        byte[] bytes = tex.EncodeToPNG();
        string path = Path.Combine(Application.dataPath, filename);
        File.WriteAllBytes(path, bytes);
        Debug.Log($"[Save] 저장 완료: {path}");
    }

    public void SaveEdgeMaskToPNG(string filename = "EdgeMaskSnapshot.png")
    {
        SaveRenderTextureToPNG(edgeMask, filename);
    }
}

//using UnityEngine;
//using System.IO;

//[RequireComponent(typeof(Camera))]
//public class SilhouetteEdgeMaskRenderer : MonoBehaviour
//{
//    public Shader solidColorShader; // SilhouetteSolidColor.shader
//    public Shader edgeDetectShader; // SilhouetteEdgeShader
//    public MeshFilter meshFilter;   // 타겟 메쉬

//    [HideInInspector] public RenderTexture silhouetteMask;
//    [HideInInspector] public RenderTexture edgeMask;

//    [Range(0.01f, 1.0f)] public float edgeThreshold = 0.1f;

//    private Camera cam;
//    private Material edgeMat;
//    private Material solidMat;

//    void Awake()
//    {
//        cam = GetComponent<Camera>();
//        cam.depthTextureMode = DepthTextureMode.None;
//        AutoAssignMeshFilter();

//        if (meshFilter == null || meshFilter.sharedMesh == null)
//        {
//            Debug.LogError("[Silhouette] meshFilter가 설정되지 않았습니다.");
//            return;
//        }
//        // Shader가 비어있다면 기본 할당
//        if (solidColorShader == null)
//            solidColorShader = Shader.Find("Hidden/SilhouetteSolidColor");
//        if (edgeDetectShader == null)
//            edgeDetectShader = Shader.Find("Hidden/SilhouetteEdgeShader");

//        solidMat = new Material(solidColorShader);
//        edgeMat = new Material(edgeDetectShader);
//        edgeMat.SetFloat("_EdgeThreshold", edgeThreshold);
//    }

//    public void Render()
//    {

//        int w = Screen.width;
//        int h = Screen.height;
//        // RenderTexture 생성 및 크기 체크
//        if (silhouetteMask == null || silhouetteMask.width != w || silhouetteMask.height != h)
//        {
//            if (silhouetteMask != null) silhouetteMask.Release();
//            silhouetteMask = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "SilhouetteMask" };
//        }

//        if (edgeMask == null || edgeMask.width != w || edgeMask.height != h)
//        {
//            if (edgeMask != null) edgeMask.Release();
//            edgeMask = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "EdgeMask" };
//        }

//        // ---------- DrawMesh 방식으로 직접 렌더링 ----------
//        var oldRT = RenderTexture.active;
//        RenderTexture.active = silhouetteMask;
//        GL.Clear(true, true, Color.black);

//        GL.PushMatrix();
//        Matrix4x4 vp = cam.projectionMatrix * cam.worldToCameraMatrix;
//        GL.LoadProjectionMatrix(vp);

//        solidMat.SetPass(0);
//        Graphics.DrawMeshNow(meshFilter.sharedMesh, meshFilter.transform.localToWorldMatrix);
//        GL.PopMatrix();

//        RenderTexture.active = oldRT;

//        // ---------- Edge Shader 적용 ----------
//        Graphics.Blit(silhouetteMask, edgeMask, edgeMat);
//    }

//    public RenderTexture GetEdgeMask() => edgeMask;
//    public RenderTexture GetSilhouetteMask() => silhouetteMask;

//    public void SaveRenderTextureToPNG(RenderTexture rt, string filename)
//    {
//        if (rt == null)
//        {
//            Debug.LogError($"[SaveRenderTextureToPNG] {filename} 저장 실패: RenderTexture가 null입니다.");
//            return;
//        }

//        RenderTexture.active = rt;
//        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
//        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
//        tex.Apply();
//        RenderTexture.active = null;

//        byte[] bytes = tex.EncodeToPNG();
//        string path = Path.Combine(Application.dataPath, filename);
//        File.WriteAllBytes(path, bytes);
//        Debug.Log($"[SaveRenderTextureToPNG] 저장 완료: {path}");
//    }

//    public void SaveEdgeMaskToPNG(string filename = "EdgeMaskSnapshot.png")
//    {
//        SaveRenderTextureToPNG(edgeMask, filename);
//    }

//    public void SaveSilhouetteMaskToPNG(string filename = "SilhouetteMaskSnapshot.png")
//    {
//        SaveRenderTextureToPNG(silhouetteMask, filename);
//    }
//    private void AutoAssignMeshFilter()
//    {
//        int excludedLayer = LayerMask.NameToLayer("Vertex in 3D");

//        MeshFilter[] candidates = FindObjectsOfType<MeshFilter>();
//        float minDist = float.MaxValue;
//        MeshFilter selected = null;

//        foreach (var mf in candidates)
//        {
//            GameObject go = mf.gameObject;

//            if (!go.activeInHierarchy) continue;                    // 비활성화된 오브젝트 제외
//            if (go.layer == excludedLayer) continue;                // 제외 Layer이면 스킵

//            Vector3 toMesh = go.transform.position - cam.transform.position;
//            float dot = Vector3.Dot(cam.transform.forward, toMesh); // 카메라 앞에 있는지 확인
//            if (dot > 0 && dot < minDist)
//            {
//                minDist = dot;
//                selected = mf;
//            }
//        }

//        if (selected != null)
//        {
//            meshFilter = selected;
//            Debug.Log($"[Silhouette] 자동 선택된 Mesh: {meshFilter.name}");
//        }
//        else
//        {
//            Debug.LogWarning("[Silhouette] 활성화된 대상 Mesh를 찾지 못했습니다.");
//        }
//    }
//}
