using UnityEngine;

namespace Yoegoe.UI
{
    /// <summary>
    /// 고정 동작(파라미터 없음)으로 화면/팝업을 여는 버튼에 직접 붙이는 셀프와이어링 컴포넌트.
    /// Button.OnClick()이 Inspector에서 이 컴포넌트의 Invoke()를 가리키도록 프리팹에 영구 직렬화되므로,
    /// 부모 스크린 스크립트의 SerializeField/WireRuntimeListeners 수정 없이 버튼을 추가/이동할 수 있다.
    /// </summary>
    public enum UiNavigationTarget
    {
        ShopScreen,
        YutScreen,
    }

    public class UiNavigationButton : MonoBehaviour
    {
        [SerializeField] UiNavigationTarget target;

        public void Invoke()
        {
            switch (target)
            {
                case UiNavigationTarget.ShopScreen:
                    ShopScreen.Instance?.Open();
                    break;
                case UiNavigationTarget.YutScreen:
                    YutScreen.Instance?.Open();
                    break;
            }
        }
    }
}
