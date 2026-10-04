using UnityEngine;
using System.IO;

// saliency Python 계산 설정과 시작. 실제 실행은 SaliencyJob이 백그라운드에서 한다.
// (예전에는 메인 스레드에서 프로세스가 끝날 때까지 기다려서, 새 모델을 불러오면 Unity가 15초쯤 멈췄음)
public class PythonProcessManager : MonoBehaviour
{
    public static PythonProcessManager Instance;

    public const string DefaultLocalPython = "python";
    public const string DefaultLocalScript = "bridge_cfs.py";
    public const string DefaultScriptDirectory = "PythonScripts";

    [Header("Local Settings (CfS-CNN)")]
    public string localPythonPath = DefaultLocalPython;
    public string localScriptName = DefaultLocalScript;

    // WSL Conda Python 경로
    private const string WSL_PYTHON_PATH = "/home/minsu/miniconda3/envs/textured_saliency/bin/python";
    // WSL 실행 스크립트 경로
    private const string WSL_SCRIPT_PATH = "/home/minsu/TexMeshSaliency/bridge_tex.py";

    [Header("Common")]
    // 프로젝트 루트 기준 폴더명 (Assets 폴더와 같은 레벨에 있는 폴더)
    public string scriptDirectory = DefaultScriptDirectory;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // CfS와 TexMesh 결과를 각 폴더에 만든다. 바로 돌아오고, 끝났는지는 job.IsDone으로 확인.
    public SaliencyJob StartSaliencyCalculation(string objPath, string cfsOutDir, string texOutDir, bool runCfs = true, bool runTex = true)
    {
        Debug.Log($"[PythonProcessManager] Saliency 계산 시작 (백그라운드, CfS: {runCfs}, Tex: {runTex})");
        return SaliencyJob.Start(objPath, cfsOutDir, texOutDir, runCfs, runTex, SettingsFrom(this));
    }

    // 에디터 도구처럼 플레이 중이 아닐 때도 쓰도록 static. manager가 null이면 기본값.
    public static SaliencyJob.Settings SettingsFrom(PythonProcessManager manager)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string directory = manager != null ? manager.scriptDirectory : DefaultScriptDirectory;
        string script = manager != null ? manager.localScriptName : DefaultLocalScript;
        return new SaliencyJob.Settings
        {
            localPython = manager != null ? manager.localPythonPath : DefaultLocalPython,
            cfsScriptPath = Path.Combine(projectRoot, directory, script),
            wslPython = WSL_PYTHON_PATH,
            wslScriptPath = WSL_SCRIPT_PATH,
        };
    }
}
