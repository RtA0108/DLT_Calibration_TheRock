using System.Collections.Generic;
using UnityEngine; // [중요] Vector3를 쓰려면 이게 꼭 필요합니다!

[System.Serializable]
public class MeshEntry
{
    public string id;           // 고유 ID
    public string displayName;  // 이름
    public bool isBuiltIn;      // 내장 여부

    // ▼▼▼ [여기에 추가] 크기와 회전 변수 ▼▼▼
    public Vector3 initialScale = Vector3.one;      // 기본값 (1,1,1)
    public Vector3 initialRotation = Vector3.zero;  // 기본값 (0,0,0)
    // ▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲

    public string meshPath;     // 모델 경로
    public string cfsDataPath;  // 데이터 경로 1
    public string texDataPath;  // 데이터 경로 2

    // 4D 텍스처(이미지 시퀀스 또는 비디오). 비워 두면 Resources/4D_Textures/<id> 폴더가 있을 때 자동으로 사용.
    // Resources 기준 경로, StreamingAssets 비디오 경로 또는 디스크 절대 경로. 자세한 규칙은 TextureSequenceAnimator 참고.
    public string animatedTexturePath;
    public float animatedTextureFps;   // 0이면 기본값(24fps)

    // 패치 선 모드에서 이 각도(도)보다 크게 꺾인 모서리를 그림. 0이면 모서리 각도 분포로 자동 결정.
    public float patchCreaseAngle;
}

[System.Serializable]
public class MeshLibrary
{
    public List<MeshEntry> entries = new List<MeshEntry>();
}
