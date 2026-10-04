using System.Collections.Generic;
using UnityEngine;

// 대응점 마커에 붙일 "패치" 이미지를 만든다.
// 현재 프로젝터 카메라 시점으로 모델을 한 장 그려 두고, 마커 위치 주변을 잘라 쓴다.
// 사용자는 점 하나 대신 주변 모양을 실물에 겹치도록 맞추면 되고, 패치 중심이 곧 대응점이 된다.
//   - Lines   : 보이는 실루엣 외곽선 + 꺾인 모서리를 흰 선으로 그림 (흰색/단색 실물용)
//   - Texture : 프로젝터가 실제로 비출 모델 화면 그대로 (무늬 있는 실물용)
public class PatchSnapshot
{
    public enum Mode { Lines, Texture }

    public float creaseAngle = 35f;  // 인접한 두 면이 이 각도(도)보다 크게 꺾이면 모서리 선으로 그림
    public int lineDilation = 1;     // 선 굵기 보정 (픽셀 단위 팽창 반경)

    private RenderTexture textureFrame, linesFrame;
    private Texture2D textureFrameCpu, linesFrameCpu; // 잘라내기용 CPU 복사본
    // 픽셀 배열은 촬영할 때 한 번만 꺼내 둔다. (예전에는 패치 하나 자를 때마다 화면 전체를 복사해서,
    //  1080p 프로젝터에서 점 20개면 마커를 놓을 때마다 약 330MB를 만들고 버렸음)
    private Color32[] textureFramePixels, linesFramePixels;
    private int capturedFrame = -1;
    private Matrix4x4 capturedViewProj;
    private GameObject capturedTarget;
    private float capturedCreaseAngle;

    private Material depthMat, lineMat;
    private readonly Dictionary<Mesh, EdgeCache> edgeCaches = new Dictionary<Mesh, EdgeCache>();

    // 메쉬의 모서리 인접 정보. 카메라 자세와 무관해서 메쉬마다 한 번만 만든다.
    private class EdgeCache
    {
        public Vector3[] vertices;
        public List<int> a = new List<int>(), b = new List<int>();     // 모서리 양 끝 버텍스
        public List<int> t1 = new List<int>(), t2 = new List<int>();   // 양쪽 면 (경계면 t2 = -1)
        public List<bool> crease = new List<bool>();                  // 각진 모서리 여부
        public List<bool> multiFace = new List<bool>();               // 면 3개 이상이 공유 (항상 그림)
        public Vector3[] faceNormals, faceCenters;
        public float creaseAngle = -1f;                               // crease를 계산할 때 쓴 기준 각도
        public float medianAngle;                                     // 모서리 꺾임 각도 중앙값 (자동 기준값용)
    }

    // 모서리 기준 각도 자동 결정.
    //  - 매끈한 메쉬(중앙값 10도 미만, 예: 라이브러리의 high poly 모델들 0~6도): 크게 꺾인 진짜 모서리만 -> 35도
    //  - 각진 메쉬(중앙값 10도 이상, 예: TheRock 21도): 면 경계가 실물에서도 보이므로 중앙값 x 0.8 (10~35도)
    public const float SmoothMedianLimit = 10f, SmoothMeshAngle = 35f, FacetedFactor = 0.8f;

    public float AutoCreaseAngle(GameObject target, out float medianAngle)
    {
        // 모델 메쉬 중 면이 가장 많은 것을 기준으로
        Mesh mesh = null;
        foreach (MeshFilter mf in ModelMeshFilters(target))
            if (mf.sharedMesh != null && (mesh == null || mf.sharedMesh.triangles.Length > mesh.triangles.Length)) mesh = mf.sharedMesh;

        medianAngle = mesh != null ? GetEdges(mesh).medianAngle : 0f;
        if (medianAngle < SmoothMedianLimit) return SmoothMeshAngle;
        return Mathf.Clamp(medianAngle * FacetedFactor, SmoothMedianLimit, SmoothMeshAngle);
    }

    // 같은 프레임에 같은 카메라/모델이면 다시 그리지 않는다 (R로 여러 점을 한 번에 고를 때).
    public void EnsureCaptured(Camera cam, GameObject target)
    {
        Matrix4x4 viewProj = cam.projectionMatrix * cam.worldToCameraMatrix;
        if (capturedFrame == Time.frameCount && capturedTarget == target && capturedViewProj == viewProj
            && capturedCreaseAngle == creaseAngle) return;

        int w = cam.pixelWidth, h = cam.pixelHeight;
        EnsureTexture(ref textureFrame, w, h, "PatchTextureFrame");
        EnsureTexture(ref linesFrame, w, h, "PatchLinesFrame");

        RenderTextureFrame(cam, target);
        RenderLinesFrame(cam, target, viewProj);

        // 렌더 텍스처의 일부 영역만 ReadPixels로 읽으면 플랫폼(DX11)에 따라 세로 기준이 뒤집힌다.
        // 전체를 한 번 읽어 두고 CPU에서 잘라낸다. (전체 읽기는 WorldToScreenPoint 좌표와 일치함을 확인)
        ReadFull(textureFrame, ref textureFrameCpu);
        ReadFull(linesFrame, ref linesFrameCpu);
        textureFramePixels = textureFrameCpu.GetPixels32();
        linesFramePixels = linesFrameCpu.GetPixels32();

        capturedFrame = Time.frameCount;
        capturedTarget = target;
        capturedViewProj = viewProj;
        capturedCreaseAngle = creaseAngle;
    }

    // center(스크린 픽셀, 좌하단 원점)를 중심으로 size x size를 잘라낸다. 화면 밖은 검정.
    public Texture2D Crop(Mode mode, Vector2 center, int size)
    {
        Texture2D source = mode == Mode.Lines ? linesFrameCpu : textureFrameCpu;
        Color32[] frame = mode == Mode.Lines ? linesFramePixels : textureFramePixels;
        if (source == null || frame == null) return null;

        var patch = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 255);

        int x0 = Mathf.RoundToInt(center.x) - size / 2;
        int y0 = Mathf.RoundToInt(center.y) - size / 2;
        for (int y = 0; y < size; y++)
        {
            int sy = y0 + y;
            if (sy < 0 || sy >= source.height) continue;
            for (int x = 0; x < size; x++)
            {
                int sx = x0 + x;
                if (sx < 0 || sx >= source.width) continue;
                pixels[y * size + x] = frame[sy * source.width + sx];
            }
        }

        if (mode == Mode.Lines && lineDilation > 0) pixels = Dilate(pixels, size, lineDilation);
        // 선 모드: 선만 보이고 배경은 투명 (다른 패치나 모델 투영을 가리지 않게)
        // 텍스처 모드: 불투명 (아래 화면과 섞이지 않게)
        for (int i = 0; i < pixels.Length; i++) pixels[i].a = mode == Mode.Lines ? pixels[i].r : (byte)255;
        patch.SetPixels32(pixels);
        patch.Apply();
        return patch;
    }

    public void Release()
    {
        DestroyTexture(ref textureFrame);
        DestroyTexture(ref linesFrame);
        if (textureFrameCpu != null) Object.Destroy(textureFrameCpu);
        if (linesFrameCpu != null) Object.Destroy(linesFrameCpu);
        if (depthMat != null) Object.Destroy(depthMat); // HideAndDontSave라 직접 지우지 않으면 플레이마다 쌓임
        if (lineMat != null) Object.Destroy(lineMat);
        textureFrameCpu = linesFrameCpu = null;
        textureFramePixels = linesFramePixels = null;
        depthMat = lineMat = null;
        edgeCaches.Clear();
        capturedFrame = -1;
    }

    // Release()는 GPU 메모리만 비우고 RenderTexture 객체는 남기므로 Destroy까지 한다.
    private static void DestroyTexture(ref RenderTexture rt)
    {
        if (rt == null) return;
        rt.Release();
        Object.Destroy(rt);
        rt = null;
    }

    private static void ReadFull(RenderTexture rt, ref Texture2D cpu)
    {
        if (cpu == null || cpu.width != rt.width || cpu.height != rt.height)
        {
            if (cpu != null) Object.Destroy(cpu);
            cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        }
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        cpu.Apply();
        RenderTexture.active = previous;
    }

    // 프로젝터 카메라로 모델 레이어만 그린다. (커스텀 투영 행렬이 있으면 그대로 쓰임)
    private void RenderTextureFrame(Camera cam, GameObject target)
    {
        RenderTexture oldTarget = cam.targetTexture;
        int oldMask = cam.cullingMask;
        cam.targetTexture = textureFrame;
        cam.cullingMask = 1 << target.layer;
        cam.Render();
        cam.targetTexture = oldTarget;
        cam.cullingMask = oldMask;
    }

    // 보이는 실루엣 외곽선과 각진 모서리를 흰 선으로 그린다.
    // 먼저 모델 깊이를 그려 두고 선을 깊이 테스트해서, 가려진 선은 그리지 않는다.
    private void RenderLinesFrame(Camera cam, GameObject target, Matrix4x4 viewProj)
    {
        EnsureMaterials();
        Vector3 camPos = cam.transform.position;
        List<MeshFilter> filters = ModelMeshFilters(target);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = linesFrame;
        GL.Clear(true, true, Color.black);
        GL.PushMatrix();
        GL.LoadIdentity();
        GL.LoadProjectionMatrix(viewProj);

        depthMat.SetPass(0);
        foreach (MeshFilter mf in filters)
            if (mf.sharedMesh != null) Graphics.DrawMeshNow(mf.sharedMesh, mf.transform.localToWorldMatrix);

        lineMat.SetPass(0);
        GL.Begin(GL.LINES);
        GL.Color(Color.white);
        foreach (MeshFilter mf in filters)
        {
            if (mf.sharedMesh == null) continue;
            EdgeCache edges = GetEdges(mf.sharedMesh);
            Transform tr = mf.transform;
            Vector3 camLocal = tr.InverseTransformPoint(camPos);

            for (int e = 0; e < edges.a.Count; e++)
            {
                int f1 = edges.t1[e], f2 = edges.t2[e];
                bool front1 = Vector3.Dot(edges.faceNormals[f1], camLocal - edges.faceCenters[f1]) > 0f;
                bool front2 = f2 >= 0 && Vector3.Dot(edges.faceNormals[f2], camLocal - edges.faceCenters[f2]) > 0f;
                if (!front1 && !front2) continue; // 양쪽 다 뒷면이면 안 보임

                bool silhouette = f2 < 0 || front1 != front2;
                if (!silhouette && !edges.crease[e]) continue;

                GL.Vertex(TowardCamera(tr.TransformPoint(edges.vertices[edges.a[e]]), camPos));
                GL.Vertex(TowardCamera(tr.TransformPoint(edges.vertices[edges.b[e]]), camPos));
            }
        }
        GL.End();

        GL.PopMatrix();
        RenderTexture.active = previous;
    }

    // 모델 자체의 메쉬만 고른다. 모델 아래에는 클릭용 버텍스 구와 추천 마커도 자식으로 붙어 있는데,
    // 이들은 레이어가 달라서(Vertex In 3D / Default) 모델 레이어로 걸러낼 수 있다.
    private static List<MeshFilter> ModelMeshFilters(GameObject target)
    {
        var result = new List<MeshFilter>();
        foreach (MeshFilter mf in target.GetComponentsInChildren<MeshFilter>())
            if (mf.gameObject.layer == target.layer) result.Add(mf);
        return result;
    }

    // 선이 자기 면에 가려지지 않도록 카메라 쪽으로 아주 조금 당긴다 (거리의 0.1%).
    private static Vector3 TowardCamera(Vector3 p, Vector3 camPos)
    {
        return p + (camPos - p) * 0.001f;
    }

    private EdgeCache GetEdges(Mesh mesh)
    {
        if (edgeCaches.TryGetValue(mesh, out EdgeCache cache))
        {
            if (cache.creaseAngle != creaseAngle) UpdateCreases(cache); // 기준 각도만 바뀌었으면 각진 모서리만 다시 판정
            return cache;
        }

        // 모델을 바꿀 때마다 메쉬 인스턴스가 새로 생기므로(SaliencyDataLoader가 meshFilter.mesh 사용)
        // 이미 지워진 메쉬의 항목은 정리한다. 안 그러면 모델을 바꿀 때마다 쌓임.
        var dead = new List<Mesh>();
        foreach (Mesh key in edgeCaches.Keys) if (key == null) dead.Add(key);
        foreach (Mesh key in dead) edgeCaches.Remove(key);

        cache = new EdgeCache();
        Vector3[] verts = mesh.vertices;
        int[] tris = mesh.triangles;
        cache.vertices = verts;

        // UV 이음새 등으로 같은 위치에 버텍스가 여러 개 있으면 가짜 경계선이 생기므로 위치로 합친다.
        var weld = new Dictionary<Vector3, int>();
        int[] id = new int[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            if (!weld.TryGetValue(verts[i], out id[i]))
            {
                id[i] = weld.Count;
                weld.Add(verts[i], id[i]);
            }
        }

        int faceCount = tris.Length / 3;
        cache.faceNormals = new Vector3[faceCount];
        cache.faceCenters = new Vector3[faceCount];
        var edgeIndex = new Dictionary<long, int>();
        for (int f = 0; f < faceCount; f++)
        {
            Vector3 v0 = verts[tris[f * 3]], v1 = verts[tris[f * 3 + 1]], v2 = verts[tris[f * 3 + 2]];
            cache.faceNormals[f] = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            cache.faceCenters[f] = (v0 + v1 + v2) / 3f;

            for (int k = 0; k < 3; k++)
            {
                int i0 = tris[f * 3 + k], i1 = tris[f * 3 + (k + 1) % 3];
                int lo = Mathf.Min(id[i0], id[i1]), hi = Mathf.Max(id[i0], id[i1]);
                if (lo == hi) continue; // 퇴화된 면
                long key = ((long)lo << 32) | (uint)hi;

                if (edgeIndex.TryGetValue(key, out int e))
                {
                    if (cache.t2[e] < 0) cache.t2[e] = f;
                    else cache.multiFace[e] = true; // 면 3개 이상이 공유하는 모서리는 항상 그림
                }
                else
                {
                    edgeIndex.Add(key, cache.a.Count);
                    cache.a.Add(i0); cache.b.Add(i1);
                    cache.t1.Add(f); cache.t2.Add(-1);
                    cache.crease.Add(false);
                    cache.multiFace.Add(false);
                }
            }
        }

        var angles = new List<float>();
        for (int e = 0; e < cache.a.Count; e++)
            if (cache.t2[e] >= 0) angles.Add(Vector3.Angle(cache.faceNormals[cache.t1[e]], cache.faceNormals[cache.t2[e]]));
        angles.Sort();
        cache.medianAngle = angles.Count > 0 ? angles[angles.Count / 2] : 0f;

        UpdateCreases(cache);
        edgeCaches.Add(mesh, cache);
        return cache;
    }

    private void UpdateCreases(EdgeCache cache)
    {
        for (int e = 0; e < cache.a.Count; e++)
        {
            int f2 = cache.t2[e];
            cache.crease[e] = cache.multiFace[e]
                || (f2 >= 0 && Vector3.Angle(cache.faceNormals[cache.t1[e]], cache.faceNormals[f2]) > creaseAngle);
        }
        cache.creaseAngle = creaseAngle;
    }

    private void EnsureMaterials()
    {
        if (depthMat != null && lineMat != null) return;
        Shader shader = Shader.Find("Hidden/Internal-Colored");

        depthMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        depthMat.SetColor("_Color", Color.black);
        depthMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        depthMat.SetInt("_ZWrite", 1);
        depthMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);

        lineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        lineMat.SetColor("_Color", Color.white);
        lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        lineMat.SetInt("_ZWrite", 0);
        lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
    }

    private static void EnsureTexture(ref RenderTexture rt, int w, int h, string name)
    {
        if (rt != null && rt.width == w && rt.height == h) return;
        DestroyTexture(ref rt);
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = name };
    }

    // 1픽셀 선이 프로젝터에서 너무 가늘게 보이지 않도록 밝은 픽셀을 주변으로 넓힌다.
    private static Color32[] Dilate(Color32[] src, int size, int radius)
    {
        var dst = new Color32[src.Length];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                byte best = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= size) continue;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= size) continue;
                        byte v = src[yy * size + xx].r;
                        if (v > best) best = v;
                    }
                }
                dst[y * size + x] = new Color32(best, best, best, 255);
            }
        }
        return dst;
    }
}
