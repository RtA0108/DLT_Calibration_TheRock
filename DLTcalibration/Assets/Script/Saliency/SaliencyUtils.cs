using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class SaliencyUtils
{
    //Occlusion Culling
    public static List<Vector3> GetVisibleVertices(
    Camera camera, MeshFilter meshFilter, Dictionary<int, Vector3> vertexPositions, LayerMask visibilityLayerMask)
    {
        List<Vector3> visibleVertices = new List<Vector3>();
        Vector3 camPos = camera.transform.position;
        Dictionary<Vector3, bool> uniquePositions = new Dictionary<Vector3, bool>();

        float dotThreshold = 0.5f; // ← 더 정면을 향한 정점만 raycast 시도
        float hitTolerance = 0.005f; // 거리 기반 (너무 뒤에 있어서 잘 안보이는 vertex 제외)

        // normals 프로퍼티는 접근할 때마다 배열 전체를 복사하므로 한 번만 읽는다.
        // (vertexPositions의 인덱스는 sharedMesh.vertices 기준)
        Vector3[] normals = meshFilter.sharedMesh.normals;

        foreach (var kvp in vertexPositions)
        {
            int vertexIndex = kvp.Key;
            Vector3 vertexWorldPos = meshFilter.transform.TransformPoint(kvp.Value);

            // 중복 제거용 좌표 정규화
            Vector3 roundedVertex = new Vector3(
                Mathf.Round(vertexWorldPos.x * 1000f) / 1000f,
                Mathf.Round(vertexWorldPos.y * 1000f) / 1000f,
                Mathf.Round(vertexWorldPos.z * 1000f) / 1000f
            );

            Vector3 worldNormal = meshFilter.transform.TransformDirection(normals[vertexIndex]);
            Vector3 toCamera = (camPos - vertexWorldPos).normalized;

            if (Vector3.Dot(worldNormal, toCamera) <= dotThreshold) continue;

            Ray ray = new Ray(camPos, (vertexWorldPos - camPos).normalized);
            //Mathf.Infinity를 고정된 수치로 변경할지 고민
            float rayDistance = Vector3.Distance(camPos, vertexWorldPos) * 1.1f;
            if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, visibilityLayerMask))
                //if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, visibilityLayerMask))
            {
                 if (Vector3.Distance(hit.point, vertexWorldPos) < hitTolerance)
                 {
                    if (!uniquePositions.ContainsKey(roundedVertex))
                    {
                        uniquePositions[roundedVertex] = true;
                        visibleVertices.Add(vertexWorldPos);
                    }
                 }
            }
        }

        return visibleVertices;
    }

    //Silhouette 기반 필터 진행중
    public static List<Vector3> FilterVerticesByEdge(RenderTexture edgeMask, List<Vector3> vertices, Camera camera, float distanceThresholdPixels = 5.0f)
    {
        List<Vector3> filtered = new List<Vector3>();

        // edgeMask 읽기
        Texture2D edgeTex = new Texture2D(edgeMask.width, edgeMask.height, TextureFormat.RGB24, false);
        RenderTexture.active = edgeMask;
        edgeTex.ReadPixels(new Rect(0, 0, edgeMask.width, edgeMask.height), 0, 0);
        edgeTex.Apply();
        RenderTexture.active = null;

        int w = edgeMask.width;
        int h = edgeMask.height;

        foreach (var vertex in vertices)
        {
            Vector3 screenPos = camera.WorldToScreenPoint(vertex);
            if (screenPos.z < 0) continue; // 카메라 뒤

            int x = Mathf.RoundToInt(screenPos.x);
            int y = Mathf.RoundToInt(screenPos.y);

            bool isNearEdge = false;
            for (int dx = -Mathf.CeilToInt(distanceThresholdPixels); dx <= Mathf.CeilToInt(distanceThresholdPixels); dx++)
            {
                for (int dy = -Mathf.CeilToInt(distanceThresholdPixels); dy <= Mathf.CeilToInt(distanceThresholdPixels); dy++)
                {
                    int px = x + dx;
                    int py = y + dy;
                    if (px >= 0 && px < w && py >= 0 && py < h)
                    {
                        Color c = edgeTex.GetPixel(px, py);
                        if (c.r > 0.5f || c.g > 0.5f || c.b > 0.5f)
                        {
                            isNearEdge = true;
                            break;
                        }
                    }
                }
                if (isNearEdge) break;
            }
            if (!isNearEdge)
            {
                filtered.Add(vertex);
            //    Debug.Log($"[EdgeFilter] PASS: vertex screen ({x}, {y}) → retained");
            }
            //else
            //{
            //    Debug.Log($"[EdgeFilter] REMOVED: vertex screen ({x}, {y}) is near edge");
            //}
        }

        // 호출마다 새로 만들므로 정리
        if (Application.isPlaying) Object.Destroy(edgeTex); else Object.DestroyImmediate(edgeTex);
        return filtered;
    }

    // 안쪽 가림 경계 필터
    // 카메라에서 본 모델 깊이를 그려서, 이웃 픽셀끼리 깊이가 크게 끊기는 곳(앞 부분이 뒷 부분을 가리는 경계)을 찾고
    // 그 근처(±radius px)의 버텍스를 뺀다. 투영이 조금만 어긋나도 빛이 앞 부분 가장자리에 걸리는 점들이다.
    // 바깥 윤곽선(모델-배경 경계)은 실루엣 필터가 담당하므로 여기서는 모델 픽셀끼리만 비교한다.
    private static Material _linearDepthMat;

    public static List<Vector3> FilterVerticesByOcclusionEdges(Camera camera, MeshFilter meshFilter, List<Vector3> vertices, float depthRatio, float radiusPixels)
    {
        bool[] edge = ComputeOcclusionEdgeMask(camera, meshFilter, depthRatio, out int w, out int h);
        if (edge == null) return vertices;

        int r = Mathf.CeilToInt(radiusPixels);
        var result = new List<Vector3>();
        foreach (var v in vertices)
        {
            Vector3 s = camera.WorldToScreenPoint(v);
            if (s.z < 0) continue;
            int x = Mathf.RoundToInt(s.x), y = Mathf.RoundToInt(s.y);

            bool near = false;
            for (int dy = -r; dy <= r && !near; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px >= 0 && px < w && py >= 0 && py < h && edge[py * w + px]) { near = true; break; }
                }
            }
            if (!near) result.Add(v);
        }
        return result;
    }

    // 카메라 픽셀 해상도의 가림 경계 마스크 (true = 경계 픽셀)
    public static bool[] ComputeOcclusionEdgeMask(Camera camera, MeshFilter meshFilter, float depthRatio, out int w, out int h)
    {
        w = camera.pixelWidth;
        h = camera.pixelHeight;

        if (_linearDepthMat == null)
        {
            Shader shader = Shader.Find("Hidden/LinearDepthColor");
            if (shader == null)
            {
                Debug.LogError("[OcclusionEdge] Hidden/LinearDepthColor 셰이더를 찾을 수 없습니다.");
                return null;
            }
            _linearDepthMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        bool useFloat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat);
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, useFloat ? RenderTextureFormat.RFloat : RenderTextureFormat.RHalf);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.clear); // 배경 깊이 = 0
        GL.PushMatrix();
        GL.LoadIdentity();
        GL.LoadProjectionMatrix(camera.projectionMatrix * camera.worldToCameraMatrix);
        _linearDepthMat.SetPass(0);
        Graphics.DrawMeshNow(meshFilter.sharedMesh, meshFilter.transform.localToWorldMatrix);
        GL.PopMatrix();

        // 전체를 읽는다 (부분 읽기는 DX11에서 세로가 뒤집힘)
        var tex = new Texture2D(w, h, useFloat ? TextureFormat.RFloat : TextureFormat.RHalf, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        Color[] pixels = tex.GetPixels();
        if (Application.isPlaying) Object.Destroy(tex); else Object.DestroyImmediate(tex);

        var edge = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float d = pixels[i].r;
                if (d <= 0f) continue;

                // 오른쪽, 위 이웃과 비교 (두 픽셀 모두 모델일 때만)
                if (x + 1 < w) MarkIfJump(edge, pixels, i, i + 1, d, depthRatio);
                if (y + 1 < h) MarkIfJump(edge, pixels, i, i + w, d, depthRatio);
            }
        }
        return edge;
    }

    private static void MarkIfJump(bool[] edge, Color[] pixels, int i, int j, float d, float depthRatio)
    {
        float n = pixels[j].r;
        if (n <= 0f) return;
        if (Mathf.Abs(d - n) > depthRatio * Mathf.Min(d, n)) edge[i] = edge[j] = true;
    }

    // triangle normal filter

    public static List<Vector3> FilterVerticesByTriangleNormals(List<Vector3> filtered, MeshFilter meshFilter, Camera camera, float dotThreshold = 0.2f)
    {
        Dictionary<Vector3, Vector3> normalMap = new();
        HashSet<Vector3> filteredSet = new(filtered);
        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] verts = mesh.vertices;
        int[] tris = mesh.triangles;
        Vector3 camPos = camera.transform.position;

        // triangle normal 평균 계산
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 v0 = meshFilter.transform.TransformPoint(verts[tris[i]]);
            Vector3 v1 = meshFilter.transform.TransformPoint(verts[tris[i + 1]]);
            Vector3 v2 = meshFilter.transform.TransformPoint(verts[tris[i + 2]]);

            if (filteredSet.Contains(v0) && filteredSet.Contains(v1) && filteredSet.Contains(v2))
            {
                Vector3 triNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                if (!normalMap.ContainsKey(v0)) normalMap[v0] = Vector3.zero;
                if (!normalMap.ContainsKey(v1)) normalMap[v1] = Vector3.zero;
                if (!normalMap.ContainsKey(v2)) normalMap[v2] = Vector3.zero;
                normalMap[v0] += triNormal;
                normalMap[v1] += triNormal;
                normalMap[v2] += triNormal;
            }
        }

        List<Vector3> final = new();
        foreach (var v in filtered)
        {
            if (normalMap.TryGetValue(v, out var normal))
            {
                Vector3 viewDir = (camPos - v).normalized;
                if (Vector3.Dot(normal.normalized, viewDir) >= dotThreshold)
                    final.Add(v);
            }
        }
        return final;
    }



    // 분산 + saliency score 비율 변경하며 vertex 추천
    public static List<Vector3> SelectHybridDistributedVertices(List<Vector3> candidates, Dictionary<Vector3, float> saliencyMap, float l, int selectionCount, float alpha = 0.5f)
    {
        List<Vector3> selected = new List<Vector3>();

        if (candidates.Count <= selectionCount)
            return new List<Vector3>(candidates);

        // --- Step 1: 가장 높은 엔트로피를 가진 정점으로 첫 정점 선택 ---
        Vector3 first = candidates.OrderByDescending(v => saliencyMap[v]).First();
        selected.Add(first);
        candidates.Remove(first);

        // --- Step 2: Hybrid 분산 선택 ---
        while (selected.Count < selectionCount)
        {
            Vector3 bestVertex = Vector3.zero;
            float bestScore = float.MinValue;

            foreach (var candidate in candidates)
            {
                float minDist = selected.Min(s => Vector3.Distance(candidate, s));
                // 하이브리드 점수 (alpha 조절) -> alpha가 높을수록 saliency에 대한 영향 증가
                float score = alpha * saliencyMap[candidate] + (1 - alpha) * (minDist / l);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestVertex = candidate;
                }
            }

            if (bestVertex != Vector3.zero)
            {
                selected.Add(bestVertex);
                candidates.Remove(bestVertex);
            }
            else break;
        }

        return selected;
    }
    //-------------새로운 추천 알고리즘들-----------------------------

    public static List<Vector3> SelectVerticesWithAdaptiveNMS(List<Vector3> candidates, Dictionary<Vector3, float> saliencyMap, float objectSizeL, int targetCount)
    {
        // 1. 초기 설정
        // 물체 크기의 20% 거리부터 시작 (너무 좁으면 0.1f 등으로 수정 가능)
        float currentRadiusRatio = 0.2f;

        // 무한 루프 방지용 최대 시도 횟수
        int maxRetries = 20;

        // 원본 데이터를 Saliency 높은 순으로 미리 정렬 (매번 정렬하지 않도록 최적화)
        var sortedCandidates = candidates.OrderByDescending(v => saliencyMap[v]).ToList();

        for (int i = 0; i < maxRetries; i++)
        {
            float currentRadius = objectSizeL * currentRadiusRatio;

            // NMS 실행
            List<Vector3> result = RunNMS(sortedCandidates, currentRadius, targetCount);

            // 성공 조건: 목표 개수를 채웠으면 반환
            if (result.Count >= targetCount)
            {
                if (i > 0)
                    Debug.Log($"[Saliency] {i}번 재시도 끝에 반경 {currentRadiusRatio:F3}L 에서 {targetCount}개 선택 성공.");

                return result;
            }

            // 실패 시: 반경을 80% 수준으로 줄여서 다시 시도
            currentRadiusRatio *= 0.8f;
        }

        // 2. [최후의 안전장치] 
        // 아무리 반경을 줄여도 안 되면(거의 없겠지만), 그냥 Saliency 1등부터 6등까지 짤라서 리턴
        Debug.LogWarning($"[Saliency] 적절한 분산 점을 찾지 못했습니다. 단순히 점수가 높은 상위 {targetCount}개를 반환합니다.");
        return sortedCandidates.Take(targetCount).ToList();
    }

    /// <summary>
    /// 실제 NMS 로직을 수행하는 내부 함수
    /// </summary>
    private static List<Vector3> RunNMS(List<Vector3> sortedCandidates, float radius, int targetCount)
    {
        List<Vector3> selected = new List<Vector3>();
        // 원본 리스트를 훼손하지 않기 위해 복사해서 사용 (검사 대상들)
        List<Vector3> pool = new List<Vector3>(sortedCandidates);

        float sqrRadius = radius * radius; // 거리 비교 최적화 (Sqrt 연산 방지)

        while (selected.Count < targetCount && pool.Count > 0)
        {
            // 1. 현재 풀에서 Saliency가 가장 높은 점 선택
            Vector3 best = pool[0];
            selected.Add(best);

            // 2. 선택된 점과 그 주변 반경(radius) 내에 있는 점들을 모두 제거 (억제)
            // 거리 비교 시 Vector3.Distance 대신 sqrMagnitude 사용이 더 빠름
            pool.RemoveAll(candidate => (candidate - best).sqrMagnitude < sqrRadius);
        }

        return selected;
    }

    //------------
    public static List<Vector3> SelectVerticesWithMultiplicativeFPS(List<Vector3> candidates, Dictionary<Vector3, float> saliencyMap, float l, int selectionCount)
    {
        List<Vector3> selected = new List<Vector3>();

        // 원본 리스트 보호를 위해 복사본 사용
        List<Vector3> pool = new List<Vector3>(candidates);

        if (pool.Count <= selectionCount)
            return pool; // 후보가 목표보다 적으면 그냥 다 반환

        // --- Step 1: 첫 번째 점은 무조건 Saliency 1등 선택 ---
        // (기준점이 확실해야 이후 분산이 잘 됩니다)
        Vector3 first = pool.OrderByDescending(v => saliencyMap[v]).First();
        selected.Add(first);
        pool.Remove(first);

        // --- Step 2: 목표 개수(6개) 채울 때까지 반복 ---
        while (selected.Count < selectionCount && pool.Count > 0)
        {
            Vector3 bestCandidate = Vector3.zero;
            float bestScore = float.MinValue;

            foreach (var candidate in pool)
            {
                // 1. 이미 선택된 점들과의 거리 중 '최소 거리' 계산 (가장 가까운 선택된 점과의 거리)
                float minDistanceToSelected = float.MaxValue;
                foreach (var s in selected)
                {
                    float d = Vector3.Distance(candidate, s);
                    if (d < minDistanceToSelected) minDistanceToSelected = d;
                }

                // 2. 점수 계산 (핵심: 곱셈 방식)
                // Saliency(중요도) x NormalizedDistance(희소성)
                // 거리를 l(전체크기)로 나누어 0~1 사이로 정규화 -> 스케일 영향 제거
                float normalizedDist = minDistanceToSelected / l;
                float saliency = saliencyMap[candidate];

                // *거리 가중치를 더 주고 싶으면 Math.Pow(normalizedDist, 0.5f) 등으로 조절 가능하지만,
                // 기본 곱셈이 가장 밸런스가 좋습니다.
                float score = saliency * normalizedDist;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestCandidate = candidate;
                }
            }

            // 이번 라운드 1등을 선택 목록에 추가하고 후보군에서 제거
            selected.Add(bestCandidate);
            pool.Remove(bestCandidate);
        }

        return selected;
    }
    //
    public static void HighlightVertex(Vector3 position, Color color, float scale, bool isSaliencyHighlight, int index = -1)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * scale;
        if (isSaliencyHighlight)
            sphere.name = $"HighVertex_{index}";

        Renderer r = sphere.GetComponent<Renderer>();
        if (r != null)
        {
            r.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            r.material.color = color;
        }
    }
    //중복 없애기 (수정 필요성? -> 추후 확인 예정)
    public static Vector3[] GetUniqueWorldVertices(MeshFilter meshFilter, int precision = 1000)
    {
        return meshFilter.mesh.vertices
            .Select(v => meshFilter.transform.TransformPoint(v))
            .GroupBy(v => new Vector3(
                Mathf.Round(v.x * precision) / precision,
                Mathf.Round(v.y * precision) / precision,
                Mathf.Round(v.z * precision) / precision
            ))
            .Select(g => g.First())
            .ToArray();
    }
   
    // 필터에서 사용중
    private static Vector3 GetVertexNormal(MeshFilter meshFilter, Vector3 vertexPosition)
    {
        Vector3[] positions = meshFilter.mesh.vertices;
        Vector3[] normals = meshFilter.mesh.normals;

        for (int i = 0; i < positions.Length; i++)
        {
            if (Vector3.Distance(meshFilter.transform.TransformPoint(positions[i]), vertexPosition) < 0.001f)
                return normals[i];
        }

        return Vector3.zero;
    }

    //----------------------------------neighbor 탐색 수정 중-----------------------------------------------

    /// <summary>
    /// 중복 위치를 가진 vertex index들을 포함하여 topology neighbor 확장 (depth 제한 적용)
    /// </summary>
    public static HashSet<int> FindTopologyNeighbors(int startIndex, Dictionary<int, HashSet<int>> adjacency, int maxDepth, Mesh mesh, Transform transform)
    {
        Queue<(int, int)> queue = new Queue<(int, int)>();
        HashSet<int> visited = new HashSet<int> { startIndex };
        queue.Enqueue((startIndex, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= maxDepth) continue;

            foreach (int neighbor in adjacency[current])
            {
                if (visited.Add(neighbor))
                    queue.Enqueue((neighbor, depth + 1));
            }
        }

        // 중복 위치 정점 보정 포함
        Dictionary<Vector3, List<int>> positionMap = GetOrBuildPositionToIndicesMap(mesh, transform);
        HashSet<int> expanded = new HashSet<int>(visited);
        foreach (int idx in visited)
        {
            Vector3 wp = transform.TransformPoint(mesh.vertices[idx]);
            if (positionMap.TryGetValue(wp, out var dupList))
            {
                foreach (var dupIdx in dupList)
                    expanded.Add(dupIdx);
            }
        }

        expanded.Remove(startIndex);
        return expanded;
    }


    /// <summary>
    /// Euclidean 거리 기반 이웃 탐색 (반경 내 정점 반환)
    /// </summary>
    public static HashSet<int> FindEuclideanNeighbors(Vector3[] worldPositions, int centerIndex, float radius)
    {
        HashSet<int> neighbors = new();
        Vector3 center = worldPositions[centerIndex];
        float rSqr = radius * radius;

        for (int i = 0; i < worldPositions.Length; i++)
        {
            if (i == centerIndex) continue;
            if ((worldPositions[i] - center).sqrMagnitude <= rSqr)
                neighbors.Add(i);
        }
        return neighbors;
    }

    /// <summary>
    /// Geodesic 기반 이웃 탐색 (Dijkstra 방식, 거리 제한) -> 아직은 미사용
    /// </summary>
    public static HashSet<int> FindGeodesicNeighbors(int startIndex, Mesh mesh, Transform transform, float maxDistance)
    {
        Vector3[] vertices = mesh.vertices;
        Dictionary<int, HashSet<int>> adjacency = BuildAdjacency(mesh);
        Vector3[] worldPositions = vertices.Select(v => transform.TransformPoint(v)).ToArray();

        HashSet<int> result = new();
        Dictionary<int, float> dist = new() { [startIndex] = 0f };
        PriorityQueue<int> queue = new();
        queue.Enqueue(startIndex, 0f);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            float currentDist = dist[current];

            if (current != startIndex)
                result.Add(current);

            if (adjacency.TryGetValue(current, out var neighbors))
            {
                foreach (int neighbor in neighbors)
                {
                    float edgeLength = (worldPositions[current] - worldPositions[neighbor]).magnitude;
                    float newDist = currentDist + edgeLength;

                    if (newDist <= maxDistance && (!dist.ContainsKey(neighbor) || newDist < dist[neighbor]))
                    {
                        dist[neighbor] = newDist;
                        queue.Enqueue(neighbor, newDist);
                    }
                }
            }
        }

        return result;
    }

    // 기존 adjacency 생성 함수 그대로 유지
    public static Dictionary<int, HashSet<int>> BuildAdjacency(Mesh mesh)
    {
        var adjacency = new Dictionary<int, HashSet<int>>();
        int[] triangles = mesh.triangles;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v0 = triangles[i];
            int v1 = triangles[i + 1];
            int v2 = triangles[i + 2];
            AddEdge(adjacency, v0, v1);
            AddEdge(adjacency, v1, v2);
            AddEdge(adjacency, v2, v0);
        }
        return adjacency;
    }

    private static void AddEdge(Dictionary<int, HashSet<int>> adj, int a, int b)
    {
        if (!adj.ContainsKey(a)) adj[a] = new HashSet<int>();
        if (!adj.ContainsKey(b)) adj[b] = new HashSet<int>();
        adj[a].Add(b);
        adj[b].Add(a);
    }




    //기존에 수정중이던 neighbor 탐색 방법들 (사용 여부 확인 예정)

    private static Dictionary<Mesh, Dictionary<int, HashSet<int>>> _adjacencyCache = new();
    private static Dictionary<Mesh, Dictionary<Vector3, List<int>>> _positionMapCache = new();

    public static Dictionary<int, HashSet<int>> GetOrBuildAdjacency(Mesh mesh)
    {
        if (_adjacencyCache.TryGetValue(mesh, out var cached))
            return cached;

        var built = BuildAdjacency(mesh);
        _adjacencyCache[mesh] = built;
        return built;
    }

   

    /// <summary>
    /// worldPos에 가장 가까운 정점 index 반환
    /// </summary>
    public static int FindNearestVertexIndex(Vector3 worldPos, Vector3[] worldVertices)
    {
        float minDist = float.MaxValue;
        int nearestIndex = 0;
        for (int i = 0; i < worldVertices.Length; i++)
        {
            float dist = (worldVertices[i] - worldPos).sqrMagnitude;
            if (dist < minDist)
            {
                minDist = dist;
                nearestIndex = i;
            }
        }
        return nearestIndex;
    }

    /// <summary>
    /// 동일 world position을 공유하는 vertex index 리스트 생성
    /// </summary>
    public static Dictionary<Vector3, List<int>> GetOrBuildPositionToIndicesMap(Mesh mesh, Transform transform)
    {
        if (_positionMapCache.TryGetValue(mesh, out var cached))
            return cached;

        var dict = new Dictionary<Vector3, List<int>>();
        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(verts[i]);
            if (!dict.TryGetValue(worldPos, out var list))
            {
                list = new List<int>();
                dict[worldPos] = list;
            }
            list.Add(i);
        }

        _positionMapCache[mesh] = dict;
        return dict;
    }
    public static Dictionary<Vector3, float> NormalizeSaliencyMap(Dictionary<Vector3, float> saliencyMap)
    {
        Dictionary<Vector3, float> normalized = new();
        float min = saliencyMap.Values.Min();
        float max = saliencyMap.Values.Max();
        foreach (var kv in saliencyMap)
        {
            float t = Mathf.Clamp01((kv.Value - min) / (max - min));
            normalized[kv.Key] = t;
        }
        return normalized;
    }

    public static Dictionary<Vector3, float> GroupSaliencyByRoundedPosition(Dictionary<Vector3, float> saliencyMap, int precision = 1000)
    {
        return saliencyMap
            .GroupBy(kv => new Vector3(
                Mathf.Round(kv.Key.x * precision) / precision,
                Mathf.Round(kv.Key.y * precision) / precision,
                Mathf.Round(kv.Key.z * precision) / precision))
            .ToDictionary(g => g.Key, g => g.First().Value);
    }
}
public class PriorityQueue<T>
{
    private readonly SortedDictionary<float, Queue<T>> _dict = new();

    public void Enqueue(T item, float priority)
    {
        if (!_dict.TryGetValue(priority, out var queue))
        {
            queue = new Queue<T>();
            _dict[priority] = queue;
        }
        queue.Enqueue(item);
    }

    public T Dequeue()
    {
        var first = _dict.First();
        var item = first.Value.Dequeue();
        if (first.Value.Count == 0)
            _dict.Remove(first.Key);
        return item;
    }

    public int Count => _dict.Sum(p => p.Value.Count);
}
//public static List<Vector3> FilterVerticesForCalibration(
//      Camera cam, MeshFilter meshFilter, List<Vector3> visibleVertices,
//      float silhouetteDotThreshold = 0.2f,
//      float edgeProximityThreshold = 0.01f)
//{
//    List<Vector3> filtered = new List<Vector3>();
//    Mesh mesh = meshFilter.sharedMesh;
//    Vector3[] meshNormals = mesh.normals;
//    Vector3[] meshVertices = mesh.vertices;
//    Vector3[] worldVertices = meshVertices.Select(v => meshFilter.transform.TransformPoint(v)).ToArray();

//    // [1] dot 기반 필터링 후보 등록
//    HashSet<Vector3> dotSilhouetteVertices = new();
//    foreach (var v in visibleVertices)
//    {
//        Vector3 normal = meshFilter.transform.TransformDirection(GetVertexNormalA(meshFilter, v));
//        Vector3 viewDir = (cam.transform.position - v).normalized;
//        float dot = Mathf.Abs(Vector3.Dot(normal.normalized, viewDir));
//        if (dot < silhouetteDotThreshold)
//            dotSilhouetteVertices.Add(v);
//    }

//    // [2] geometry 기반 edge 실루엣 정점 탐색
//    HashSet<int> edgeSilhouetteIndices = new();
//    int[] triangles = mesh.triangles;
//    Vector3[] triangleNormals = new Vector3[triangles.Length / 3];
//    for (int i = 0; i < triangles.Length; i += 3)
//    {
//        Vector3 v0 = worldVertices[triangles[i]];
//        Vector3 v1 = worldVertices[triangles[i + 1]];
//        Vector3 v2 = worldVertices[triangles[i + 2]];
//        Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
//        triangleNormals[i / 3] = normal;
//    }

//    Dictionary<(int, int), List<int>> edgeToTriangle = new();
//    for (int i = 0; i < triangles.Length; i += 3)
//    {
//        int tIndex = i / 3;
//        int[] tri = { triangles[i], triangles[i + 1], triangles[i + 2] };
//        for (int j = 0; j < 3; j++)
//        {
//            int a = tri[j];
//            int b = tri[(j + 1) % 3];
//            var edge = (Mathf.Min(a, b), Mathf.Max(a, b));
//            if (!edgeToTriangle.ContainsKey(edge)) edgeToTriangle[edge] = new List<int>();
//            edgeToTriangle[edge].Add(tIndex);
//        }
//    }

//    Vector3 camPos = cam.transform.position;
//    foreach (var kv in edgeToTriangle)
//    {
//        if (kv.Value.Count != 2) continue;
//        int t0 = kv.Value[0];
//        int t1 = kv.Value[1];
//        Vector3 n0 = triangleNormals[t0];
//        Vector3 n1 = triangleNormals[t1];

//        int vi0 = kv.Key.Item1;
//        int vi1 = kv.Key.Item2;
//        Vector3 edgeCenter = (worldVertices[vi0] + worldVertices[vi1]) * 0.5f;
//        Vector3 viewDir = (camPos - edgeCenter).normalized;
//        float d0 = Vector3.Dot(n0, viewDir);
//        float d1 = Vector3.Dot(n1, viewDir);

//        if ((d0 > 0 && d1 < 0) || (d0 < 0 && d1 > 0))
//        {
//            edgeSilhouetteIndices.Add(vi0);
//            edgeSilhouetteIndices.Add(vi1);
//        }
//    }

//    HashSet<Vector3> edgeSilhouettePositions = new();
//    foreach (int i in edgeSilhouetteIndices)
//    {
//        edgeSilhouettePositions.Add(worldVertices[i]);
//    }

//    // [3] 최종 제거
//    foreach (var v in visibleVertices)
//    {
//        bool isDotSilhouette = dotSilhouetteVertices.Contains(v);
//        bool isEdgeNear = edgeSilhouettePositions.Any(s => (s - v).sqrMagnitude < edgeProximityThreshold * edgeProximityThreshold);

//        if (!isDotSilhouette && !isEdgeNear)
//            filtered.Add(v);
//    }

//    return filtered;
//}

//private static Vector3 GetVertexNormalA(MeshFilter meshFilter, Vector3 vertexWorldPos)
//{
//    Vector3[] localVertices = meshFilter.sharedMesh.vertices;
//    Vector3[] normals = meshFilter.sharedMesh.normals;

//    for (int i = 0; i < localVertices.Length; i++)
//    {
//        Vector3 worldPos = meshFilter.transform.TransformPoint(localVertices[i]);
//        if (Vector3.Distance(worldPos, vertexWorldPos) < 0.0005f)
//            return normals[i];
//    }

//    return Vector3.up; // fallback
//}
