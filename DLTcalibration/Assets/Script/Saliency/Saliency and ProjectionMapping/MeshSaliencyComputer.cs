using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class MeshSaliencyComputer
{
    public static Dictionary<Vector3, float> Compute(MeshFilter meshFilter, List<Vector3> targetVertices, float l)
    {
        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] localVertices = mesh.vertices;
        Vector3[] worldVertices = localVertices.Select(v => meshFilter.transform.TransformPoint(v)).ToArray();

        float epsilon = 0.003f * l;
        float[] sigmas = new float[] { 2f, 3f, 4f, 5f, 6f }.Select(s => s * epsilon).ToArray();

        // 1. 중복 위치 제거: 대표 인덱스 하나만 추출
        Dictionary<Vector3, int> uniqueVertexMap = new(); // 위치 → 대표 인덱스
        for (int i = 0; i < worldVertices.Length; i++)
        {
            Vector3 pos = worldVertices[i];
            if (!uniqueVertexMap.ContainsKey(pos))
                uniqueVertexMap[pos] = i;
        }

        // 2. targetVertices 중에서 실제로 존재하는 고유 위치만 추출
        HashSet<Vector3> validTargetPositions = new HashSet<Vector3>(targetVertices.Where(uniqueVertexMap.ContainsKey));

        // 3. mean curvature 계산 (Taubin 기반) → log 변환
        Dictionary<int, float> meanCurvatures = ComputeMeanCurvatures_Taubin(meshFilter);
        Dictionary<int, float> hLog = meanCurvatures.ToDictionary(kv => kv.Key, kv => Mathf.Log(kv.Value + 1e-4f));

        // 4. Multi-scale saliency 계산
        Dictionary<Vector3, float[]> multiScale = new();
        foreach (float sigma in sigmas)
        {
            int sigmaIndex = System.Array.IndexOf(sigmas, sigma);

            foreach (Vector3 pos in validTargetPositions)
            {
                int index = uniqueVertexMap[pos];
                float g1 = GaussianSmooth(index, worldVertices, hLog, sigma);
                float g2 = GaussianSmooth(index, worldVertices, hLog, 2f * sigma);

                float saliency = Mathf.Abs(g1 - g2);
                if (!multiScale.ContainsKey(pos))
                    multiScale[pos] = new float[sigmas.Length];

                multiScale[pos][sigmaIndex] = saliency;
            }
        }

        return NormalizeSaliency(multiScale);
    }
    private static Dictionary<int, float> ComputeMeanCurvatures_Taubin(MeshFilter meshFilter)
    {
        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector3[] worldVertices = vertices.Select(v => meshFilter.transform.TransformPoint(v)).ToArray();
        int[] triangles = mesh.triangles;

        // 1. Face normals and areas
        Dictionary<int, List<int>> vertexFaces = new();
        Dictionary<int, Vector3> vertexNormals = new();
        Dictionary<int, float> areaSum = new();
        Dictionary<int, List<int>> neighbors = new();

        for (int i = 0; i < vertices.Length; i++)
        {
            vertexFaces[i] = new List<int>();
            neighbors[i] = new List<int>();
            areaSum[i] = 0f;
        }

        Vector3[] faceNormals = new Vector3[triangles.Length / 3];
        float[] faceAreas = new float[triangles.Length / 3];

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];

            Vector3 v0 = worldVertices[i0];
            Vector3 v1 = worldVertices[i1];
            Vector3 v2 = worldVertices[i2];

            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
            float area = normal.magnitude / 2f;
            normal.Normalize();

            int faceIdx = i / 3;
            faceNormals[faceIdx] = normal;
            faceAreas[faceIdx] = area;

            vertexFaces[i0].Add(faceIdx);
            vertexFaces[i1].Add(faceIdx);
            vertexFaces[i2].Add(faceIdx);

            neighbors[i0].Add(i1); neighbors[i0].Add(i2);
            neighbors[i1].Add(i0); neighbors[i1].Add(i2);
            neighbors[i2].Add(i0); neighbors[i2].Add(i1);

            areaSum[i0] += area / 3f;
            areaSum[i1] += area / 3f;
            areaSum[i2] += area / 3f;
        }

        foreach (int vi in vertexFaces.Keys)
        {
            Vector3 normalSum = Vector3.zero;
            foreach (int faceIdx in vertexFaces[vi])
                normalSum += faceNormals[faceIdx] * faceAreas[faceIdx];
            vertexNormals[vi] = normalSum.normalized;
        }

        // 2. Mean curvature estimation
        Dictionary<int, float> meanCurvature = new();

        foreach (int i in neighbors.Keys)
        {
            Vector3 ni = vertexNormals[i];
            Vector3 pi = worldVertices[i];

            Vector3 sum = Vector3.zero;
            float totalWeight = 0f;

            HashSet<int> uniqueNeighbors = new HashSet<int>(neighbors[i]);
            foreach (int j in uniqueNeighbors)
            {
                Vector3 pj = worldVertices[j];
                Vector3 dir = pj - pi;
                Vector3 t = dir - Vector3.Dot(dir, ni) * ni;
                t.Normalize();

                float kappa = 2f * Vector3.Dot(ni, pj - pi) / (Vector3.SqrMagnitude(pj - pi) + 1e-6f);

                // Weight by shared area (uniform for now)
                sum += kappa * t;
                totalWeight += 1f;
            }

            float A = areaSum[i] > 1e-6f ? areaSum[i] : 1f;
            meanCurvature[i] = sum.magnitude / (2f * A);
        }

        return meanCurvature;
    }

    private static float GaussianSmooth(int centerIdx, Vector3[] positions, Dictionary<int, float> hLog, float sigma)
    {
        float weightedSum = 0f;
        float totalWeight = 0f;
        float radius = 2f * sigma;

        for (int i = 0; i < positions.Length; i++)
        {
            float distSqr = (positions[i] - positions[centerIdx]).sqrMagnitude;
            if (distSqr > radius * radius) continue;

            float w = Mathf.Exp(-distSqr / (2f * sigma * sigma));
            totalWeight += w;
            weightedSum += hLog[i] * w;
        }

        return totalWeight > 1e-6f ? weightedSum / totalWeight : 0f;
    }

    private static Dictionary<Vector3, float> NormalizeSaliency(Dictionary<Vector3, float[]> saliencyMulti)
    {
        Dictionary<Vector3, float> finalSaliency = new();

        foreach (var kvp in saliencyMulti)
        {
            float average = kvp.Value.Average();
            finalSaliency[kvp.Key] = average;
        }

        float min = finalSaliency.Values.Min();
        float max = finalSaliency.Values.Max();
        float range = Mathf.Max(max - min, 1e-6f);

        foreach (var key in finalSaliency.Keys.ToList())
        {
            finalSaliency[key] = (finalSaliency[key] - min) / range;
        }

        return finalSaliency;
    }
    //public static Dictionary<Vector3, float> Compute(MeshFilter meshFilter, List<Vector3> vertices, float l)
    //{
    //    Vector3[] meshVertices = meshFilter.mesh.vertices;
    //    Vector3[] worldPositions = meshVertices.Select(v => meshFilter.transform.TransformPoint(v)).ToArray();
    //    Dictionary<int, float> curvatureMap = ComputeMeanCurvatures(meshFilter);
    //    //Dictionary<int, float> curvatureMap = ComputeMeanCurvatures_Taubin(meshFilter);
    //    float epsilon = 0.01f * l; // 또는 0.02f 실험
    //    float[] sigmaScales = new float[] { 2f, 3f, 4f, 5f, 6f }.Select(s => s * epsilon).ToArray();

    //    Dictionary<Vector3, float[]> multiScaleSaliency = new();
    //    Dictionary<Vector3, float> finalSaliency = new();

    //    foreach (float sigma in sigmaScales)
    //    {
    //        foreach (Vector3 v in vertices)
    //        {
    //            int index = SaliencyUtils.FindNearestVertexIndex(v, worldPositions);
    //            float g1 = ComputeGaussianWeightedAverage(index, worldPositions, curvatureMap, sigma);
    //            float g2 = ComputeGaussianWeightedAverage(index, worldPositions, curvatureMap, 2f * sigma);

    //            float saliency = Mathf.Abs(g1 - g2);
    //            if (!multiScaleSaliency.ContainsKey(v))
    //                multiScaleSaliency[v] = new float[sigmaScales.Length];
    //            multiScaleSaliency[v][System.Array.IndexOf(sigmaScales, sigma)] = saliency;
    //        }
    //    }

    //    ApplyNonlinearSuppression(multiScaleSaliency, out finalSaliency);
    //    return finalSaliency;
    //}

    //private static float ComputeGaussianWeightedAverage(int centerIndex, Vector3[] worldPositions, Dictionary<int, float> curvatureMap, float sigma)
    //{
    //    float weightedSum = 0f;
    //    float weightTotal = 0f;
    //    float radius = 2f * sigma;

    //    for (int i = 0; i < worldPositions.Length; i++)
    //    {
    //        if (i == centerIndex) continue;
    //        float distSqr = (worldPositions[i] - worldPositions[centerIndex]).sqrMagnitude;
    //        if (distSqr > radius * radius) continue;

    //        float weight = Mathf.Exp(-distSqr / (2 * sigma * sigma));
    //        weightTotal += weight;
    //        weightedSum += curvatureMap[i] * weight;
    //    }

    //    return weightTotal > 1e-6f ? weightedSum / weightTotal : 0f;
    //}

    //private static void ApplyNonlinearSuppression(Dictionary<Vector3, float[]> multiScaleSaliency, out Dictionary<Vector3, float> finalSaliency)
    //{
    //    finalSaliency = new();
    //    int scaleCount = multiScaleSaliency.Values.First().Length;

    //    for (int i = 0; i < scaleCount; i++)
    //    {
    //        float max = multiScaleSaliency.Values.Max(arr => arr[i]);
    //        float avgLocalMax = multiScaleSaliency.Values.Select(arr => arr[i]).OrderByDescending(x => x).Take(10).Average();
    //        float weight = Mathf.Pow(max - avgLocalMax, 2f);

    //        foreach (var kvp in multiScaleSaliency)
    //        {
    //            if (!finalSaliency.ContainsKey(kvp.Key))
    //                finalSaliency[kvp.Key] = 0f;
    //            finalSaliency[kvp.Key] += kvp.Value[i] * weight;
    //        }
    //    }
    //}

    //private static Dictionary<int, float> ComputeMeanCurvatures(MeshFilter meshFilter)
    //{
    //    Mesh mesh = meshFilter.sharedMesh;
    //    Vector3[] vertices = mesh.vertices;
    //    Vector3[] worldVertices = vertices.Select(v => meshFilter.transform.TransformPoint(v)).ToArray();
    //    int[] triangles = mesh.triangles;

    //    Dictionary<int, List<(int j, float weight)>> cotangentWeights = new();
    //    Dictionary<int, float> areaSum = new();
    //    Dictionary<int, float> curvature = new();

    //    for (int i = 0; i < vertices.Length; i++)
    //    {
    //        cotangentWeights[i] = new List<(int, float)>();
    //        areaSum[i] = 0f;
    //    }

    //    for (int i = 0; i < triangles.Length; i += 3)
    //    {
    //        int i0 = triangles[i];
    //        int i1 = triangles[i + 1];
    //        int i2 = triangles[i + 2];

    //        Vector3 v0 = worldVertices[i0];
    //        Vector3 v1 = worldVertices[i1];
    //        Vector3 v2 = worldVertices[i2];

    //        float area = Vector3.Cross(v1 - v0, v2 - v0).magnitude / 6f;
    //        areaSum[i0] += area;
    //        areaSum[i1] += area;
    //        areaSum[i2] += area;

    //        AddCotangent(i0, i1, i2, v0, v1, v2, cotangentWeights);
    //        AddCotangent(i1, i2, i0, v1, v2, v0, cotangentWeights);
    //        AddCotangent(i2, i0, i1, v2, v0, v1, cotangentWeights);
    //    }

    //    foreach (int i in cotangentWeights.Keys)
    //    {
    //        Vector3 sum = Vector3.zero;
    //        foreach (var (j, weight) in cotangentWeights[i])
    //        {
    //            sum += weight * (worldVertices[j] - worldVertices[i]);
    //        }

    //        float A = areaSum[i] > 1e-6f ? areaSum[i] : 1f;
    //        curvature[i] = sum.magnitude / (2f * A);
    //    }

    //    return curvature;
    //}

    //private static void AddCotangent(int i0, int i1, int i2, Vector3 v0, Vector3 v1, Vector3 v2,
    //                                  Dictionary<int, List<(int j, float weight)>> cotangentWeights)
    //{
    //    Vector3 a = v1 - v0;
    //    Vector3 b = v2 - v0;
    //    float cot = Vector3.Dot(a, b) / Vector3.Cross(a, b).magnitude;

    //    if (!float.IsNaN(cot) && !float.IsInfinity(cot))
    //    {
    //        cotangentWeights[i0].Add((i1, cot));
    //        cotangentWeights[i1].Add((i0, cot));
    //    }
    //}
    ////Mesh Saliency 논문의 방식
    //private static Dictionary<int, float> ComputeMeanCurvatures_Taubin(MeshFilter meshFilter)
    //{
    //    Mesh mesh = meshFilter.sharedMesh;
    //    Vector3[] vertices = mesh.vertices;
    //    Vector3[] worldVertices = vertices.Select(v => meshFilter.transform.TransformPoint(v)).ToArray();
    //    Dictionary<int, HashSet<int>> adjacency = SaliencyUtils.GetOrBuildAdjacency(mesh);

    //    Dictionary<int, float> curvature = new();

    //    for (int i = 0; i < vertices.Length; i++)
    //    {
    //        if (!adjacency.TryGetValue(i, out var neighbors) || neighbors.Count == 0)
    //        {
    //            curvature[i] = 0f;
    //            continue;
    //        }

    //        Vector3 vi = worldVertices[i];
    //        Vector3 avgNeighbor = Vector3.zero;

    //        foreach (int j in neighbors)
    //        {
    //            avgNeighbor += worldVertices[j];
    //        }

    //        avgNeighbor /= neighbors.Count;
    //        Vector3 laplacian = avgNeighbor - vi;
    //        curvature[i] = laplacian.magnitude; // or .sqrMagnitude for speed
    //    }

    //    return curvature;
    //}
}