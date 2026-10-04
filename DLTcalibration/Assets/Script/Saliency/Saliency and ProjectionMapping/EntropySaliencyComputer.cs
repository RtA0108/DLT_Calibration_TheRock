using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Diagnostics;
public static class EntropySaliencyComputer
{
    public static Dictionary<Vector3, float> Compute(MeshFilter meshFilter, List<Vector3> visibleVertices, float l)
    {
        // Stopwatch 시작
        Stopwatch sw = new Stopwatch();
        sw.Start();

        Dictionary<Vector3, float> entropyMap = new Dictionary<Vector3, float>();
        float[] entropyValues = ComputeVertexEntropy(visibleVertices, l, meshFilter);

        for (int i = 0; i < visibleVertices.Count; i++)
            entropyMap[visibleVertices[i]] = entropyValues[i];

        sw.Stop(); // Stopwatch 정지
        UnityEngine.Debug.Log($"[Profile] EntropySaliencyComputer.Compute: {sw.Elapsed.TotalSeconds:F2} seconds");

        return entropyMap;
    }

    private static float[] ComputeVertexEntropy(List<Vector3> visibleVertices, float l, MeshFilter meshFilter)
    {
        float[] entropyValues = new float[visibleVertices.Count];
        List<int> neighborCounts = new List<int>();

        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] worldPositions = mesh.vertices.Select(v => meshFilter.transform.TransformPoint(v)).ToArray();
        Vector3[] normals = mesh.normals;
        float sigma = 0.05f * l;

        Dictionary<Vector3, List<int>> positionToIndicesMap = SaliencyUtils.GetOrBuildPositionToIndicesMap(mesh, meshFilter.transform);


        for (int i = 0; i < visibleVertices.Count; i++)
        {
            Vector3 vertex = visibleVertices[i];
            int vIndex = SaliencyUtils.FindNearestVertexIndex(vertex, worldPositions);

            HashSet<int> neighbors = SaliencyUtils.FindEuclideanNeighbors(worldPositions, vIndex, sigma);

            int neighborCount = neighbors.Count;
            neighborCounts.Add(neighborCount);
            if (neighbors.Count == 0)
            {
                entropyValues[i] = 1e-3f;
                continue;
            }

            Dictionary<Vector3Int, int> normalHistogram = new();

            foreach (int ni in neighbors)
            {
                Vector3 neighborWorldPos = worldPositions[ni];
                if (!positionToIndicesMap.TryGetValue(neighborWorldPos, out var indices)) continue;

                Vector3 avgNormal = (indices.Count == 1) ? normals[indices[0]] : indices.Aggregate(Vector3.zero, (acc, idx) => acc + normals[idx]).normalized;
                Vector3 transformed = meshFilter.transform.TransformDirection(avgNormal);

                Vector3Int quantized = SphericalQuantizeNormal(transformed, 10); // 정규화된 분포가 과하지 않게
                if (!normalHistogram.TryAdd(quantized, 1))
                    normalHistogram[quantized]++;
            }

            if (normalHistogram.Count == 0)
            {
                entropyValues[i] = 1e-3f;
                continue;
            }

            float entropy = 0f;
            int totalNormals = neighbors.Count;

            foreach (var count in normalHistogram.Values)
            {
                float p = (float)count / (totalNormals + 1e-6f);
                if (p > 0f && !float.IsNaN(p))
                    entropy -= p * Mathf.Log(p + 1e-6f);
            }

            if (float.IsNaN(entropy) || float.IsInfinity(entropy)) entropy = 0f;

            float maxEntropy = Mathf.Log(normalHistogram.Keys.Count + 1e-6f);
            float normalized = entropy / (maxEntropy + 1e-6f);
            if (float.IsNaN(normalized) || float.IsInfinity(normalized)) normalized = 0f;

            normalized = Mathf.Pow(normalized, 2.0f);
            float saliency = Mathf.Clamp01(normalized);

            entropyValues[i] = saliency;
        }

        if (neighborCounts.Count > 0)
        {
            int min = neighborCounts.Min();
            int max = neighborCounts.Max();
            float avg = (float)neighborCounts.Average();
            int under10 = neighborCounts.Count(n => n < 10);
            int under5 = neighborCounts.Count(n => n < 5);

            UnityEngine.Debug.Log($"[Entropy Neighbor Stat] Total={neighborCounts.Count}, Min={min}, Max={max}, Avg={avg:F2}, <10={under10}, <5={under5}");
        }

        return entropyValues;
    }



    //디버깅용 함수 (다른 mesh를 넣었는데 빨간점이 안나옴.)
    public static void CheckFilteredVerticesMatch(Dictionary<Vector3, float> entropyMap, List<Vector3> filteredVertices)
    {
        int matched = 0;
        int unmatched = 0;

        foreach (var v in filteredVertices)
        {
            if (entropyMap.ContainsKey(v))
            {
                matched++;
            }
            else
            {
                unmatched++;
                UnityEngine.Debug.LogWarning($"[Filtered Vertex Missing in EntropyMap] Pos={v}");
            }
        }

        UnityEngine.Debug.Log($"[Entropy Map Match Check] Matched: {matched}, Unmatched: {unmatched}, Total Filtered: {filteredVertices.Count}");
    }

    // 개선된 Spherical Quantization 방식
    private static Vector3Int SphericalQuantizeNormal(Vector3 normal, int resolution)
    {
        normal.Normalize();
        float theta = Mathf.Acos(Mathf.Clamp(normal.y, -1f, 1f)); // elevation
        float phi = Mathf.Atan2(normal.z, normal.x);              // azimuth
        if (phi < 0f) phi += 2f * Mathf.PI;

        int thetaIndex = Mathf.FloorToInt(theta / Mathf.PI * resolution);
        int phiIndex = Mathf.FloorToInt(phi / (2f * Mathf.PI) * resolution);

        return new Vector3Int(thetaIndex, phiIndex, 0);
    }

}