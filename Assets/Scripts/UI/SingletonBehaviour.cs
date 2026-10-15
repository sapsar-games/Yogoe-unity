using UnityEngine;

namespace Yoegoe.UI
{
    /// <summary>
    /// 씬에 하나만 존재하는 UI 팝업/스크린이 반복해서 구현하던
    /// "Awake에 Instance 등록, OnDestroy에 해제" 패턴을 공용화한 베이스.
    /// 추가 초기화/정리가 필요하면 Awake/OnDestroy를 override하고 base를 호출한다.
    /// </summary>
    public abstract class SingletonBehaviour<T> : MonoBehaviour where T : SingletonBehaviour<T>
    {
        public static T Instance { get; protected set; }

        protected virtual void Awake()
        {
            Instance = (T)this;
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
