using System.Collections.Generic;

namespace Yoegoe.Core
{
    /// <summary>
    /// 풀스크린 UI(상점·윷놀이·공양간·출석·상세 등)가 떠 있는 동안 맵 입력(MapPointerRouter)을
    /// 막기 위한 등록부. 화면이 Open/Close에서 스스로 Register/Unregister하면 되므로,
    /// MapPointerRouter가 화면 타입마다 하드코딩해서 알 필요가 없고 프리팹의
    /// raycastTarget 설정에만 기대지도 않는다 (ShopScreen에서 raycastTarget이 꺼져 있어도
    /// 맵 입력을 막지 못했던 사고 — Docs 참고).
    /// </summary>
    public static class UiBlockGate
    {
        static readonly HashSet<object> openScreens = new HashSet<object>();

        public static bool IsBlocking => openScreens.Count > 0;

        public static void Register(object screen)
        {
            if (screen != null) openScreens.Add(screen);
        }

        public static void Unregister(object screen)
        {
            if (screen != null) openScreens.Remove(screen);
        }
    }
}
