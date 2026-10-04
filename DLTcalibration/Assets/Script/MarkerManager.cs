using UnityEngine;

public class MarkerManager : MonoBehaviour
{

    public RectTransform canvasRectTransform; // Canvas의 RectTransform
    public GameObject markerPrefab; // 마커로 사용할 프리팹

    //Canvas Plane Distance를 object와 Camera의 거리 중간값으로 설정?
    public void Start()
    {
        if (canvasRectTransform == null) Debug.LogError("No Rect Transform");
    }

    // slot: VertexClickTest의 슬롯 인덱스. 마커 번호는 slot + 1로 표시된다.
    // screenPosition: cam 기준 스크린 픽셀 좌표 (좌하단 원점)
    public Marker CreateMarker(int slot, Vector2 screenPosition, Camera cam, VertexClickTest owner)
    {
        if (markerPrefab == null)
        {
            Debug.LogError("Marker prefab is not assigned.");
            return null;
        }

        if (canvasRectTransform == null)
        {
            Debug.LogError("No Rect Transform");
            return null;
        }

        // 마커 생성
        GameObject newMarker = Instantiate(markerPrefab, canvasRectTransform);
        newMarker.name = "MarkerUI " + (slot + 1);
        // 마커 설정
        Marker markerScript = newMarker.GetComponent<Marker>();
        markerScript.SetMarker(slot, screenPosition, canvasRectTransform, cam, owner);
        Debug.Log("Marker Start Point" + screenPosition);
        return markerScript;
    }
}
