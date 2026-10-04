using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class RuntimeMeshLoader : MonoBehaviour
{
    public static RuntimeMeshLoader Instance;

    private const string LIBRARY_FILENAME = "DefaultLibrary";
    private Dictionary<string, MeshEntry> _libraryDict = new Dictionary<string, MeshEntry>();

    [Header("State")]
    public GameObject CurrentLoadedObject;
    public bool IsLoading = false;

    private Queue<MeshEntry> loadQueue = new Queue<MeshEntry>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        IsLoading = false;
        loadQueue.Clear();
        LoadDefaultLibrary();
    }

    private void LoadDefaultLibrary()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(LIBRARY_FILENAME);
        if (jsonFile)
        {
            MeshLibrary lib = JsonUtility.FromJson<MeshLibrary>(jsonFile.text);
            foreach (var e in lib.entries) if (!_libraryDict.ContainsKey(e.id)) _libraryDict.Add(e.id, e);
        }
    }

    public void LoadMeshByID(string id)
    {
        if (_libraryDict.ContainsKey(id)) { loadQueue.Enqueue(_libraryDict[id]); if (!IsLoading) LoadNext(); }
        else if (File.Exists(id) || id.Contains("/") || id.Contains("\\"))
        {
            loadQueue.Enqueue(new MeshEntry { id = "External", meshPath = id, isBuiltIn = false, initialScale = Vector3.one });
            if (!IsLoading) LoadNext();
        }
    }

    private void LoadNext() { if (loadQueue.Count > 0) StartCoroutine(LoadProcess(loadQueue.Dequeue())); }

    private IEnumerator LoadProcess(MeshEntry entry)
    {
        IsLoading = true;

        if (CurrentLoadedObject != null)
        {
            Destroy(CurrentLoadedObject);
            CurrentLoadedObject = null;
        }
        yield return null;

        GameObject loadedObj = null;

        try
        {
            // 1. 모델 로드
            if (entry.isBuiltIn)
            {
                GameObject prefab = Resources.Load<GameObject>(entry.meshPath);
                if (prefab) { loadedObj = Instantiate(prefab); loadedObj.name = prefab.name; }
            }
            else
            {
                string path = entry.meshPath;
                if (!File.Exists(path) && !Path.HasExtension(path)) path += ".obj";
                if (File.Exists(path)) { loadedObj = SimpleObjLoader.Load(path); entry.meshPath = path; }
            }

            if (loadedObj == null) throw new System.Exception("로드 실패");

            // 2. Transform 설정
            CurrentLoadedObject = loadedObj;
            CurrentLoadedObject.transform.position = Vector3.zero;
            CurrentLoadedObject.transform.localScale = entry.initialScale;
            CurrentLoadedObject.transform.localEulerAngles = entry.initialRotation;

            try { loadedObj.tag = "something"; }
            catch { }

            // 3. 자식 세팅
            int meshLayer = LayerMask.NameToLayer("Meshes");
            if (meshLayer == -1) meshLayer = LayerMask.NameToLayer("Default");

            loadedObj.layer = meshLayer;
            MeshFilter[] filters = loadedObj.GetComponentsInChildren<MeshFilter>();

            foreach (var mf in filters)
            {
                GameObject child = mf.gameObject;
                child.layer = meshLayer;

                if (child.GetComponent<Collider>() == null)
                {
                    var mc = child.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
            }

            // 4. 경로 준비 (★ CFSCNN 오류 해결 핵심)
            string objPath = entry.meshPath;
            string cfsPath = "", texPath = "";

            if (entry.isBuiltIn)
            {
                cfsPath = entry.cfsDataPath;
                texPath = entry.texDataPath;

                // ★ [수정] Resources 경로는 실제 파일 경로가 아님 ("Folder/File").
                // CFSCNN은 실제 .obj 파일의 디스크 경로를 원하므로, 유니티 프로젝트 경로를 조합해줌.
                // (주의: 에디터 환경 기준. 빌드 시에는 StreamingAssets 등을 사용해야 할 수 있음)

                string fullResourcePath = Path.Combine(Application.dataPath, "Resources", entry.meshPath + ".obj");
                if (File.Exists(fullResourcePath))
                {
                    objPath = fullResourcePath;
                }
                else
                {
                    // .obj가 없으면 .fbx일 수도 있음
                    string fbxPath = Path.Combine(Application.dataPath, "Resources", entry.meshPath + ".fbx");
                    if (File.Exists(fbxPath)) objPath = fbxPath;
                    else
                    {
                        // 그래도 없으면 일단 원본 문자열을 넘기되 경고
                        // (단순 프리팹만 있고 원본 모델파일이 Resources에 없으면 읽기 실패함)
                        Debug.LogWarning($"[Loader] Resources 원본 모델 파일을 찾을 수 없습니다: {fullResourcePath}");
                    }
                }
            }
            else
            {
                string dir = Path.GetDirectoryName(entry.meshPath);
                string name = Path.GetFileNameWithoutExtension(entry.meshPath);
                string cfsFile = Path.Combine(dir, name + "_saliency.txt");
                string texFile = Path.Combine(dir, name + "_vertex_saliency.txt");
                cfsPath = File.Exists(cfsFile) ? cfsFile : "";
                texPath = File.Exists(texFile) ? texFile : "";
            }

            // 모델별 4D 텍스처 (있으면 재생, 없으면 원래 텍스처).
            // 히트맵(SaliencyMapVisualizer.Init)이 원래 재질을 저장하기 전에 적용해야, 히트맵을 껐을 때
            // 재생 중인 재질로 돌아온다. (예전에는 원본 재질로 돌아가서 히트맵을 한 번 켜고 끄면 4D 재생이 멈췄음)
            if (TextureSequenceAnimator.Instance != null)
                TextureSequenceAnimator.Instance.Apply(loadedObj, entry);

            // 5. 컴포넌트 부착
            var calib = loadedObj.AddComponent<ProjectionMappingCalibrator>();
            calib.enabled = false;
            calib.SetPaths(objPath, cfsPath, texPath);
            calib.Init();

            LayerMask visibilityMask = 1 << meshLayer;
            var vis = loadedObj.AddComponent<SaliencyMapVisualizer>();
            vis.enabled = false;
            vis.mainCamera = Camera.main;
            vis.meshFilter = loadedObj.GetComponentInChildren<MeshFilter>(); // 안전장치
            vis.Init(objPath, cfsPath, objPath, texPath, visibilityMask);

            if (MainController.Instance != null)
            {
                MainController.Instance.currentEntry = entry;
                MainController.Instance.RegisterNewMesh(loadedObj, objPath, cfsPath, texPath);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Loader Error] {e.Message}");
            if (loadedObj != null) Destroy(loadedObj);
        }
        finally
        {
            IsLoading = false;
            LoadNext();
        }
    }
}
//using System.Collections;
//using System.Collections.Generic;
//using System.IO;
//using UnityEngine;

//public class RuntimeMeshLoader : MonoBehaviour
//{
//    public static RuntimeMeshLoader Instance;

//    private const string LIBRARY_FILENAME = "DefaultLibrary";
//    private Dictionary<string, MeshEntry> _libraryDict = new Dictionary<string, MeshEntry>();

//    [Header("State")]
//    public GameObject CurrentLoadedObject;
//    public bool IsLoading = false;

//    private Queue<MeshEntry> loadQueue = new Queue<MeshEntry>();

//    private void Awake()
//    {
//        if (Instance == null) Instance = this;
//        else Destroy(gameObject);

//        IsLoading = false;
//        loadQueue.Clear();
//        LoadDefaultLibrary();
//    }

//    private void LoadDefaultLibrary()
//    {
//        TextAsset jsonFile = Resources.Load<TextAsset>(LIBRARY_FILENAME);
//        if (jsonFile)
//        {
//            MeshLibrary lib = JsonUtility.FromJson<MeshLibrary>(jsonFile.text);
//            foreach (var e in lib.entries) if (!_libraryDict.ContainsKey(e.id)) _libraryDict.Add(e.id, e);
//        }
//    }

//    public void LoadMeshByID(string id)
//    {
//        if (_libraryDict.ContainsKey(id)) { loadQueue.Enqueue(_libraryDict[id]); if (!IsLoading) LoadNext(); }
//        else if (File.Exists(id) || id.Contains("/") || id.Contains("\\"))
//        {
//            loadQueue.Enqueue(new MeshEntry { id = "External", meshPath = id, isBuiltIn = false, initialScale = Vector3.one });
//            if (!IsLoading) LoadNext();
//        }
//    }

//    private void LoadNext() { if (loadQueue.Count > 0) StartCoroutine(LoadProcess(loadQueue.Dequeue())); }

//    private IEnumerator LoadProcess(MeshEntry entry)
//    {
//        IsLoading = true;

//        if (CurrentLoadedObject != null)
//        {
//            Destroy(CurrentLoadedObject);
//            CurrentLoadedObject = null;
//        }
//        yield return null;

//        GameObject loadedObj = null;

//        try
//        {
//            // 1. 모델 로드 (부모 껍데기 생성)
//            if (entry.isBuiltIn)
//            {
//                GameObject prefab = Resources.Load<GameObject>(entry.meshPath);
//                if (prefab) { loadedObj = Instantiate(prefab); loadedObj.name = prefab.name; }
//            }
//            else
//            {
//                string path = entry.meshPath;
//                if (!File.Exists(path) && !Path.HasExtension(path)) path += ".obj";
//                if (File.Exists(path)) { loadedObj = SimpleObjLoader.Load(path); entry.meshPath = path; }
//            }

//            if (loadedObj == null) throw new System.Exception("로드 실패");

//            // 2. Transform 설정 (부모 기준)
//            CurrentLoadedObject = loadedObj;
//            CurrentLoadedObject.transform.position = Vector3.zero;
//            CurrentLoadedObject.transform.localScale = entry.initialScale;
//            CurrentLoadedObject.transform.localEulerAngles = entry.initialRotation;

//            // =========================================================
//            // ★ [핵심 수정] 자식들까지 완벽하게 세팅 (Layer & Collider)
//            // =========================================================

//            int meshLayer = LayerMask.NameToLayer("Meshes");
//            if (meshLayer == -1) meshLayer = LayerMask.NameToLayer("Default");

//            // 부모 레이어 설정
//            loadedObj.layer = meshLayer;

//            // 자식들(실제 메쉬) 순회하며 세팅
//            MeshFilter[] filters = loadedObj.GetComponentsInChildren<MeshFilter>();

//            foreach (var mf in filters)
//            {
//                GameObject child = mf.gameObject;

//                // 1. 레이어 통일
//                child.layer = meshLayer;

//                // 2. MeshCollider 부착 (없으면)
//                if (child.GetComponent<Collider>() == null)
//                {
//                    var mc = child.AddComponent<MeshCollider>();
//                    mc.sharedMesh = mf.sharedMesh; // 메쉬 데이터 연결
//                }
//            }
//            // =========================================================

//            // 4. 경로 준비
//            string objPath = entry.meshPath;
//            string cfsPath = "", texPath = "";

//            if (entry.isBuiltIn)
//            {
//                cfsPath = entry.cfsDataPath; texPath = entry.texDataPath;
//            }
//            else
//            {
//                string dir = Path.GetDirectoryName(entry.meshPath);
//                string name = Path.GetFileNameWithoutExtension(entry.meshPath);
//                string cfsFile = Path.Combine(dir, name + "_saliency.txt");
//                string texFile = Path.Combine(dir, name + "_vertex_saliency.txt");

//                if (!File.Exists(cfsFile) || !File.Exists(texFile))
//                {
//                    if (PythonProcessManager.Instance != null)
//                        PythonProcessManager.Instance.RunSaliencyCalculation(entry.meshPath, "", dir);
//                }
//                cfsPath = File.Exists(cfsFile) ? cfsFile : "";
//                texPath = File.Exists(texFile) ? texFile : "";
//            }

//            // 5. 컴포넌트 부착
//            LayerMask visibilityMask = 1 << meshLayer;

//            // (A) Calibrator (부모에 부착)
//            var calib = loadedObj.AddComponent<ProjectionMappingCalibrator>();
//            calib.SetPaths(objPath, cfsPath, texPath);
//            calib.Init(); // 초기화 지시
//            calib.enabled = false;

//            // (B) Visualizer
//            var vis = loadedObj.AddComponent<SaliencyMapVisualizer>();
//            vis.enabled = false;
//            vis.meshFilter = loadedObj.GetComponentInChildren<MeshFilter>(); // 자식 필터 연결
//            vis.mainCamera = Camera.main;
//            vis.Init(objPath, cfsPath, objPath, texPath, visibilityMask);

//            // 6. MainController에 보고
//            if (MainController.Instance != null)
//            {
//                MainController.Instance.RegisterNewMesh(loadedObj, objPath, cfsPath, texPath);
//            }
//        }
//        catch (System.Exception e)
//        {
//            Debug.LogError($"[Loader Error] {e.Message}");
//            if (loadedObj != null) Destroy(loadedObj);
//        }
//        finally
//        {
//            IsLoading = false;
//            LoadNext();
//        }
//    }
//}