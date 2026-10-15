using UnityEngine;
using UnityEngine.UI;

namespace Yoegoe.UI
{
    /// <summary>
    /// 버튼에 onClick 리스너를 (기존 리스너 제거 후) 다시 연결하는 공용 헬퍼.
    /// 여러 팝업/스크린이 각자 <c>Wire</c>/<c>BindButton</c>으로 복붙해 쓰던 걸 한 곳으로 모았다.
    /// </summary>
    public static class UiButtonUtils
    {
        public static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null || action == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        public static void Bind(Transform target, UnityEngine.Events.UnityAction action)
        {
            if (target == null) Bind((Button)null, action);
            else Bind(target.GetComponent<Button>(), action);
        }
    }
}
