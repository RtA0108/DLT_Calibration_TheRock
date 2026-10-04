using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SaliencyMapVisualizer : MonoBehaviour
{
    [Header("Settings")]
    public SaliencyMode saliencyMode = SaliencyMode.Entropy;

    // Alpha 값 조절 (기본값 0.8)
    [Range(0.1f, 5.0f)]
    public float visualizationAlpha = 0.8f;

    [Header("References")]
    public MeshFilter meshFilter;
    public Camera mainCamera;
    public LayerMask visibilityLayerMask;

    // 경로 변수 (HideInInspector)
    [HideInInspector] public string cfsObjPath;
    [HideInInspector] public string cfsTxtPath;
    [HideInInspector] public string texObjPath;
    [HideInInspector] public string texTxtPath;

    private Dictionary<SaliencyMode, Color[]> cachedColors = new Dictionary<SaliencyMode, Color[]>();
    private Material[] originalMaterials;
    private Material runtimeHeatmapMat;
    private bool isInitialized = false;

    // ★ RuntimeMeshLoader가 호출하는 Init 함수 (인자 5개)
    public void Init(string cfsObj, string cfsTxt, string texObj, string texTxt, LayerMask mask)
    {
        this.cfsObjPath = cfsObj;
        this.cfsTxtPath = cfsTxt;
        this.texObjPath = texObj;
        this.texTxtPath = texTxt;
        this.visibilityLayerMask = mask;

        if (meshFilter == null) meshFilter = GetComponentInChildren<MeshFilter>();
        if (mainCamera == null) mainCamera = Camera.main;

        if (meshFilter == null) return;

        StoreOriginalMaterials();
        CreateHeatmapMaterial(); // ★ 여기서 쉐이더 찾음

        isInitialized = true;

        if (this.enabled)
        {
            ComputeAndApply();
            ApplyVertexColorMaterial();
        }
    }

    void OnEnable()
    {
        if (!isInitialized) return;
        ComputeAndApply();
        ApplyVertexColorMaterial();
    }

    void OnDisable()
    {
        if (!isInitialized) return;
        RestoreOriginalMaterials();
    }

    void OnValidate() { if (isInitialized && this.enabled) ComputeAndApply(); }

    // --- 내부 로직 ---

    void CreateHeatmapMaterial()
    {
        // ★ [1순위] 과거 성공 코드 복구: VertexColorLitS 쉐이더부터 찾음
        // (HeatmapMat 파일 로드 로직 삭제함)

        Shader shader = Shader.Find("Universal Render Pipeline/VertexColorLitS");
        if (shader == null) shader = Shader.Find("VertexColorLitS"); // 이름이 다를 경우

        // 2순위: 없을 경우 대비한 기본 URP 쉐이더 (보라색 방지)
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        if (shader != null)
        {
            runtimeHeatmapMat = new Material(shader);
            runtimeHeatmapMat.enableInstancing = false;
            // Lit 쉐이더일 경우 Vertex Color가 켜져야 하므로 키워드 활성화
            runtimeHeatmapMat.EnableKeyword("_VERTEX_COLORS");
        }
        else
        {
            Debug.LogError("[Visualizer] 'VertexColorLitS' 쉐이더를 찾을 수 없습니다.");
        }
    }

    void ComputeAndApply()
    {
        if (meshFilter == null) return;

        // ★ Transform Reset (데이터 매칭용)
        Vector3 oldPos = transform.position;
        Quaternion oldRot = transform.rotation;
        Vector3 oldScale = transform.localScale;

        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        float localL = Vector3.Distance(meshFilter.sharedMesh.bounds.min, meshFilter.sharedMesh.bounds.max);

        Vector3[] allVertices = SaliencyUtils.GetUniqueWorldVertices(meshFilter);
        List<Vector3> allVertexList = new List<Vector3>(allVertices);
        Dictionary<Vector3, float> map = null;

        try
        {
            switch (saliencyMode)
            {
                case SaliencyMode.Entropy:
                    map = EntropySaliencyComputer.Compute(meshFilter, allVertexList, localL);
                    break;
                case SaliencyMode.Curvature:
                    map = MeshSaliencyComputer.Compute(meshFilter, allVertexList, localL);
                    break;
                case SaliencyMode.CFSCNN:
                    map = SaliencyDataLoader.GetSaliencyMap(meshFilter, allVertexList, cfsObjPath, cfsTxtPath);
                    break;
                case SaliencyMode.TexMesh:
                    map = SaliencyDataLoader.GetSaliencyMap(meshFilter, allVertexList, texObjPath, texTxtPath);
                    break;
            }

            if (map != null)
            {
                var normalized = SaliencyUtils.NormalizeSaliencyMap(map);
                var grouped = SaliencyUtils.GroupSaliencyByRoundedPosition(normalized, 1000);

                cachedColors[saliencyMode] = GenerateColors(meshFilter.mesh, grouped);
                meshFilter.mesh.colors = cachedColors[saliencyMode];
            }
        }
        catch (System.Exception e) { Debug.LogError($"[Visualizer Error] {e.Message}"); }
        finally
        {
            // 복구
            transform.position = oldPos;
            transform.rotation = oldRot;
            transform.localScale = oldScale;
        }
    }

    Color[] GenerateColors(Mesh mesh, Dictionary<Vector3, float> groupedSaliency)
    {
        Vector3[] vertices = mesh.vertices;
        Color[] colors = new Color[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = meshFilter.transform.TransformPoint(vertices[i]);
            Vector3 rounded = new Vector3(
                Mathf.Round(worldPos.x * 1000f) / 1000f,
                Mathf.Round(worldPos.y * 1000f) / 1000f,
                Mathf.Round(worldPos.z * 1000f) / 1000f
            );

            if (groupedSaliency.TryGetValue(rounded, out float saliency))
            {
                colors[i] = EvaluateTurboColormap(Mathf.Pow(saliency, visualizationAlpha));
            }
            else colors[i] = Color.black;
        }
        return colors;
    }

    void ApplyVertexColorMaterial()
    {
        var renderer = meshFilter.GetComponent<Renderer>();
        if (renderer == null || runtimeHeatmapMat == null) return;

        Material[] newMats = new Material[renderer.sharedMaterials.Length];
        for (int i = 0; i < newMats.Length; i++) newMats[i] = runtimeHeatmapMat;
        renderer.materials = newMats;
    }

    void StoreOriginalMaterials()
    {
        var renderer = meshFilter.GetComponent<Renderer>();
        if (renderer != null) originalMaterials = renderer.sharedMaterials;
    }

    void RestoreOriginalMaterials()
    {
        var renderer = meshFilter.GetComponent<Renderer>();
        if (renderer != null && originalMaterials != null) renderer.materials = originalMaterials;
    }

    private static readonly Color[] turboColors = new Color[] {
        new Color(0.18995f, 0.07176f, 0.23217f), new Color(0.25107f, 0.25237f, 0.63302f),
        new Color(0.27628f, 0.51281f, 0.83584f), new Color(0.19806f, 0.75294f, 0.64386f),
        new Color(0.31121f, 0.90487f, 0.38750f), new Color(0.55814f, 0.96702f, 0.26579f),
        new Color(0.83394f, 0.88904f, 0.17860f), new Color(0.99314f, 0.69015f, 0.12952f),
        new Color(0.98730f, 0.42773f, 0.14025f), new Color(0.89427f, 0.12115f, 0.16104f)
    };
    private Color EvaluateTurboColormap(float t)
    {
        t = Mathf.Clamp01(t);
        float scaled = t * (turboColors.Length - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, turboColors.Length - 2);
        return Color.Lerp(turboColors[i], turboColors[i + 1], scaled - i);
    }
}
//using System.Collections;
//using System.Collections.Generic;
//using System.Linq;
//using UnityEngine;

////public enum SaliencyMode { Entropy, Curvature }

//public class SaliencyMapVisualizer : MonoBehaviour
//{
//    public MeshFilter meshFilter;
//    public Camera mainCamera;
//    public LayerMask visibilityLayerMask;
//    public bool runOnStart = true;
//    public SaliencyMode saliencyMode = SaliencyMode.Entropy;
//    Material[] originalMaterials;
//    [Header("CFSCNN Source File Paths")]
//    public string cfsSaliencyObjPath = "Assets/Meshes/Chick_Tri.obj";
//    public string cfsSaliencyTxtPath = "Assets/Resources/CfSCNN/Chick_Tri_saliency.txt";

//    [Header("TexMesh Source File Paths")]
//    public string texSaliencyObjPath = "Assets/Meshes/Chick_Tri.obj";
//    public string texSaliencyTxtPath = "Assets/Resources/TexMesh/Chick_vertex_saliency.txt";
//    private void Start()
//    {
//        // 자동으로 하위에서 MeshFilter를 가져오도록 보정
//        if (meshFilter == null)
//        {
//            meshFilter = GetComponentInChildren<MeshFilter>();
//            if (meshFilter == null)
//            {
//                Debug.LogError("[SaliencyMapVisualizer] 하위에서 MeshFilter를 찾을 수 없습니다.");
//                return;
//            }
//        }

//        if (runOnStart)
//        {
//            StoreOriginalMaterials();
//            ApplyVertexColorMaterial();
//            StartCoroutine(VisualizeOnce());
//        }
//    }
//    private void LogSaliencyStatistics(Dictionary<Vector3, float> saliencyMap)
//    {
//        float[] values = saliencyMap.Values.ToArray();
//        float min = values.Min();
//        float max = values.Max();
//        float avg = values.Average();
//        float median = values.OrderBy(v => v).ElementAt(values.Length / 2);
//        float stdDev = Mathf.Sqrt(values.Select(v => Mathf.Pow(v - avg, 2)).Average());

//        Debug.Log($"[Saliency Stats] Count: {values.Length}, Min: {min:F4}, Max: {max:F4}, Avg: {avg:F4}, Median: {median:F4}, StdDev: {stdDev:F4}");
//    }
//    void OnDisable()
//    {
//        RestoreOriginalMaterials();
//    }
//    void StoreOriginalMaterials()
//    {
//        var renderer = meshFilter.GetComponent<Renderer>() ?? meshFilter.GetComponentInChildren<Renderer>();
//        if (renderer != null)
//            originalMaterials = renderer.sharedMaterials;
//    }

//    void RestoreOriginalMaterials()
//    {
//        var renderer = meshFilter.GetComponent<Renderer>() ?? meshFilter.GetComponentInChildren<Renderer>();
//        if (renderer != null && originalMaterials != null)
//        {
//            renderer.materials = originalMaterials;
//            Debug.Log("[SaliencyMapVisualizer] 원래 머티리얼로 복원됨");
//        }
//    }

//    void ApplyVertexColorMaterial()
//    {
//        if (!Application.isPlaying) return;

//        var renderer = meshFilter.GetComponent<Renderer>() ?? meshFilter.GetComponentInChildren<Renderer>();
//        if (renderer == null)
//        {
//            Debug.LogError("[SaliencyMapVisualizer] MeshRenderer를 찾을 수 없습니다.");
//            return;
//        }

//        Shader shader = Shader.Find("Universal Render Pipeline/VertexColorLitS");
//        if (shader == null)
//        {
//            Debug.LogError("[SaliencyMapVisualizer] 셰이더를 찾을 수 없습니다. 이름을 확인하세요.");
//            return;
//        }

//        Material material = new Material(shader);
//        material.enableInstancing = false;

//        int slotCount = renderer.sharedMaterials.Length;
//        Material[] materials = Enumerable.Repeat(material, slotCount).ToArray();
//        renderer.materials = materials;

//        //Debug.Log("[SaliencyMapVisualizer] 런타임에 VertexColorLitS 셰이더 적용 완료");

//        // 디버깅: vertex color 존재 여부 확인
//        var mesh = meshFilter.sharedMesh;
//        int colorCount = mesh.colors?.Length ?? 0;
//        Debug.Log($"[VC Check] VertexCount: {mesh.vertexCount}, ColorCount: {colorCount}, VC 존재 여부: {(colorCount > 0 ? "있음" : "없음")}");

//        // vertex color가 없으면 임의 색상이라도 채워야 셰이더가 동작함

//        if (colorCount == 0)
//        {
//            Color[] colors = new Color[mesh.vertexCount];
//            for (int i = 0; i < colors.Length; i++)
//                colors[i] = Color.gray;
//            mesh.colors = colors;
//            Debug.Log("[SaliencyMapVisualizer] vertex color가 비어있어 기본 회색으로 채움");
//        }
//    }

//    IEnumerator VisualizeOnce()
//    {
//        if (meshFilter == null || mainCamera == null)
//        {
//            Debug.LogError("[SaliencyMapVisualizer] MeshFilter 또는 Camera가 설정되지 않았습니다.");
//            yield break;
//        }

//        Vector3[] allVertices = SaliencyUtils.GetUniqueWorldVertices(meshFilter);
//        List<Vector3> allVertexList = new List<Vector3>(allVertices);
//        Bounds bounds = meshFilter.mesh.bounds;
//        Vector3 scale = meshFilter.transform.lossyScale;
//        Vector3 scaledMin = Vector3.Scale(bounds.min, scale);
//        Vector3 scaledMax = Vector3.Scale(bounds.max, scale);
//        float l = Vector3.Distance(scaledMin, scaledMax);

//        Debug.Log($"[SaliencyMapVisualizer] σ = multi-scale 기반 saliency 계산 시작 (l = {l:F4})");

//        Dictionary<Vector3, float> saliencyMap = saliencyMode switch
//        {
//            SaliencyMode.Entropy => EntropySaliencyComputer.Compute(meshFilter, allVertexList, l),
//            SaliencyMode.Curvature => MeshSaliencyComputer.Compute(meshFilter, allVertexList, l),
//            SaliencyMode.Spectral => SpectralSaliencyComputer.Compute(meshFilter, allVertexList, l),
//            SaliencyMode.CFSCNN => SaliencyDataLoader.GetSaliencyMap(meshFilter, allVertexList, cfsSaliencyObjPath, cfsSaliencyTxtPath),
//            SaliencyMode.TexMesh => SaliencyDataLoader.GetSaliencyMap(meshFilter, allVertexList, texSaliencyObjPath, texSaliencyTxtPath),
//            _ => throw new System.Exception("Unknown saliency mode")
//        };

//        var normalized = SaliencyUtils.NormalizeSaliencyMap(saliencyMap);
//        var grouped = SaliencyUtils.GroupSaliencyByRoundedPosition(normalized, 1000); // 소수점 3자리까지 그룹핑

//        LogSaliencyStatistics(grouped);
//        ApplyVertexColors(meshFilter.mesh, grouped);


//        Debug.Log("[SaliencyMapVisualizer] 시각화 완료.");
//        yield return null;
//    }
//    private void ApplyVertexColors(Mesh mesh, Dictionary<Vector3, float> groupedSaliency)
//    {
//        Vector3[] vertices = mesh.vertices;
//        Color[] colors = new Color[vertices.Length];
//        Dictionary<Vector3, float> saliencyConsistencyCheck = new();

//        // Percentile 기준 계산
//        var sorted = groupedSaliency.Values.OrderBy(v => v).ToList();
//        float p5 = sorted[(int)(0.95f * sorted.Count)];
//        float p20 = sorted[(int)(0.80f * sorted.Count)];

//        for (int i = 0; i < vertices.Length; i++)
//        {
//            Vector3 worldPos = meshFilter.transform.TransformPoint(vertices[i]);
//            // 동일 위치 정점에 같은 색을 주기 위해 반올림 위치로 매칭
//            Vector3 rounded = new Vector3(
//                Mathf.Round(worldPos.x * 1000f) / 1000f,
//                Mathf.Round(worldPos.y * 1000f) / 1000f,
//                Mathf.Round(worldPos.z * 1000f) / 1000f
//            );
//            if (groupedSaliency.TryGetValue(rounded, out float saliency))
//            {
//                colors[i] = EvaluateTurboColormap(Mathf.Pow(saliency, 0.8f));
//                //  색상 매핑 (범위 구분)
//                //if (saliency >= p5)
//                //    colors[i] = Color.red;
//                //else if (saliency >= p20)
//                //    colors[i] = new Color(1f, 0.65f, 0f); // 주황색
//                //else
//                //    colors[i] = Color.blue;

//                if (saliencyConsistencyCheck.TryGetValue(rounded, out float existing))
//                {
//                    if (Mathf.Abs(existing - saliency) > 1e-4f)
//                    {
//                        Debug.LogWarning($"[Mismatch] Same position {rounded} has inconsistent saliency: {existing:F5} vs {saliency:F5}");
//                    }
//                }
//                else
//                {
//                    saliencyConsistencyCheck[rounded] = saliency;
//                }
//            }
//            else
//            {
//                colors[i] = Color.black;
//            }
//        }
//        mesh.colors = colors;
//    }
//    private static readonly Color[] turboColors = new Color[]
//    {
//        new Color(0.18995f, 0.07176f, 0.23217f),
//        new Color(0.25107f, 0.25237f, 0.63302f),
//        new Color(0.27628f, 0.51281f, 0.83584f),
//        new Color(0.19806f, 0.75294f, 0.64386f),
//        new Color(0.31121f, 0.90487f, 0.38750f),
//        new Color(0.55814f, 0.96702f, 0.26579f),
//        new Color(0.83394f, 0.88904f, 0.17860f),
//        new Color(0.99314f, 0.69015f, 0.12952f),
//        new Color(0.98730f, 0.42773f, 0.14025f),
//        new Color(0.89427f, 0.12115f, 0.16104f)
//    };

//    private Color EvaluateTurboColormap(float t)
//    {
//        t = Mathf.Clamp01(t);
//        float scaled = t * (turboColors.Length - 1);
//        int i = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, turboColors.Length - 2);
//        float f = scaled - i;
//        return Color.Lerp(turboColors[i], turboColors[i + 1], f);
//    }

//}
