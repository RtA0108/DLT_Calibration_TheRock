using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// 새 모델을 라이브러리에 추가하는 창. 메뉴: Tools > 캘리브레이션 > 새 모델 추가
// 예전에는 아래를 손으로 했다 (TheRock 추가 때 실제로 1~3번이 빠져서 문제가 났음).
//  1. OBJ와 MTL, 텍스처를 Assets/Resources/Meshes/<id>/로 복사. OBJ 이름은 <id>.obj
//     (saliency 결과 파일 이름이 OBJ 이름을 따르므로 id와 맞춘다)
//  2. 텍스처 연결: MTL이 없거나 재질 지정(usemtl)이 빠져 있으면 만들어 넣는다
//  3. 메쉬 Read/Write 켜기 (꺼져 있으면 런타임에 버텍스를 못 읽어 버텍스 구 생성과 추천이 실패)
//  4. DefaultLibrary.txt에 항목 추가
//  5. (선택) 4D 텍스처 이미지 시퀀스를 Resources/4D_Textures/<id>/로 복사
//  6. (선택) saliency 계산 (CfS + TexMesh, 백그라운드) -> Resources/Result_CfS, Result_Tex
public class ModelImportWindow : EditorWindow
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };
    private static readonly string[] MapKeywords = { "map_Kd", "map_Ka", "map_Ks", "map_Ns", "map_d", "map_Bump", "map_bump", "bump", "disp", "norm" };

    private string objSource = "", textureSource = "", sequenceSource = "";
    private string id = "", displayName = "";
    private bool copySequence = true, runSaliency = true;

    private string mtlInfo = "";      // OBJ를 고르면 MTL/텍스처를 어떻게 찾았는지 표시
    private bool textureFromMtl;      // MTL에 텍스처가 이미 지정돼 있으면 그걸 쓴다

    private SaliencyJob job;
    private double lastRepaint;
    private Vector2 scroll;
    private readonly List<(MessageType type, string text)> report = new List<(MessageType, string)>();

    private static string ResourcesDir => Path.Combine(Application.dataPath, "Resources");
    private static string LibraryPath => Path.Combine(ResourcesDir, "DefaultLibrary.txt");
    // 사용법 문서: Unity 프로젝트 폴더(Assets 옆)에 있어서 탐색기에서도 바로 보임
    private static string GuidePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "새_모델_추가_방법.md");

    private static void OpenGuide()
    {
        if (File.Exists(GuidePath)) EditorUtility.OpenWithDefaultApp(GuidePath);
        else Debug.LogWarning($"[모델 추가] 사용법 문서가 없습니다: {GuidePath}");
    }

    [MenuItem("Tools/캘리브레이션/새 모델 추가")]
    private static void Open()
    {
        GetWindow<ModelImportWindow>("새 모델 추가").minSize = new Vector2(460f, 420f);
    }

    private void OnDisable()
    {
        EditorApplication.update -= PollJob;
    }

    // ------------------------------------------------------------------ 화면
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.HelpBox(
                "OBJ를 고르면 MTL과 텍스처를 함께 복사하고, 텍스처 연결, Read/Write, 라이브러리 등록, saliency 계산까지 한 번에 합니다.",
                MessageType.Info);
            if (GUILayout.Button("사용법", GUILayout.Width(56f), GUILayout.Height(38f))) OpenGuide();
        }

        using (new EditorGUI.DisabledScope(job != null))
        {
            EditorGUILayout.LabelField("모델", EditorStyles.boldLabel);
            if (PathField("OBJ 파일", ref objSource, () => EditorUtility.OpenFilePanel("OBJ 선택", DirOf(objSource), "obj")))
                OnObjChanged();

            using (new EditorGUI.DisabledScope(textureFromMtl))
            {
                if (PathField("텍스처 (선택)", ref textureSource, () => EditorUtility.OpenFilePanelWithFilters(
                        "텍스처 선택", DirOf(objSource), new[] { "Image", "png,jpg,jpeg,tga,bmp" })))
                    mtlInfo = DescribeTexture();
            }
            if (!string.IsNullOrEmpty(mtlInfo)) EditorGUILayout.LabelField(" ", mtlInfo, EditorStyles.wordWrappedMiniLabel);

            id = EditorGUILayout.TextField(new GUIContent("id", "라이브러리 id, 폴더 이름, OBJ 이름, saliency 결과 파일 이름에 쓰임"), id);
            displayName = EditorGUILayout.TextField(new GUIContent("표시 이름", "모델 목록에 보이는 이름"), displayName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("4D 텍스처 (선택)", EditorStyles.boldLabel);
            PathField("이미지 시퀀스 폴더", ref sequenceSource, () => EditorUtility.OpenFolderPanel("이미지 시퀀스 폴더 선택", DirOf(sequenceSource), ""));
            copySequence = EditorGUILayout.Toggle(new GUIContent("Resources로 복사",
                "켜면 Resources/4D_Textures/<id>/로 복사(자동 인식). 끄면 지금 폴더를 직접 참조(빌드에는 포함 안 됨)"), copySequence);

            EditorGUILayout.Space();
            runSaliency = EditorGUILayout.Toggle(new GUIContent("saliency 계산",
                "CfS-CNN과 TexMesh를 지금 계산한다. 끄면 플레이에서 처음 불러올 때 계산됨"), runSaliency);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!File.Exists(objSource) || string.IsNullOrWhiteSpace(id)))
            {
                if (GUILayout.Button("추가", GUILayout.Height(28f))) Add();
            }
        }

        if (job != null)
        {
            EditorGUILayout.Space();
            Rect r = EditorGUILayout.GetControlRect(false, 20f);
            float t = (float)(EditorApplication.timeSinceStartup % 2.0) / 2f; // 끝을 알 수 없어서 움직이는 막대만
            EditorGUI.ProgressBar(r, t, $"saliency: {job.Stage}... {job.ElapsedSeconds:F0}초");
            EditorGUILayout.HelpBox("계산이 끝날 때까지 플레이 시작이나 스크립트 수정은 피하세요. " +
                                    "Python은 끝까지 돌지만 이 창이 결과를 확인하지 못합니다.", MessageType.None);
        }

        if (report.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);
            foreach (var (type, text) in report) EditorGUILayout.HelpBox(text, type);
        }
        EditorGUILayout.EndScrollView();
    }

    // 경로 입력칸 + 찾기 버튼. 값이 바뀌면 true.
    private static bool PathField(string label, ref string value, Func<string> browse)
    {
        string before = value;
        using (new EditorGUILayout.HorizontalScope())
        {
            value = EditorGUILayout.TextField(label, value);
            if (GUILayout.Button("찾기", GUILayout.Width(48f)))
            {
                string picked = browse();
                if (!string.IsNullOrEmpty(picked)) value = picked;
                GUI.FocusControl(null);
            }
        }
        return value != before;
    }

    private static string DirOf(string path)
    {
        return string.IsNullOrEmpty(path) ? "" : Path.GetDirectoryName(path);
    }

    // OBJ를 바꾸면 id/이름 기본값과 텍스처를 새로 정한다.
    private void OnObjChanged()
    {
        textureFromMtl = false;
        textureSource = "";
        mtlInfo = "";
        if (!File.Exists(objSource)) return;

        string stem = Path.GetFileNameWithoutExtension(objSource);
        id = Regex.Replace(stem, @"[^A-Za-z0-9_\-]", "_");
        displayName = stem;

        string mtl = FindMtl(objSource, File.ReadLines(objSource).Take(2000).ToArray());
        string mtlTexture = mtl != null ? FirstDiffuseTexture(mtl) : null;
        if (mtlTexture != null)
        {
            textureFromMtl = true;
            textureSource = mtlTexture;
        }
        else
        {
            textureSource = GuessTexture(objSource) ?? "";
        }
        mtlInfo = DescribeTexture(mtl);
    }

    private string DescribeTexture(string mtl = null)
    {
        if (textureFromMtl) return $"MTL({Path.GetFileName(mtl)})에 지정된 텍스처를 씁니다.";
        if (string.IsNullOrEmpty(textureSource)) return "텍스처 없음. TexMesh saliency는 텍스처 없이 계산됩니다.";
        return "MTL에 텍스처가 없어 이 이미지로 MTL을 만들어 연결합니다.";
    }

    // ------------------------------------------------------------------ 추가
    private void Add()
    {
        report.Clear();
        id = id.Trim();
        if (!Regex.IsMatch(id, @"^[A-Za-z0-9_\-]+$"))
        {
            Report(MessageType.Error, "id에는 영문, 숫자, _, -만 쓸 수 있습니다.");
            return;
        }
        if (string.IsNullOrWhiteSpace(displayName)) displayName = id;

        string meshPath = $"Meshes/{id}/{id}";
        string destDir = Path.Combine(ResourcesDir, "Meshes", id);
        // Windows 파일 이름은 대소문자를 구분하지 않으므로 id 비교도 대소문자 무시
        List<MeshEntry> library = LoadLibrary();
        MeshEntry existing = library.FirstOrDefault(e => string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase));
        bool addEntry = existing == null;
        if (existing != null && existing.meshPath != meshPath)
        {
            Report(MessageType.Error, $"id '{id}'는 이미 다른 모델({existing.meshPath})에 쓰이고 있습니다. 다른 id를 쓰세요.");
            return;
        }
        // saliency 결과 파일 이름은 OBJ 이름(= id)을 따른다. 다른 모델의 OBJ 이름과 같으면 그 모델 결과를 덮어쓴다.
        // (예: Venus의 OBJ는 Statue_v1_L2_Venus라서, 같은 OBJ를 id 그대로 추가하면 Venus 결과가 바뀜)
        MeshEntry sameResults = library.FirstOrDefault(e => e != existing && !string.IsNullOrEmpty(e.meshPath) &&
            string.Equals(Path.GetFileNameWithoutExtension(e.meshPath), id, StringComparison.OrdinalIgnoreCase));
        if (sameResults != null)
        {
            Report(MessageType.Error, $"id '{id}'로 만들면 saliency 결과 파일 이름이 '{sameResults.displayName}' 모델과 같아져 " +
                                      "그 모델의 결과를 덮어쓰게 됩니다. 다른 id를 쓰세요.");
            return;
        }
        if ((existing != null || Directory.Exists(destDir)) &&
            !EditorUtility.DisplayDialog("새 모델 추가", $"'{id}'가 이미 있습니다. 파일을 덮어쓸까요?" +
                                          (existing != null ? "\n(라이브러리 항목은 그대로 둡니다)" : ""), "덮어쓰기", "취소"))
            return;

        try
        {
            Directory.CreateDirectory(destDir);
            CopyModel(destDir);
            string animatedPath = CopySequence();
            AssetDatabase.Refresh();

            string objAsset = $"Assets/Resources/{meshPath}.obj";
            EnableReadWrite(objAsset);
            VerifyModel(meshPath);

            if (addEntry) AddLibraryEntry(meshPath, animatedPath);
            else Report(MessageType.Info, "라이브러리 항목은 이미 있어서 그대로 두었습니다.");

            string objFull = Path.Combine(destDir, id + ".obj");
            if (runSaliency) StartSaliency(objFull);
            else ReportExistingResults(objFull);
        }
        catch (Exception e)
        {
            Report(MessageType.Error, $"실패: {e.Message}");
            Debug.LogException(e);
        }
    }

    // OBJ, MTL, 텍스처 복사와 텍스처 연결
    private void CopyModel(string destDir)
    {
        string srcDir = Path.GetDirectoryName(objSource);
        List<string> obj = File.ReadAllLines(objSource).ToList();
        string mtlName = id + ".obj.mtl";
        string srcMtl = FindMtl(objSource, obj);

        List<string> materials = obj.Where(l => l.StartsWith("usemtl ")).Select(l => l.Substring(7).Trim()).Distinct().ToList();
        List<string> mtl;
        if (srcMtl != null)
        {
            mtl = File.ReadAllLines(srcMtl).ToList();
            CopyMtlTextures(mtl, Path.GetDirectoryName(srcMtl), destDir);
            List<string> defined = mtl.Where(l => l.TrimStart().StartsWith("newmtl ")).Select(l => l.Trim().Substring(7).Trim()).ToList();

            // MTL에 텍스처가 없는데 이미지를 골랐으면 모든 재질에 넣는다
            bool hasDiffuse = mtl.Any(l => l.TrimStart().StartsWith("map_Kd "));
            if (!hasDiffuse && File.Exists(textureSource))
            {
                string tex = CopyTexture(textureSource, destDir);
                for (int i = mtl.Count - 1; i >= 0; i--)
                    if (mtl[i].TrimStart().StartsWith("newmtl ")) mtl.Insert(i + 1, "map_Kd " + tex);
                Report(MessageType.Info, $"MTL에 텍스처가 없어 {tex}를 넣었습니다.");
            }
            if (defined.Count == 0) // 재질 정의가 없는 MTL
            {
                mtl.Add("newmtl " + id + "Mat");
                defined.Add(id + "Mat");
            }
            if (materials.Count == 0) InsertUseMtl(obj, defined[0]);
            Report(MessageType.Info, $"MTL 복사: {Path.GetFileName(srcMtl)} → {mtlName}");
        }
        else if (File.Exists(textureSource))
        {
            // TheRock처럼 MTL도 재질 지정도 없는 OBJ: MTL을 만들고 연결
            string tex = CopyTexture(textureSource, destDir);
            if (materials.Count == 0)
            {
                materials.Add(id + "Mat");
                InsertUseMtl(obj, materials[0]);
            }
            mtl = new List<string> { $"# {id} material (모델 추가 도구가 만듦)" };
            foreach (string m in materials)
                mtl.AddRange(new[] { "newmtl " + m, "Ka 0.200000 0.200000 0.200000", "Kd 1.000000 1.000000 1.000000",
                                     "Ks 0.000000 0.000000 0.000000", "illum 1", "map_Kd " + tex, "" });
            Report(MessageType.Info, $"MTL이 없어 {mtlName}을 만들고 {tex}를 연결했습니다.");
        }
        else
        {
            mtl = null;
            Report(MessageType.Warning, "텍스처가 없습니다. 모델은 흰색으로 보이고, TexMesh saliency는 텍스처 없이 계산됩니다.");
        }

        // OBJ의 mtllib를 새 MTL 이름으로 (없으면 맨 앞 주석 다음에 추가)
        obj.RemoveAll(l => l.StartsWith("mtllib "));
        if (mtl != null)
        {
            int at = 0;
            while (at < obj.Count && (obj[at].StartsWith("#") || obj[at].Trim().Length == 0)) at++;
            obj.Insert(at, "mtllib " + mtlName);
            WriteText(Path.Combine(destDir, mtlName), mtl);
        }
        WriteText(Path.Combine(destDir, id + ".obj"), obj);

        int vertexCount = obj.Count(l => l.StartsWith("v "));
        Report(MessageType.Info, $"OBJ 복사: {Path.GetFileName(objSource)} → Resources/Meshes/{id}/{id}.obj (버텍스 {vertexCount}개)");
    }

    // 첫 면(f) 앞에 usemtl을 넣는다 (재질 지정이 없으면 Unity가 MTL 텍스처를 연결하지 않음)
    private static void InsertUseMtl(List<string> obj, string material)
    {
        int firstFace = obj.FindIndex(l => l.StartsWith("f "));
        obj.Insert(firstFace < 0 ? obj.Count : firstFace, "usemtl " + material);
    }

    // MTL의 텍스처 줄(map_Kd 등)을 찾아 파일을 복사하고, 경로를 파일 이름만 남긴다.
    private void CopyMtlTextures(List<string> mtl, string mtlDir, string destDir)
    {
        for (int i = 0; i < mtl.Count; i++)
        {
            if (!TryParseMap(mtl[i], out string keyword, out string options, out string file)) continue;
            string src = ResolveFile(mtlDir, file);
            if (src == null && keyword == "map_Kd" && File.Exists(textureSource))
            {
                src = textureSource; // MTL이 가리키는 파일이 없으면 고른(또는 찾은) 이미지로 대신
                Report(MessageType.Warning, $"MTL의 텍스처 {file}가 없어 {Path.GetFileName(src)}로 바꿨습니다.");
            }
            if (src == null)
            {
                Report(MessageType.Warning, $"MTL의 텍스처를 찾지 못했습니다: {file}");
                continue;
            }
            string copied = CopyTexture(src, destDir);
            mtl[i] = $"{keyword} {options}{copied}";
        }
    }

    // "map_Kd -s 1 1 1 tex.png" → keyword, 옵션("-s 1 1 1 "), 파일
    private static bool TryParseMap(string line, out string keyword, out string options, out string file)
    {
        keyword = options = file = null;
        string trimmed = line.Trim();
        keyword = MapKeywords.FirstOrDefault(k => trimmed.StartsWith(k + " ") || trimmed.StartsWith(k + "\t"));
        if (keyword == null) return false;

        string rest = trimmed.Substring(keyword.Length).Trim();
        if (!rest.StartsWith("-")) { options = ""; file = rest; }
        else
        {
            // 옵션이 있으면 마지막 토큰이 파일 (옵션 값 개수가 옵션마다 달라서)
            int lastSpace = rest.LastIndexOf(' ');
            options = rest.Substring(0, lastSpace + 1);
            file = rest.Substring(lastSpace + 1);
        }
        return file.Length > 0;
    }

    private string CopyTexture(string src, string destDir)
    {
        string name = Path.GetFileName(src);
        string dest = Path.Combine(destDir, name);
        if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            File.Copy(src, dest, true);
        return name;
    }

    private static void WriteText(string path, List<string> lines)
    {
        // BOM 없이: OBJ 첫 줄이 "v ..."일 때 BOM이 붙으면 Python 쪽 파서가 첫 버텍스를 놓칠 수 있음
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
    }

    // ------------------------------------------------------------------ MTL/텍스처 찾기 (bridge_tex.py와 같은 순서)
    private static string FindMtl(string objPath, IEnumerable<string> objLines)
    {
        string dir = Path.GetDirectoryName(objPath);
        string stem = Path.GetFileNameWithoutExtension(objPath);
        var candidates = objLines.Where(l => l.StartsWith("mtllib ")).Select(l => l.Substring(7).Trim()).ToList();
        candidates.Add(stem + ".mtl");
        candidates.Add(Path.GetFileName(objPath) + ".mtl");
        foreach (string c in candidates)
        {
            string found = ResolveFile(dir, c);
            if (found != null) return found;
        }
        return null;
    }

    private static string FirstDiffuseTexture(string mtlPath)
    {
        foreach (string line in File.ReadLines(mtlPath))
            if (TryParseMap(line, out string keyword, out _, out string file) && keyword == "map_Kd")
                return ResolveFile(Path.GetDirectoryName(mtlPath), file);
        return null;
    }

    // MTL이 없을 때: OBJ와 이름이 같은 이미지, 아니면 폴더에 이미지가 하나뿐이면 그것
    private static string GuessTexture(string objPath)
    {
        string dir = Path.GetDirectoryName(objPath);
        string stem = Path.GetFileNameWithoutExtension(objPath);
        string[] images = Directory.GetFiles(dir).Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
        string same = images.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
        if (same != null) return same;
        return images.Length == 1 ? images[0] : null;
    }

    // 상대 경로 그대로, 안 되면 파일 이름만으로 (대소문자 무시) 찾는다
    private static string ResolveFile(string dir, string file)
    {
        file = file.Trim().Trim('"').Replace('\\', '/');
        if (file.StartsWith("./")) file = file.Substring(2);
        string direct = Path.IsPathRooted(file) ? file : Path.Combine(dir, file);
        if (File.Exists(direct)) return direct;

        string name = Path.GetFileName(file);
        foreach (string searchDir in new[] { Path.GetDirectoryName(direct), dir }.Distinct())
        {
            if (!Directory.Exists(searchDir)) continue;
            string match = Directory.GetFiles(searchDir).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return null;
    }

    // ------------------------------------------------------------------ 임포트 설정과 확인
    private void EnableReadWrite(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null) throw new Exception($"Unity가 모델을 임포트하지 못했습니다: {assetPath}");
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        Report(MessageType.Info, "메쉬 Read/Write 켬");
    }

    // 실행할 때와 같은 방식으로 불러와서 메쉬와 텍스처가 제대로 붙었는지 본다
    private void VerifyModel(string meshPath)
    {
        GameObject prefab = Resources.Load<GameObject>(meshPath);
        if (prefab == null) throw new Exception($"Resources.Load로 모델을 불러오지 못했습니다: {meshPath}");

        MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        int vertices = filters.Where(f => f.sharedMesh != null).Sum(f => f.sharedMesh.vertexCount);
        bool readable = filters.All(f => f.sharedMesh == null || f.sharedMesh.isReadable);
        bool textured = prefab.GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials).Any(m => m != null && m.mainTexture != null);

        Report(readable ? MessageType.Info : MessageType.Error,
            $"Unity 메쉬 {filters.Length}개, 버텍스 {vertices}개, Read/Write {(readable ? "켜짐" : "꺼짐")}");
        if (File.Exists(textureSource) || textureFromMtl)
            Report(textured ? MessageType.Info : MessageType.Warning,
                textured ? "텍스처가 재질에 연결됐습니다." : "텍스처를 지정했지만 재질에 연결되지 않았습니다. MTL을 확인하세요.");
    }

    // ------------------------------------------------------------------ 4D 텍스처
    // 복사하면 Resources/4D_Textures/<id>/ 규칙으로 자동 인식되므로 경로를 따로 적지 않는다(null).
    private string CopySequence()
    {
        if (string.IsNullOrWhiteSpace(sequenceSource)) return null;
        if (!Directory.Exists(sequenceSource))
        {
            Report(MessageType.Warning, $"4D 텍스처 폴더가 없습니다: {sequenceSource}");
            return null;
        }
        string[] frames = Directory.GetFiles(sequenceSource).Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
        if (frames.Length == 0)
        {
            Report(MessageType.Warning, "4D 텍스처 폴더에 이미지가 없습니다.");
            return null;
        }
        if (!copySequence)
        {
            Report(MessageType.Info, $"4D 텍스처: {frames.Length}장, 원래 폴더를 직접 참조");
            return sequenceSource.Replace('\\', '/');
        }

        string dest = Path.Combine(ResourcesDir, "4D_Textures", id);
        Directory.CreateDirectory(dest);
        foreach (string f in frames) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        Report(MessageType.Info, $"4D 텍스처: {frames.Length}장을 Resources/4D_Textures/{id}/로 복사");
        return null;
    }

    // ------------------------------------------------------------------ 라이브러리
    private static List<MeshEntry> LoadLibrary()
    {
        if (!File.Exists(LibraryPath)) return new List<MeshEntry>();
        MeshLibrary lib = JsonUtility.FromJson<MeshLibrary>(File.ReadAllText(LibraryPath));
        return lib?.entries ?? new List<MeshEntry>();
    }

    // 손으로 정리된 파일 모양을 유지하려고 마지막 항목 뒤에 글자로 끼워 넣는다
    private void AddLibraryEntry(string meshPath, string animatedPath)
    {
        string text = File.ReadAllText(LibraryPath);
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var fields = new List<string>
        {
            $"\"id\": \"{Escape(id)}\"",
            $"\"displayName\": \"{Escape(displayName.Trim())}\"",
            "\"isBuiltIn\": true",
            $"\"meshPath\": \"{meshPath}\"",
            "\"cfsDataPath\": \"\"",
            "\"texDataPath\": \"\"",
        };
        if (animatedPath != null) fields.Add($"\"animatedTexturePath\": \"{Escape(animatedPath)}\"");
        string entry = "        {" + nl + string.Join("," + nl, fields.Select(f => "            " + f)) + nl + "        }";

        int close = text.LastIndexOf(']');
        int lastBrace = close < 0 ? -1 : text.LastIndexOf('}', close);
        int open = text.IndexOf('[');
        if (close < 0 || open < 0) throw new Exception("DefaultLibrary.txt 형식을 읽지 못했습니다.");
        string updated = lastBrace > open
            ? text.Insert(lastBrace + 1, "," + nl + entry)
            : text.Insert(open + 1, nl + entry + nl);

        // 쓰기 전에 실제로 읽히는지 확인
        MeshLibrary check = JsonUtility.FromJson<MeshLibrary>(updated);
        if (check == null || check.entries.All(e => e.id != id)) throw new Exception("라이브러리 항목을 추가한 결과를 읽지 못했습니다.");

        File.WriteAllText(LibraryPath, updated, new UTF8Encoding(false));
        AssetDatabase.ImportAsset("Assets/Resources/DefaultLibrary.txt");
        Report(MessageType.Info, $"라이브러리에 '{displayName}' 추가 (모델 목록 맨 아래)");
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ------------------------------------------------------------------ saliency
    private void StartSaliency(string objFull)
    {
        var manager = FindObjectOfType<PythonProcessManager>();
        job = SaliencyJob.Start(objFull, Path.Combine(ResourcesDir, "Result_CfS"), Path.Combine(ResourcesDir, "Result_Tex"),
                                true, true, PythonProcessManager.SettingsFrom(manager));
        EditorApplication.update -= PollJob;
        EditorApplication.update += PollJob;
        Report(MessageType.Info, "saliency 계산 시작 (CfS-CNN → TexMesh). 에디터는 계속 쓸 수 있습니다.");
    }

    private void PollJob()
    {
        if (job == null) { EditorApplication.update -= PollJob; return; }
        if (!job.IsDone)
        {
            if (EditorApplication.timeSinceStartup - lastRepaint > 0.2) { lastRepaint = EditorApplication.timeSinceStartup; Repaint(); }
            return;
        }

        EditorApplication.update -= PollJob;
        int objVertices = File.ReadLines(job.objPath).Count(l => l.StartsWith("v "));
        ReportResult("CfS-CNN", job.CfsSucceeded, job.CfsResultPath, objVertices);
        ReportResult("TexMesh", job.TexSucceeded, job.TexResultPath, objVertices);
        Report(MessageType.Info, $"saliency 계산 끝 ({job.ElapsedSeconds:F0}초). 자세한 출력은 콘솔에 있습니다.");
        job = null;
        AssetDatabase.Refresh();
        Repaint();
    }

    private void ReportResult(string label, bool ok, string path, int objVertices)
    {
        if (!ok)
        {
            Report(MessageType.Error, $"{label} 계산 실패. 콘솔의 Python 출력을 확인하세요.");
            return;
        }
        int values = File.ReadLines(path).Count(l => l.Trim().Length > 0);
        Report(values == objVertices ? MessageType.Info : MessageType.Warning,
            $"{label}: {Path.GetFileName(path)} (값 {values}개, OBJ 버텍스 {objVertices}개)");
    }

    // 계산하지 않을 때: 같은 이름의 예전 결과가 있으면 그게 그대로 쓰이므로 알린다
    private void ReportExistingResults(string objFull)
    {
        string stem = Path.GetFileNameWithoutExtension(objFull);
        string cfs = Path.Combine(ResourcesDir, "Result_CfS", stem + "_saliency.txt");
        string tex = Path.Combine(ResourcesDir, "Result_Tex", stem + "_vertex_saliency.txt");
        if (File.Exists(cfs) || File.Exists(tex))
            Report(MessageType.Warning, "같은 이름의 saliency 결과가 이미 있어 그대로 쓰입니다. 모델이 바뀌었다면 saliency 계산을 켜고 다시 추가하세요.");
        else
            Report(MessageType.Info, "saliency는 플레이에서 이 모델을 처음 불러올 때 백그라운드로 계산됩니다.");
    }

    private void Report(MessageType type, string text)
    {
        report.Add((type, text));
        if (type == MessageType.Error) Debug.LogError("[모델 추가] " + text);
        else if (type == MessageType.Warning) Debug.LogWarning("[모델 추가] " + text);
        else Debug.Log("[모델 추가] " + text);
    }
}
