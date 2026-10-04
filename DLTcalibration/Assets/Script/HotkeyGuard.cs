using UnityEngine;
using UnityEngine.EventSystems;

// 입력칸(스케일/회전 숫자 등)에 글자를 치는 중에는 단축키가 동작하지 않게 한다.
// (예전에는 스케일 입력칸에 "1"을 치면 캘리브레이션 모드가 같이 켜지고 꺼졌음)
public static class HotkeyGuard
{
    public static bool Blocked
    {
        get
        {
            EventSystem es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            GameObject selected = es.currentSelectedGameObject;

            var tmpInput = selected.GetComponent<TMPro.TMP_InputField>();
            if (tmpInput != null && tmpInput.isFocused) return true;
            var legacyInput = selected.GetComponent<UnityEngine.UI.InputField>();
            return legacyInput != null && legacyInput.isFocused;
        }
    }
}
