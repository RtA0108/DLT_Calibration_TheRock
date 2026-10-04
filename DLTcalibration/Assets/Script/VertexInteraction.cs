using UnityEngine;

// 버텍스 구의 선택 표시(색상)만 담당한다.
// 선택 / 마커 생성 / 2D-3D 데이터 저장은 VertexClickTest가 슬롯 단위로 한 곳에서 관리한다.
// (예전에는 여기와 VertexClickTest가 서로 다른 인덱스로 데이터를 써서, 선택 해제 후 짝이 어긋났음)
public class VertexInteraction : MonoBehaviour
{
    // [설정] 새로 생성할 Mesh Prefab (필요 시)
    public GameObject newMeshPrefab;

    private Color originalColor;
    private new Renderer renderer;

    void Awake()
    {
        renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            originalColor = renderer.material.color;
        }
    }

    public void SetSelected(bool selected)
    {
        if (renderer == null) return;
        renderer.material.color = selected ? Color.red : originalColor;
    }
}

//using System;
//using System.Collections;
//using System.Collections.Generic;
//using Unity.VisualScripting;
//using UnityEngine;
//using UnityEngine.UI;

//public class VertexInteraction : MonoBehaviour
//{
//    public GameObject newMeshPrefab;
//    //새로 생성된 Vertex의 screenCoord를 지속적으로 저장 (마우스 위치가 아니라 sphere의 위치로 저장해야 함)
//    public Dictionary<int, Vector2> screenCoord = new Dictionary<int, Vector2>();

//    private GameObject createdMesh;
//    private GameObject LVManger;
//    private Color originalColor;
//    private new Renderer renderer;
//    private static int meshCounter = 0;
//    private int meshIndex = 0;
//    private bool copied = false;
//    public Camera mainCam;
//    private GameObject markerManager;
//    void Start()
//    {
//        Camera cam = GameObject.FindGameObjectWithTag("MainCamera").gameObject.GetComponent<Camera>();
//        mainCam = cam;
//        // Get the renderer component to access the material color
//        renderer = GetComponent<Renderer>();
//        // markerManager = GameObject.Find("MarkerManager");
//        markerManager = GameObject.Find("CanvasUI");
//        if (markerManager == null)
//        {
//            Debug.LogError("MarkerManager를 찾을 수 없습니다. 씬에 MarkerManager가 존재하는지 확인하세요.");
//        }
//        // Store the original color
//        originalColor = renderer.material.color;
//        LVManger = GameObject.Find("LevelManager");
//    }
//    private void OnMouseDown()
//    {
//        if (markerManager == null)
//        {
//            Debug.LogError("MarkerManager is not assigned.");
//            return;
//        }

//        renderer.material.color = renderer.material.color == originalColor ? Color.red : originalColor;
//        Debug.Log(this.transform.position);
//        if (!copied){

//            meshIndex = LVManger.GetComponent<VertexClickTest>().arrayIndex;
//            Vector2 screenCoordMarker = new Vector2(mainCam.WorldToScreenPoint(this.transform.position).x, mainCam.WorldToScreenPoint(this.transform.position).y);
//            Debug.Log("interaction"+screenCoordMarker);
//            markerManager.GetComponent<MarkerManager>().CreateMarker(screenCoordMarker);
//            //마커 2D 추가
//            LVManger.GetComponent<VertexClickTest>().verticesStruct[meshIndex].screenCoordinate = screenCoordMarker;
//            LVManger.GetComponent<VertexClickTest>().verticesStruct[meshIndex].screenCoordinateGT = screenCoordMarker;
//            meshCounter++;
//            copied = true;
//        }

//    }


//}