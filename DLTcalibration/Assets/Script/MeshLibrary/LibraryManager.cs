using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class LibraryManager : MonoBehaviour
{
    public static LibraryManager Instance;

    // MeshLibraryData.cs에 있는 MeshEntry를 갖다 씁니다.
    public List<MeshEntry> allMeshEntries = new List<MeshEntry>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        LoadLibrary();
    }

    public void LoadLibrary()
    {
        allMeshEntries.Clear();

        // Resources/DefaultLibrary.json 읽기
        TextAsset defaultJson = Resources.Load<TextAsset>("DefaultLibrary");

        if (defaultJson != null)
        {
            // MeshLibraryData.cs에 있는 MeshLibrary 클래스 사용
            MeshLibrary lib = JsonUtility.FromJson<MeshLibrary>(defaultJson.text);
            if (lib != null)
            {
                allMeshEntries.AddRange(lib.entries);
                Debug.Log($"[Library] JSON 로드 완료: {lib.entries.Count}개 모델 발견.");
            }
        }
        else
        {
            Debug.LogError("[Library] Resources 폴더에 'DefaultLibrary.json'이 없습니다!");
        }
    }
}