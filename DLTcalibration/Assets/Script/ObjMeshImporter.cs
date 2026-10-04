using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine;

public static class ObjMeshImporter
{
    public static List<Vector3> LoadVertices(string path)
    {
        List<Vector3> vertices = new List<Vector3>();
        string[] lines = null;

        Debug.Log($"[ObjMeshImporter] 경로 검색 시작: '{path}'");

        // ---------------------------------------------------------
        // 1. 외부 파일(Disk) 시도 (절대 경로)
        // ---------------------------------------------------------
        string diskPath = path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase) ? path : path + ".obj";
        if (File.Exists(diskPath))
        {
            lines = File.ReadAllLines(diskPath);
            Debug.Log($"[ObjMeshImporter] 외부 파일 찾음: {diskPath}");
        }
        // ---------------------------------------------------------
        // 2. 내부 리소스(Resources) 시도
        // ---------------------------------------------------------
        else
        {
            // Resources.Load는 확장자와 "Assets/Resources/"를 싫어합니다. 다 떼버립니다.
            string cleanPath = CleanPathForResources(path);
            TextAsset textAsset = Resources.Load<TextAsset>(cleanPath);

            if (textAsset != null)
            {
                lines = textAsset.text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                Debug.Log($"[ObjMeshImporter] 리소스 찾음: {cleanPath}");
            }
            else
            {
                // 혹시 모르니 경로를 조금 더 다듬어서 한 번 더 시도
                if (cleanPath.Contains("Resources/"))
                {
                    string fallback = cleanPath.Substring(cleanPath.LastIndexOf("Resources/") + 10);
                    textAsset = Resources.Load<TextAsset>(fallback);
                    if (textAsset != null)
                    {
                        lines = textAsset.text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                        Debug.Log($"[ObjMeshImporter] 리소스(Fallback) 찾음: {fallback}");
                    }
                }
            }
        }

        if (lines == null)
        {
            Debug.LogError($"[ObjMeshImporter] 실패! 파일을 찾을 수 없습니다. 경로: {path}");
            return vertices;
        }

        // ---------------------------------------------------------
        // 3. 파싱 (v x y z) -> Unity 좌표계 (-x) 적용
        // ---------------------------------------------------------
        foreach (string line in lines)
        {
            if (line.StartsWith("v "))
            {
                string[] parts = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        // ★ Unity 표준 (-x) 적용
                        vertices.Add(new Vector3(-x, y, z));
                    }
                }
            }
        }

        return vertices;
    }

    // 어떤 경로가 들어와도 Resources.Load가 좋아하는 형태로 깎아주는 함수
    private static string CleanPathForResources(string path)
    {
        // 1. 역슬래시 통일
        path = path.Replace("\\", "/");

        // 2. 확장자 제거
        if (Path.HasExtension(path))
        {
            path = Path.ChangeExtension(path, null);
        }

        // 3. "Assets/Resources/" 제거
        string prefix = "Assets/Resources/";
        int index = path.IndexOf(prefix, System.StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            path = path.Substring(index + prefix.Length);
        }

        return path;
    }
}