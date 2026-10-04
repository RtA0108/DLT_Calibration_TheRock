using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Globalization;

public static class SimpleObjLoader
{
    public static GameObject Load(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        // --- 1. OBJ 파싱 (기존 로직 유지) ---
        List<Vector3> rawVertices = new List<Vector3>();
        List<Vector2> rawUVs = new List<Vector2>();
        List<Vector3> rawNormals = new List<Vector3>(); // 읽지만 무시 (Recalculate 사용)

        List<Vector3> newVertices = new List<Vector3>();
        List<Vector2> newUVs = new List<Vector2>();
        List<int> newTriangles = new List<int>();

        Dictionary<string, int> history = new Dictionary<string, int>();
        string[] lines = File.ReadAllLines(filePath);

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            string[] parts = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v": // 좌표 통일 (-x)
                    rawVertices.Add(new Vector3(-ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    break;
                case "vt":
                    rawUVs.Add(new Vector2(ParseFloat(parts[1]), ParseFloat(parts[2])));
                    break;
                case "vn":
                    rawNormals.Add(new Vector3(-ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    break;
                case "f": // 면 뒤집기 (0-2-1)
                    for (int i = 1; i < parts.Length - 2; i++)
                    {
                        ProcessIndex(parts[1], rawVertices, rawUVs, rawNormals, newVertices, newUVs, newTriangles, history);
                        ProcessIndex(parts[i + 2], rawVertices, rawUVs, rawNormals, newVertices, newUVs, newTriangles, history);
                        ProcessIndex(parts[i + 1], rawVertices, rawUVs, rawNormals, newVertices, newUVs, newTriangles, history);
                    }
                    break;
            }
        }

        // --- 2. 메쉬 생성 ---
        Mesh mesh = new Mesh();
        mesh.name = Path.GetFileNameWithoutExtension(filePath);
        if (newVertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = newVertices.ToArray();
        mesh.uv = newUVs.ToArray();
        mesh.triangles = newTriangles.ToArray();

        // 조명 보정 (필수)
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        // --- 3. 오브젝트 생성 ---
        GameObject root = new GameObject(Path.GetFileNameWithoutExtension(filePath));
        GameObject model = new GameObject("Model");
        model.transform.SetParent(root.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        model.AddComponent<MeshFilter>().mesh = mesh;
        MeshRenderer renderer = model.AddComponent<MeshRenderer>();

        // --- 4. 쉐이더 및 텍스처 로드 (★여기가 핵심★) ---

        // (A) 쉐이더 찾기 (보라색 방지)
        // URP Lit -> URP Simple -> Standard -> Legacy Diffuse 순으로 찾습니다.
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (!shader) shader = Shader.Find("Standard");
        if (!shader) shader = Shader.Find("Mobile/Diffuse"); // 최후의 수단

        if (shader)
        {
            Material mat = new Material(shader);

            // (B) 텍스처 자동 로드
            // OBJ 파일과 같은 폴더, 같은 이름의 png/jpg를 찾습니다.
            Texture2D texture = LoadTextureForMesh(filePath);
            if (texture != null)
            {
                // URP는 "_BaseMap", 일반은 "_MainTex"를 씁니다. 둘 다 넣어줍니다.
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
                Debug.Log($"[SimpleObjLoader] 텍스처 로드 성공: {texture.name}");
            }
            else
            {
                Debug.LogWarning($"[SimpleObjLoader] 텍스처를 찾을 수 없습니다. (흰색 재질 사용)");
            }

            renderer.material = mat;
        }
        else
        {
            Debug.LogError("[SimpleObjLoader] 치명적 오류: 사용할 수 있는 쉐이더가 없습니다!");
        }

        return root;
    }

    // 텍스처 로드 헬퍼 함수
    private static Texture2D LoadTextureForMesh(string objPath)
    {
        string dir = Path.GetDirectoryName(objPath);
        string name = Path.GetFileNameWithoutExtension(objPath);

        // 1. PNG 시도
        string texPath = Path.Combine(dir, name + ".png");
        if (!File.Exists(texPath))
        {
            // 2. JPG 시도
            texPath = Path.Combine(dir, name + ".jpg");
            if (!File.Exists(texPath))
            {
                // 3. JPEG 시도
                texPath = Path.Combine(dir, name + ".jpeg");
                if (!File.Exists(texPath)) return null;
            }
        }

        // 파일 읽어서 텍스처로 변환
        byte[] fileData = File.ReadAllBytes(texPath);
        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(fileData); // 크기는 자동 조절됨
        tex.name = Path.GetFileName(texPath);

        return tex;
    }

    static void ProcessIndex(string part, List<Vector3> rawV, List<Vector2> rawUV, List<Vector3> rawN, List<Vector3> newV, List<Vector2> newUV, List<int> newTri, Dictionary<string, int> history)
    {
        if (history.ContainsKey(part))
        {
            newTri.Add(history[part]);
            return;
        }
        string[] data = part.Split('/');
        int vIdx = int.Parse(data[0]);
        if (vIdx < 0) vIdx += rawV.Count + 1;
        newV.Add(rawV[vIdx - 1]);

        if (data.Length > 1 && !string.IsNullOrEmpty(data[1]))
        {
            int uvIdx = int.Parse(data[1]);
            if (uvIdx < 0) uvIdx += rawUV.Count + 1;
            newUV.Add(rawUV[uvIdx - 1]);
        }
        else newUV.Add(Vector2.zero);

        int newIndex = newV.Count - 1;
        history.Add(part, newIndex);
        newTri.Add(newIndex);
    }

    static float ParseFloat(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}