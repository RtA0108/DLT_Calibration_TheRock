using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;
using System.Globalization;

public static class SaliencyDataLoader
{
    private const float MATCH_THRESHOLD = 0.001f;

    private static Dictionary<Vector3, float> _fullLocalSaliencyMap;
    private static Dictionary<Vector3, Vector3> _unityToObjCoordMap;
    private static PointGrid _unityKeyGrid;
    private static string _loadedObjPath;
    private static string _loadedTxtPath;

    // "가장 가까운 점이 MATCH_THRESHOLD 이내인가"를 빠르게 찾기 위한 격자.
    // 칸 크기를 MATCH_THRESHOLD로 두면, 그 거리 안의 점은 반드시 주변 27칸 안에 있다.
    // (예전에는 모든 점과 하나씩 거리를 비교해서 Chick 기준 조회 한 번에 수억 번 계산했음)
    private class PointGrid
    {
        private readonly Dictionary<(int, int, int), List<Vector3>> cells = new Dictionary<(int, int, int), List<Vector3>>();

        public PointGrid(IEnumerable<Vector3> points)
        {
            foreach (Vector3 p in points)
            {
                var key = Cell(p);
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<Vector3>();
                list.Add(p);
            }
        }

        // MATCH_THRESHOLD 이내에서 가장 가까운 점. 없으면 false.
        public bool TryFindNearest(Vector3 p, out Vector3 nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            var (cx, cy, cz) = Cell(p);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var list)) continue;
                        foreach (Vector3 q in list)
                        {
                            float d = Vector3.Distance(p, q);
                            if (d < best) { best = d; nearest = q; }
                        }
                    }
            return best < MATCH_THRESHOLD;
        }

        private static (int, int, int) Cell(Vector3 p)
        {
            return (Mathf.FloorToInt(p.x / MATCH_THRESHOLD), Mathf.FloorToInt(p.y / MATCH_THRESHOLD), Mathf.FloorToInt(p.z / MATCH_THRESHOLD));
        }
    }

    // 캐시는 경로로만 구분하므로, 같은 경로의 파일을 다시 계산했으면 이걸 불러 새로 읽게 한다.
    public static void ClearCache()
    {
        _fullLocalSaliencyMap = null;
        _unityToObjCoordMap = null;
        _unityKeyGrid = null;
        _loadedObjPath = null;
        _loadedTxtPath = null;
    }

    public static Dictionary<Vector3, float> GetSaliencyMap(MeshFilter meshFilter, List<Vector3> filteredWorldVertices, string objPath, string txtPath)
    {
        // 캐싱 로직
        if (_fullLocalSaliencyMap == null || _loadedObjPath != objPath || _loadedTxtPath != txtPath)
        {
            _fullLocalSaliencyMap = LoadFullSaliencyMapFromSource(objPath, txtPath);
            if (_fullLocalSaliencyMap.Count == 0) return new Dictionary<Vector3, float>();

            BuildUnityToObjMap(meshFilter);
            _unityKeyGrid = new PointGrid(_unityToObjCoordMap.Keys);
            _loadedObjPath = objPath;
            _loadedTxtPath = txtPath;
        }

        var filteredSaliencyMap = new Dictionary<Vector3, float>();
        var transform = meshFilter.transform;

        if (_unityToObjCoordMap == null || _unityToObjCoordMap.Count == 0) return filteredSaliencyMap;

        foreach (Vector3 worldPos in filteredWorldVertices)
        {
            Vector3 localPos = transform.InverseTransformPoint(worldPos);

            // 가장 가까운 점 찾기
            if (_unityKeyGrid.TryFindNearest(localPos, out Vector3 closestKey))
            {
                Vector3 objCoordKey = _unityToObjCoordMap[closestKey];
                if (_fullLocalSaliencyMap.ContainsKey(objCoordKey))
                {
                    filteredSaliencyMap[worldPos] = _fullLocalSaliencyMap[objCoordKey];
                }
            }
        }
        return filteredSaliencyMap;
    }

    private static Dictionary<Vector3, float> LoadFullSaliencyMapFromSource(string objPath, string txtPath)
    {
        var map = new Dictionary<Vector3, float>();

        // ObjMeshImporter가 X축 반전 없이 읽어옴
        List<Vector3> vertices = ObjMeshImporter.LoadVertices(objPath);
        List<float> scores = LoadScoresRobust(txtPath);

        if (vertices.Count == 0 || scores.Count == 0)
        {
            Debug.LogError($"[Data Error] 데이터 로드 실패. OBJ: {vertices.Count}, TXT: {scores.Count}");
            return map;
        }

        int count = Mathf.Min(vertices.Count, scores.Count);
        for (int i = 0; i < count; i++)
        {
            if (!map.ContainsKey(vertices[i])) map.Add(vertices[i], scores[i]);
        }
        return map;
    }

    private static List<float> LoadScoresRobust(string path)
    {
        List<float> scores = new List<float>();

        // ★ [CCTV 1] 경로가 아예 텅 비어서 들어왔는지 확인
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("[SaliencyDataLoader] 전달받은 TXT 경로가 텅 비어있습니다! (파이썬이 파일을 생성하지 않았을 확률 99%)");
            return scores;
        }

        Debug.LogWarning($"[SaliencyDataLoader] 읽기 시도 경로: {path}");

        string[] lines = null;
        string diskPath = path.EndsWith(".txt") ? path : path + ".txt";

        if (File.Exists(diskPath))
        {
            Debug.Log($"[SaliencyDataLoader] 로컬 파일 발견: {diskPath}");
            lines = File.ReadAllLines(diskPath);
        }
        else
        {
            Debug.LogWarning($"[SaliencyDataLoader] 로컬 파일 없음, Resources에서 찾기 시도: {diskPath}");
            string resPath = RemoveExtensionAndPrefix(path);
            TextAsset asset = Resources.Load<TextAsset>(resPath);
            if (asset != null) lines = asset.text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        }

        if (lines == null)
        {
            Debug.LogError("[SaliencyDataLoader] 파일을 최종적으로 찾지 못했습니다.");
            return scores;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (i == 0 && line.Length > 0 && line[0] == '\uFEFF') line = line.Substring(1);

            if (float.TryParse(line, NumberStyles.Any, CultureInfo.InvariantCulture, out float val)) scores.Add(val);
        }
        return scores;
    }

    private static string RemoveExtensionAndPrefix(string path)
    {
        if (Path.HasExtension(path)) path = Path.ChangeExtension(path, null);
        string prefix = "Assets/Resources/";
        if (path.IndexOf(prefix, System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            path = path.Substring(path.IndexOf(prefix, System.StringComparison.OrdinalIgnoreCase) + prefix.Length);
        }
        return path.Replace("\\", "/");
    }

    private static void BuildUnityToObjMap(MeshFilter meshFilter)
    {
        _unityToObjCoordMap = new Dictionary<Vector3, Vector3>();
        Vector3[] unityVertices = meshFilter.mesh.vertices;

        if (_fullLocalSaliencyMap.Count == 0) return;
        var objGrid = new PointGrid(_fullLocalSaliencyMap.Keys);

        // 매칭 로직: 유니티 버텍스마다 MATCH_THRESHOLD 이내의 가장 가까운 OBJ 버텍스
        int matched = 0;
        foreach (var unityVert in unityVertices)
        {
            if (objGrid.TryFindNearest(unityVert, out Vector3 closest))
            {
                matched++;
                if (!_unityToObjCoordMap.ContainsKey(unityVert))
                    _unityToObjCoordMap.Add(unityVert, closest);
            }
        }

        // 비율은 버텍스 수 기준 (각진 메쉬는 한 위치에 버텍스가 여러 개라, 위치 수로 나누면 낮게 보였음)
        float rate = (float)matched / unityVertices.Length * 100f;
        string color = rate > 90f ? "lime" : "red";
        Debug.Log($"<b><color={color}>[SaliencyDataLoader] 맵핑 완료: 버텍스 {unityVertices.Length}개 중 {matched}개 연결됨 ({rate:F1}%, 위치 {_unityToObjCoordMap.Count}곳)</color></b>");
    }
}

//using System.Collections.Generic;
//using System.IO;
//using UnityEngine;
//using System.Linq;
//using System.Globalization;

///// <summary>
///// CfS-CNN의 Saliency 데이터를 로드하고 Unity 메쉬에 매칭시키는 모든 로직을 담당합니다.
///// </summary>
//public static class SaliencyDataLoader
//{
//    // 정밀도 문제 해결을 위한 매칭 허용 오차.
//    // DebugMatcher에서 100% 성공했던 값으로 설정해주세요. (예: 0.01f)
//    private const float MATCH_THRESHOLD = 0.001f;

//    // 한 번 로드한 데이터는 다시 읽지 않도록 캐싱합니다.
//    private static Dictionary<Vector3, float> _fullLocalSaliencyMap;
//    private static Dictionary<Vector3, Vector3> _unityToObjCoordMap; // Key: Unity local, Value: OBJ local
//    private static string _loadedObjPath;

//    /// <summary>
//    /// 최종적으로 사용할 Saliency Map을 반환합니다.
//    /// </summary>
//    public static Dictionary<Vector3, float> GetSaliencyMap(MeshFilter meshFilter, List<Vector3> filteredWorldVertices, string objPath, string txtPath)
//    {
//        // 데이터가 로드되지 않았거나 다른 파일이면 새로 로드
//        if (_fullLocalSaliencyMap == null || _loadedObjPath != objPath)
//        {
//            _fullLocalSaliencyMap = LoadFullSaliencyMapFromSource(objPath, txtPath);
//            if (_fullLocalSaliencyMap.Count == 0) return new Dictionary<Vector3, float>();

//            BuildUnityToObjMap(meshFilter);
//            _loadedObjPath = objPath;
//        }

//        var filteredSaliencyMap = new Dictionary<Vector3, float>();
//        var transform = meshFilter.transform;
//        List<Vector3> unityMapKeys = _unityToObjCoordMap.Keys.ToList();
//        if (unityMapKeys.Count == 0)
//        {
//            Debug.LogError("[SaliencyDataLoader] '바로가기 맵'이 비어있습니다. BuildUnityToObjMap 실패.");
//            return filteredSaliencyMap;
//        }
//        int successCount = 0;
//        foreach (Vector3 worldPos in filteredWorldVertices)
//        {

//            Vector3 localPos = transform.InverseTransformPoint(worldPos);

//            Vector3 closestKey = unityMapKeys[0];
//            float minDistance = Vector3.Distance(localPos, closestKey);

//            for (int i = 1; i < unityMapKeys.Count; i++)
//            {
//                float distance = Vector3.Distance(localPos, unityMapKeys[i]);
//                if (distance < minDistance)
//                {
//                    minDistance = distance;
//                    closestKey = unityMapKeys[i];
//                }
//            }

//            // 가장 가까운 키와의 거리가 허용 오차 내에 있다면 성공으로 간주
//            if (minDistance < MATCH_THRESHOLD)
//            {
//                Vector3 objCoordKey = _unityToObjCoordMap[closestKey];
//                filteredSaliencyMap[worldPos] = _fullLocalSaliencyMap[objCoordKey];
//                successCount++;
//            }
//            else
//            {
//                Debug.LogError($"[SaliencyDataLoader] 조회 실패! 가장 가까운 키({closestKey:F8})와의 거리가 너무 멉니다. Distance: {minDistance:F8}, Key: {localPos:F8}");
//            }
//        }
//        Debug.Log($"<b><color=lime>[SaliencyDataLoader] 조회 완료. 총 {filteredWorldVertices.Count}개의 후보 중 {successCount}개가 성공적으로 매칭되었습니다.</color></b>");
//        return filteredSaliencyMap;
//    }


//    // LoadFullSaliencyMapFromSource와 BuildUnityToObjMap 함수는 이전 '전체 코드' 답변과 동일합니다.
//    private static Dictionary<Vector3, float> LoadFullSaliencyMapFromSource(string objPath, string saliencyTxtPath)
//    {
//        var saliencyMap = new Dictionary<Vector3, float>();

//        if (!File.Exists(objPath) || !File.Exists(saliencyTxtPath))
//        {
//            Debug.LogError($"소스 파일을 찾을 수 없습니다: OBJ({File.Exists(objPath)}), TXT({File.Exists(saliencyTxtPath)})");
//            return saliencyMap;
//        }

//        List<Vector3> localVertices = ObjMeshImporter.LoadVertices(objPath);
//        // --- 수정된 부분 시작 ---
//        List<float> scores = new List<float>();
//        string[] lines = File.ReadAllLines(saliencyTxtPath);

//        for (int i = 0; i < lines.Length; i++)
//        {
//            // 1. 앞뒤 공백 제거
//            string line = lines[i].Trim();

//            // 2. 빈 줄 건너뛰기
//            if (string.IsNullOrWhiteSpace(line)) continue;

//            // 3. BOM(Byte Order Mark) 제거 (파일 첫 줄에 숨어있는 경우가 많음)
//            if (i == 0 && line.Length > 0 && line[0] == '\uFEFF')
//            {
//                line = line.Substring(1);
//            }

//            try
//            {
//                // 4. NumberStyles.Any를 사용하여 지수 표기법(E-05)이나 섞인 공백까지 허용
//                float val = float.Parse(line, NumberStyles.Any, CultureInfo.InvariantCulture);
//                scores.Add(val);
//            }
//            catch (System.FormatException)
//            {
//                // 5. 정확히 어떤 문자열 때문에 죽었는지 로그 출력 (디버깅용 핵심)
//                Debug.LogError($"[SaliencyDataLoader] 포맷 에러 발생! Line {i + 1}: '{line}' (원본: '{lines[i]}')");
//                // 문제가 된 줄은 0으로 처리하거나, throw를 해서 멈출지 결정 (여기선 0으로 넣고 진행)
//                // scores.Add(0f); 
//                throw; // 에러를 확실히 잡기 위해 일단 멈춥니다.
//            }
//        }
//        // --- 수정된 부분 끝 ---
//        //float[] scores = File.ReadAllLines(saliencyTxtPath)
//        //                       .Where(line => !string.IsNullOrWhiteSpace(line))
//        //                       .Select(s => float.Parse(s, CultureInfo.InvariantCulture))
//        //                       .ToArray();

//        if (localVertices.Count != scores.Count || localVertices.Count == 0)
//        {
//            Debug.LogError($"정점({localVertices.Count})과 점수({scores.Count}) 개수가 다르거나, 데이터가 없습니다.");
//            return saliencyMap;
//        }

//        for (int i = 0; i < localVertices.Count; i++)
//        {
//            if (!saliencyMap.ContainsKey(localVertices[i]))
//            {
//                saliencyMap.Add(localVertices[i], scores[i]);
//            }
//        }
//        return saliencyMap;
//    }

//    private static void BuildUnityToObjMap(MeshFilter meshFilter)
//    {
//        _unityToObjCoordMap = new Dictionary<Vector3, Vector3>();
//        Vector3[] unityVertices = meshFilter.mesh.vertices;
//        List<Vector3> objVertices = _fullLocalSaliencyMap.Keys.ToList();

//        foreach (var unityVert in unityVertices)
//        {
//            if (objVertices.Count == 0) break;

//            Vector3 closestObjVert = objVertices[0];
//            float minDistance = Vector3.Distance(unityVert, closestObjVert);

//            for (int i = 1; i < objVertices.Count; i++)
//            {
//                float distance = Vector3.Distance(unityVert, objVertices[i]);
//                if (distance < minDistance)
//                {
//                    minDistance = distance;
//                    closestObjVert = objVertices[i];
//                }
//            }

//            if (minDistance < MATCH_THRESHOLD)
//            {
//                if (!_unityToObjCoordMap.ContainsKey(unityVert))
//                {
//                    _unityToObjCoordMap.Add(unityVert, closestObjVert);
//                }
//            }
//        }
//        Debug.Log($"[SaliencyDataLoader] '바로가기 맵' 생성 완료. 총 {meshFilter.mesh.vertexCount}개의 정점 중 {_unityToObjCoordMap.Count}개가 연결되었습니다.");
//    }
//}