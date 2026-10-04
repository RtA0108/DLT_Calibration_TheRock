using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CreateSphereAtVertex : MonoBehaviour
{
    [Header("Settings")]
    public GameObject vertexSpherePrefab;

    // ★ [삭제됨] targetTag 변수 삭제
    // (이 변수 때문에 프리팹의 태그가 덮어씌워지고 있었습니다)

    // 화면에 보여질 구체 크기 (고정값)
    public float sphereFixedScale = 0.1f;

    [Header("Container")]
    public Transform sphereHolder;

    [Header("Debug Info")]
    public int vertexCount;
    public int indexNumber;

    public Dictionary<int, Vector3> posIndex;

    private List<Transform> _activeSpheres = new List<Transform>();
    private Vector3 _lastLossyScale;
    private Transform _targetMeshTransform;

    public void GenerateSpheres(GameObject targetMeshObj)
    {
        ClearAllSpheres();

        if (targetMeshObj == null) return;
        _targetMeshTransform = targetMeshObj.transform;

        MeshFilter[] meshFilters = targetMeshObj.GetComponentsInChildren<MeshFilter>();
        if (meshFilters == null || meshFilters.Length == 0) return;

        EnsureHolderExists();

        sphereHolder.SetParent(_targetMeshTransform);
        sphereHolder.localPosition = Vector3.zero;
        sphereHolder.localRotation = Quaternion.identity;
        sphereHolder.localScale = Vector3.one;

        posIndex = new Dictionary<int, Vector3>();
        _activeSpheres = new List<Transform>();
        _sphereByLocal.Clear();
        indexNumber = 0;
        var sphereByWorld = new Dictionary<Vector3, Transform>();

        Vector3 currentScale = CalculateInverseScale();

        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.sharedMesh == null) continue;

            Mesh mesh = mf.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Transform meshTrans = mf.transform;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = meshTrans.TransformPoint(vertices[i]);

                // 같은 위치의 버텍스(UV 이음새 등)는 구 하나를 같이 쓴다
                if (sphereByWorld.TryGetValue(worldPos, out Transform existing))
                {
                    _sphereByLocal[vertices[i]] = existing;
                    continue;
                }

                indexNumber++;
                posIndex.Add(indexNumber, worldPos);

                // 구체 생성 (이때 프리팹의 Tag가 그대로 복사됨)
                GameObject sphere = Instantiate(vertexSpherePrefab, worldPos, Quaternion.identity);
                sphere.transform.SetParent(sphereHolder);

                // 스케일 적용
                sphere.transform.localScale = currentScale;

                sphere.name = $"Vertex_{indexNumber}";

                // ★ [수정됨] sphere.tag = targetTag; 삭제함!
                // 이제 프리팹에 설정된 태그를 그대로 사용합니다.

                int layer = LayerMask.NameToLayer("Vertex In 3D");
                if (layer != -1) sphere.layer = layer;

                _activeSpheres.Add(sphere.transform);
                sphereByWorld[worldPos] = sphere.transform;
                _sphereByLocal[vertices[i]] = sphere.transform;
            }
        }

        _lastLossyScale = sphereHolder.lossyScale;

        Debug.Log($"[SphereGenerator] 생성 완료: {indexNumber}개.");
    }

    void Update()
    {
        if (sphereHolder == null || _activeSpheres.Count == 0) return;

        Vector3 currentLossyScale = sphereHolder.lossyScale;

        if (Vector3.Distance(currentLossyScale, _lastLossyScale) > 0.0001f)
        {
            UpdateSphereScales();
            _lastLossyScale = currentLossyScale;
        }
    }

    void UpdateSphereScales()
    {
        Vector3 newScale = CalculateInverseScale();
        for (int i = 0; i < _activeSpheres.Count; i++)
        {
            if (_activeSpheres[i] != null)
            {
                _activeSpheres[i].localScale = newScale;
            }
        }
    }

    Vector3 CalculateInverseScale()
    {
        if (sphereHolder == null) return Vector3.one * sphereFixedScale;

        Vector3 parentScale = sphereHolder.lossyScale;

        float sx = Mathf.Abs(parentScale.x) < 0.0001f ? 1f : parentScale.x;
        float sy = Mathf.Abs(parentScale.y) < 0.0001f ? 1f : parentScale.y;
        float sz = Mathf.Abs(parentScale.z) < 0.0001f ? 1f : parentScale.z;

        return new Vector3(
            sphereFixedScale / sx,
            sphereFixedScale / sy,
            sphereFixedScale / sz
        );
    }

    public void ClearAllSpheres()
    {
        if (sphereHolder != null)
        {
            foreach (Transform child in sphereHolder) Destroy(child.gameObject);
            sphereHolder.SetParent(null);
            sphereHolder.localScale = Vector3.one;
        }

        if (posIndex != null) posIndex.Clear();
        if (_activeSpheres != null) _activeSpheres.Clear();
        _sphereByLocal.Clear();

        indexNumber = 0;
        vertexCount = 0;
        _targetMeshTransform = null;
    }

    private void EnsureHolderExists()
    {
        if (sphereHolder == null)
        {
            GameObject holder = new GameObject("Generated_Spheres_Holder");
            sphereHolder = holder.transform;
        }
    }

    public IReadOnlyList<Transform> ActiveSpheres => _activeSpheres;

    // 메쉬 버텍스(로컬 좌표) -> 그 위치의 구. 조작 화면에서 버텍스를 고를 때 구 수만 개를 다 훑지 않게.
    private readonly Dictionary<Vector3, Transform> _sphereByLocal = new Dictionary<Vector3, Transform>();

    public GameObject FindSphereForVertex(Vector3 localPos, Vector3 worldPos)
    {
        // 메쉬가 여러 개라 로컬 좌표가 우연히 겹치는 경우를 대비해 실제 위치도 확인하고, 아니면 전체에서 찾는다
        if (_sphereByLocal.TryGetValue(localPos, out Transform sphere) && sphere != null
            && (sphere.position - worldPos).sqrMagnitude < 0.01f)
            return sphere.gameObject;
        return FindSphereAt(worldPos, 0.1f);
    }

    // worldPos에 가장 가까운 버텍스 구를 찾는다. tolerance보다 멀리 있으면 null.
    public GameObject FindSphereAt(Vector3 worldPos, float tolerance)
    {
        Transform best = null;
        float bestSqr = tolerance * tolerance;
        foreach (Transform sphere in _activeSpheres)
        {
            if (sphere == null) continue;
            float sqr = (sphere.position - worldPos).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = sphere;
            }
        }
        return best != null ? best.gameObject : null;
    }
}

//using System.Collections;
//using System.Collections.Generic;
//using System.Linq;
//using UnityEngine;

//public class CreateSphereAtVertex : MonoBehaviour
//{
//    [Header("Settings")]
//    public GameObject vertexSpherePrefab; // 생성할 구체 프리팹
//    public string targetTag = "something"; // 탐색할 오브젝트 태그

//    [Header("Container")]
//    public Transform sphereHolder; // 생성된 구체들을 담을 부모 (MainController에 연결됨)

//    [Header("Debug Info")]
//    public int vertexCount;
//    public int indexNumber;

//    // 생성된 위치를 저장하여 중복 생성을 방지하는 딕셔너리
//    public Dictionary<int, Vector3> posIndex;

//    private Quaternion originalRotation;

//    void Start()
//    {
//        // 1. Sphere Holder가 연결되지 않았으면 자동으로 생성 (안전장치)
//        if (sphereHolder == null)
//        {
//            GameObject holderObj = new GameObject("Generated_Spheres_Holder");
//            sphereHolder = holderObj.transform;
//        }

//        // 2. 태그로 대상 오브젝트 찾기
//        GameObject[] objects = GameObject.FindGameObjectsWithTag(targetTag);

//        indexNumber = 0;
//        posIndex = new Dictionary<int, Vector3>();
//        if (objects == null || objects.Length == 0)
//        {
//            Debug.LogWarning($"[CreateSphereAtVertex] Tag가 '{targetTag}'인 오브젝트를 찾을 수 없습니다.");
//            return;
//        }

//        foreach (GameObject obj in objects)
//        {
//            MeshFilter mf = obj.GetComponent<MeshFilter>();

//            // MeshFilter나 Mesh가 없으면 스킵
//            if (mf == null || mf.sharedMesh == null) continue;

//            Mesh mesh = mf.sharedMesh;
//            vertexCount = mesh.vertices.Length;

//            // 3. 버텍스 순회하며 구체 생성
//            for (int i = 0; i < vertexCount; i++)
//            {
//                // 로컬 좌표 -> 월드 좌표 변환
//                Vector3 worldPos = obj.transform.TransformPoint(mesh.vertices[i]);

//                // 중복 위치 체크 (이미 생성된 위치면 스킵)
//                // *Tip: 중복 체크 로직이 조금 무거울 수 있으므로, Vertex가 너무 많으면 최적화 필요
//                if (posIndex.Any(x => x.Value == worldPos)) continue;

//                indexNumber++;
//                posIndex.Add(indexNumber, worldPos);

//                // 구체 생성
//                GameObject sphere = Instantiate(vertexSpherePrefab, worldPos, Quaternion.identity);

//                // [중요] 부모 설정 (Hierarchy 정리용)
//                sphere.transform.parent = sphereHolder;

//                sphere.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f);
//                sphere.name = $"Vertex_{indexNumber}";
//                sphere.layer = LayerMask.NameToLayer("Vertex In 3D"); // Raycast 무시용 레이어
//            }
//        }
//    }

//    void Update()
//    {
//        originalRotation = transform.rotation;
//    }
//}
//// Ensure the cube GameObject is assigned
//if (objects != null)
//        { 
//            foreach(GameObject obj in objects) 
//            {
//                // Get the mesh filter of the cube
//                // MeshFilter 내부엔 vertex와 uv정보가 모두 들어있음
//                MeshFilter cubeMeshFilter = obj.GetComponent<MeshFilter>();
//                posIndex = new Dictionary<int, Vector3>();
//                //Dictionary<Vector2, int> uvIndex = new Dictionary<Vector2, int>();
//                // Ensure the mesh filter is not null and has a mesh
//                if (cubeMeshFilter != null && cubeMeshFilter.sharedMesh != null)
//                {
//                    // Get the cube's mesh
//                    Mesh cubeMesh = cubeMeshFilter.sharedMesh;

//                    vertexCount = cubeMesh.vertices.Length;
//                    for(int i = 0; i<cubeMesh.vertices.Length; i++) {
//                        // Get the world position of the specified vertex
//                        Vector3 vertexPosition = obj.transform.TransformPoint(cubeMesh.vertices[i]);
//                        if(posIndex.FirstOrDefault(x => x.Value == vertexPosition).Key != 0){

//                            continue;
//                        }
//                        else{
//                            indexNumber++;
//                            posIndex.Add(indexNumber, vertexPosition);

//                        }

//                        //Vector2 uvCoordinate = cubeMesh.uv[i];
//                        //uvIndex.Add(uvCoordinate, indexNumber);


//                        GameObject sphere = Instantiate(vertexSphere, vertexPosition, Quaternion.identity);
//                        sphere.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f);
//                        sphere.name = "vertex" + indexNumber;

//                        // Assign layer to **IGNORE** it for raycasting
//                        sphere.layer = LayerMask.NameToLayer("Vertex In 3D");
//                    }
//                    //foreach (KeyValuePair <int, Vector3 > key in posIndex)
//                    //{
//                    //    Debug.Log("Key (Vertex Position): " + key.Value + "Number: " + key.Key);
//                    //}
//                    //UnityEngine.Object[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
//                    //foreach (GameObject vertexSphere in allObjects){
//                    //    //Debug.Log(somethingElse + "is an active object" + somethingElse.GetInstanceID());
//                    //    //vertex 위치 출력
//                    //    Debug.Log(vertexSphere.transform.position + " Name: " + vertexSphere.name);
//                    //}
//                }
//                // Optional: Attach the sphere to a parent object for organization
//                // sphere.transform.parent = cube.transform;

//            else
//            {
//                Debug.LogError("Mesh filter or mesh not found on the cube.");
//            }
//            }
//        }

//        else
//        {
//            Debug.LogError("Cube GameObject not assigned. Please assign the cube GameObject in the Inspector.");
//        }
//    }

//    void Update()
//    {
//        originalRotation = transform.rotation;
//    }
//}