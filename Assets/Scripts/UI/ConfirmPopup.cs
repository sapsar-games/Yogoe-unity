using System;
using UnityEngine;
using UnityEngine.UI;

namespace Yoegoe.UI
{
    /// <summary>
    /// 범용 예/아니오 확인 팝업. 셸은 Prefab(Assets/Prefabs/UI/ConfirmPopup.prefab) — 다른 화면(공양간 850)보다 위(900).
    /// 사용: <c>ConfirmPopup.Show("재료가 모자라는데도 요리할까요?", onYes)</c>. 바깥(어두운 배경)·아니오 = 취소.
    /// </summary>
    public class ConfirmPopup : SingletonBehaviour<ConfirmPopup>
    {
        [SerializeField] GameObject panel;
        [SerializeField] Text messageText;
        [SerializeField] Button yesButton;
        [SerializeField] Text yesLabel;
        [SerializeField] Button noButton;
        [SerializeField] Text noLabel;
        [SerializeField] Button dimButton;

        Action onYes;
        Action onNo;

        public bool IsOpen => panel != null && panel.activeSelf;

        protected override void Awake()
        {
            base.Awake();
            if (panel != null) panel.SetActive(false);
        }

        void Start()
        {
            UiButtonUtils.Bind(yesButton, () => Answer(true));
            UiButtonUtils.Bind(noButton, () => Answer(false));
            UiButtonUtils.Bind(dimButton, () => Answer(false));
        }

        /// <summary>팝업을 띄운다. 팝업이 없으면(씬 미배치) 묻지 않고 onYes를 바로 실행한다.</summary>
        public static void Show(string message, Action onYes, Action onNo = null, string yes = "예", string no = "아니오")
        {
            var p = Instance;
            if (p == null || p.panel == null)
            {
                Debug.LogWarning("[ConfirmPopup] Main 씬에 ConfirmPopup Prefab이 없어 확인 없이 진행합니다: " + message);
                onYes?.Invoke();
                return;
            }
            p.onYes = onYes;
            p.onNo = onNo;
            if (p.messageText != null) p.messageText.text = message;
            if (p.yesLabel != null) p.yesLabel.text = yes;
            if (p.noLabel != null) p.noLabel.text = no;
            p.panel.SetActive(true);
        }

        /// <summary>답 없이 닫기 (화면이 먼저 닫힐 때) — 콜백은 부르지 않는다.</summary>
        public static void Dismiss()
        {
            var p = Instance;
            if (p == null) return;
            p.onYes = null;
            p.onNo = null;
            if (p.panel != null) p.panel.SetActive(false);
        }

        void Answer(bool yes)
        {
            var action = yes ? onYes : onNo;
            onYes = null;
            onNo = null;
            if (panel != null) panel.SetActive(false);
            action?.Invoke();
        }
    }
}
