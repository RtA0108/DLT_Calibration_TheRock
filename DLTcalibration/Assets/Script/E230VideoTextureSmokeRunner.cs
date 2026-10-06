using System;
using System.Collections;
using System.IO;
using UnityEngine;

// E230 Unity 통합을 실제 Player에서 검증한다.
// 일반 실행에는 영향을 주지 않고, -e230Smoke 인자가 있을 때만 동작한다.
public class E230VideoTextureSmokeRunner : MonoBehaviour
{
    [Serializable]
    private class SmokeReport
    {
        public bool passed;
        public string unityVersion;
        public string modelId;
        public string videoPath;
        public string failure;
        public int textureWidth;
        public int textureHeight;
        public int rendererCount;
        public int vertexCount;
        public int uvCount;
        public bool uvComplete;
        public bool materialAssigned;
        public long startFrame;
        public long endFrame;
        public double startTime;
        public double endTime;
        public string screenshotPath;
    }

    private string reportPath;
    private SmokeReport report;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (!HasArgument("-e230Smoke")) return;

        GameObject runner = new GameObject("E230 Video Texture Smoke Runner");
        DontDestroyOnLoad(runner);
        runner.AddComponent<E230VideoTextureSmokeRunner>();
    }

    private IEnumerator Start()
    {
        reportPath = GetArgumentValue("-e230SmokeReport");
        if (string.IsNullOrEmpty(reportPath))
            reportPath = Path.Combine(Application.persistentDataPath, "e230_unity_smoke_report.json");

        report = new SmokeReport
        {
            unityVersion = Application.unityVersion,
            modelId = "TheRock",
            videoPath = Path.Combine(Application.streamingAssetsPath, "VideoTextures", "TheRock_E230_30s_Unity.mp4")
        };

        yield return RunSmokeTest();
        WriteReport();
        Debug.Log(report.passed
            ? $"[E230 Smoke] PASS - report: {reportPath}"
            : $"[E230 Smoke] FAIL - {report.failure} - report: {reportPath}");

        Application.Quit(report.passed ? 0 : 2);
    }

    private IEnumerator RunSmokeTest()
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (RuntimeMeshLoader.Instance == null && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (RuntimeMeshLoader.Instance == null)
        {
            Fail("RuntimeMeshLoader.Instance가 생성되지 않았습니다.");
            yield break;
        }

        // 테스트 중에는 기존 분석 파이프라인의 Python 작업을 시작하지 않는다.
        // 여기서는 The Rock 로드, UV, 비디오 디코딩과 재질 연결만 독립적으로 검증한다.
        MainController originalMainController = MainController.Instance;
        MainController.Instance = null;
        RuntimeMeshLoader.Instance.LoadMeshByID("TheRock");
        deadline = Time.realtimeSinceStartup + 30f;
        while ((RuntimeMeshLoader.Instance.CurrentLoadedObject == null
                || TextureSequenceAnimator.Instance == null
                || !TextureSequenceAnimator.Instance.IsVideoReady)
               && Time.realtimeSinceStartup < deadline)
            yield return null;
        MainController.Instance = originalMainController;

        GameObject model = RuntimeMeshLoader.Instance.CurrentLoadedObject;
        TextureSequenceAnimator animator = TextureSequenceAnimator.Instance;
        // 모델은 텍스처를 끈 상태로 불러와지므로(마커 맞추기용) 여기서 입힌다 (X 키와 같음)
        if (animator != null) animator.SetTextureVisible(true);
        yield return null;
        if (model == null)
        {
            Fail("TheRock 모델 로드에 실패했습니다.");
            yield break;
        }
        if (animator == null || !animator.IsVideoReady || animator.CurrentTexture == null)
        {
            Fail("E230 비디오가 제한 시간 안에 준비되지 않았습니다.");
            yield break;
        }

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        report.rendererCount = renderers.Length;
        report.materialAssigned = false;
        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.materials)
            {
                if (material != null && material.mainTexture == animator.CurrentTexture)
                {
                    report.materialAssigned = true;
                    break;
                }
            }
        }

        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            report.vertexCount += filter.sharedMesh.vertexCount;
            report.uvCount += filter.sharedMesh.uv != null ? filter.sharedMesh.uv.Length : 0;
        }
        report.uvComplete = report.vertexCount > 0 && report.uvCount == report.vertexCount;
        report.textureWidth = animator.CurrentTexture.width;
        report.textureHeight = animator.CurrentTexture.height;

        deadline = Time.realtimeSinceStartup + 10f;
        while (animator.CurrentVideoFrame < 0 && Time.realtimeSinceStartup < deadline)
            yield return null;

        report.startFrame = animator.CurrentVideoFrame;
        report.startTime = animator.CurrentVideoTime;
        yield return new WaitForSecondsRealtime(2f);
        report.endFrame = animator.CurrentVideoFrame;
        report.endTime = animator.CurrentVideoTime;

        string reportDirectory = Path.GetDirectoryName(reportPath);
        if (string.IsNullOrEmpty(reportDirectory)) reportDirectory = Application.persistentDataPath;
        Directory.CreateDirectory(reportDirectory);
        report.screenshotPath = Path.Combine(reportDirectory, "e230_unity_runtime.png");
        SetupDiagnosticCamera(model);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(report.screenshotPath);
        yield return new WaitForSecondsRealtime(0.5f);

        bool frameAdvanced = report.endFrame > report.startFrame || report.endTime > report.startTime + 0.5;
        report.passed = File.Exists(report.videoPath)
            && report.textureWidth == 2048
            && report.textureHeight == 2048
            && report.uvComplete
            && report.materialAssigned
            && frameAdvanced;

        if (!report.passed)
        {
            report.failure = $"file={File.Exists(report.videoPath)}, texture={report.textureWidth}x{report.textureHeight}, " +
                             $"uv={report.uvCount}/{report.vertexCount}, material={report.materialAssigned}, " +
                             $"frame={report.startFrame}->{report.endFrame}, time={report.startTime:F3}->{report.endTime:F3}";
        }
    }

    private static void SetupDiagnosticCamera(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        foreach (Camera existing in Camera.allCameras) existing.enabled = false;
        GameObject cameraObject = new GameObject("E230 Smoke Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.015f, 0.015f, 0.02f, 1f);
        camera.fieldOfView = 35f;
        camera.targetDisplay = 0;
        float distance = Mathf.Max(bounds.extents.magnitude * 2.8f, 1f);
        camera.transform.position = bounds.center + new Vector3(distance * 0.45f, distance * 0.1f, -distance);
        camera.transform.LookAt(bounds.center);

        GameObject lightObject = new GameObject("E230 Smoke Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
    }

    private void WriteReport()
    {
        string directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
    }

    private void Fail(string message)
    {
        report.passed = false;
        report.failure = message;
    }

    private static bool HasArgument(string name)
    {
        foreach (string argument in Environment.GetCommandLineArgs())
            if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string GetArgumentValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
