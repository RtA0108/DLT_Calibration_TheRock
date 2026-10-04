using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

// saliency Python 계산 한 건. 외부 프로세스를 백그라운드 스레드에서 돌려서 Unity가 멈추지 않게 한다.
//  - CfS-CNN: Windows python + bridge_cfs.py -> <cfsOutDir>/<OBJ 이름>_saliency.txt
//  - TexMesh: WSL python + bridge_tex.py     -> <texOutDir>/<OBJ 이름>_vertex_saliency.txt
// 경로는 메인 스레드에서 정해서 넘긴다 (Application.dataPath 같은 Unity API는 다른 스레드에서 못 씀).
// 콘솔 로그는 백그라운드 스레드에서 바로 남긴다 (Debug.Log는 어느 스레드에서나 호출 가능).
// 실행 중 플레이를 멈춰도 Python 프로세스는 끝까지 돌아 결과 파일을 남긴다.
public class SaliencyJob
{
    public struct Settings
    {
        public string localPython;    // CfS용 Windows python 실행 파일 (예: "python")
        public string cfsScriptPath;  // bridge_cfs.py 절대 경로
        public string wslPython;      // TexMesh용 WSL python 경로
        public string wslScriptPath;  // WSL 안의 bridge_tex.py 경로
    }

    public readonly string objPath, cfsOutDir, texOutDir;
    public readonly bool runCfs, runTex;

    public string CfsResultPath => Path.Combine(cfsOutDir, Path.GetFileNameWithoutExtension(objPath) + "_saliency.txt");
    public string TexResultPath => Path.Combine(texOutDir, Path.GetFileNameWithoutExtension(objPath) + "_vertex_saliency.txt");

    public bool CfsSucceeded { get; private set; }
    public bool TexSucceeded { get; private set; }
    public bool IsDone => task.IsCompleted;
    public string Stage => stage; // 지금 하는 일 (화면 표시용)
    public float ElapsedSeconds => (float)watch.Elapsed.TotalSeconds;

    private volatile string stage = "준비 중";
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private readonly DateTime startedUtc = DateTime.UtcNow;
    private readonly Task task;

    private SaliencyJob(string objPath, string cfsOutDir, string texOutDir, bool runCfs, bool runTex, Settings settings)
    {
        this.objPath = objPath;
        this.cfsOutDir = cfsOutDir;
        this.texOutDir = texOutDir;
        this.runCfs = runCfs;
        this.runTex = runTex;
        task = Task.Run(() => Run(settings));
    }

    public static SaliencyJob Start(string objPath, string cfsOutDir, string texOutDir, bool runCfs, bool runTex, Settings settings)
    {
        return new SaliencyJob(objPath, cfsOutDir, texOutDir, runCfs, runTex, settings);
    }

    private void Run(Settings s)
    {
        try
        {
            if (runCfs)
            {
                stage = "CfS-CNN 계산 중";
                CfsSucceeded = RunCfs(s) && WrittenSinceStart(CfsResultPath);
            }
            if (runTex)
            {
                stage = "TexMesh 계산 중";
                TexSucceeded = RunTex(s) && WrittenSinceStart(TexResultPath);
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[Saliency] 계산 중 예외: {e}");
        }
        finally
        {
            stage = "완료";
            watch.Stop();
        }
    }

    // 프로세스가 0으로 끝나도 결과 파일을 못 썼을 수 있어서, 이번 계산에서 새로 써졌는지 확인
    private bool WrittenSinceStart(string path)
    {
        return File.Exists(path) && File.GetLastWriteTimeUtc(path) >= startedUtc.AddSeconds(-2);
    }

    private bool RunCfs(Settings s)
    {
        if (!File.Exists(s.cfsScriptPath))
        {
            UnityEngine.Debug.LogError($"[Local Error] 스크립트 파일이 없습니다! 경로를 확인하세요: {s.cfsScriptPath}");
            return false;
        }

        var start = NewStartInfo(s.localPython, $"\"{s.cfsScriptPath}\" \"{objPath}\" \"{cfsOutDir}\"");
        start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8"; // 한글 출력이 깨지지 않게
        return Execute(start, "Local(CfS)");
    }

    private bool RunTex(Settings s)
    {
        // bridge_tex.py는 텍스처를 OBJ의 MTL에서 직접 찾으므로 --tex는 넘기지 않는다.
        string args = $"{s.wslPython} \"{s.wslScriptPath}\" --mesh \"{ToWslPath(objPath)}\" --out \"{ToWslPath(texOutDir)}\"";
        return Execute(NewStartInfo("wsl", args), "WSL(Tex)");
    }

    private static ProcessStartInfo NewStartInfo(string fileName, string arguments)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
    }

    // stdout과 stderr를 동시에 읽는다. (예전처럼 하나씩 ReadToEnd하면 다른 쪽 버퍼가 차서 멈출 수 있음)
    private static bool Execute(ProcessStartInfo startInfo, string label)
    {
        UnityEngine.Debug.Log($"[{label}] 명령어 실행:\n{startInfo.FileName} {startInfo.Arguments}");

        var output = new StringBuilder();
        var error = new StringBuilder();
        try
        {
            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (error) error.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (output.Length > 0) UnityEngine.Debug.Log($"[{label} Output]\n{output}");

                if (process.ExitCode != 0)
                {
                    if (error.Length > 0) UnityEngine.Debug.LogError($"[{label} Error / StdErr]\n{error}");
                    UnityEngine.Debug.LogError($"[{label}] 프로세스 비정상 종료 (ExitCode: {process.ExitCode})");
                    return false;
                }
                // 정상 종료여도 경고나 진행 표시가 stderr로 나올 수 있어서 에러가 아닌 경고로 남긴다.
                if (error.Length > 0) UnityEngine.Debug.LogWarning($"[{label} StdErr]\n{error}");
                return true;
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[{label}] 실행 실패 (Exception): {e.Message}");
            return false;
        }
    }

    public static string ToWslPath(string windowsPath)
    {
        if (string.IsNullOrEmpty(windowsPath)) return "";
        string path = windowsPath.Replace("\\", "/");
        if (path.Length > 1 && path[1] == ':')
        {
            char driveLetter = char.ToLower(path[0]);
            path = $"/mnt/{driveLetter}{path.Substring(2)}";
        }
        return path;
    }
}
