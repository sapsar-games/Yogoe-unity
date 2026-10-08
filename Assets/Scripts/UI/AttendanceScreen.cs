using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Minigames.Yut;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 출석보상 윷점 (기획 18장). Prefab: Assets/Prefabs/UI/AttendanceScreen.prefab
    /// 하루(새벽 4시 기준) 첫 접속 팝업 → 오늘 칸 탭 → 엽전 → 윷 3번 → 옥토끼 대화창(64괘).
    /// 팝업이 떠 있는 동안 ~ 옥토끼 대사가 끝날 때까지 요괴들은 말하지 않는다(SpeechGate).
    /// </summary>
    public class AttendanceScreen : MonoBehaviour
    {
        public static AttendanceScreen Instance { get; private set; }

        /// <summary>set 될 때마다 UiBlockGate에 자동 등록/해제 — 열려 있는 동안 맵 입력 차단.</summary>
        static bool isOpen;
        public static bool IsOpen
        {
            get => isOpen;
            private set
            {
                if (isOpen == value) return;
                isOpen = value;
                if (value) Yoegoe.Core.UiBlockGate.Register(typeof(AttendanceScreen));
                else Yoegoe.Core.UiBlockGate.Unregister(typeof(AttendanceScreen));
            }
        }

        public Font font;

        const int Days = 7;
        const int Throws = 3;

        [SerializeField] GameObject root;
        [SerializeField] GameObject boardPanel;
        [SerializeField] Image[] dayCircles = new Image[Days];
        [SerializeField] Text[] dayRewardTexts = new Text[Days];
        [SerializeField] Button[] dayButtons = new Button[Days];
        [SerializeField] Text guideText;
        [SerializeField] Button closeButton;
        [SerializeField] GameObject throwPanel;
        [Tooltip("윷가락이 착지하는 영역 (윷놀이 YutBoard 역할)")]
        [SerializeField] RectTransform throwLandZone;
        [SerializeField] Text[] throwResultTexts = new Text[Throws];
        [SerializeField] GameObject dialogPanel;
        [SerializeField] Image dialogPortrait;
        [SerializeField] Text dialogNameText;
        [SerializeField] Text dialogBodyText;
        [SerializeField] Button dialogButton;

        static readonly Color DoneColor = new Color(0.45f, 0.4f, 0.35f, 1f);
        static readonly Color TodayColor = new Color(1f, 0.82f, 0.35f, 1f);
        static readonly Color FutureColor = new Color(0.85f, 0.8f, 0.7f, 1f);

        bool busy;
        readonly RectTransform[] sticks = new RectTransform[4];

        void Awake() => Instance = this;
        void OnEnable() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>오늘 아직 처리 안 했으면 연다 (Main: 로드 직후·앱 복귀 시). hudFont = 한글 폰트.</summary>
        public static void TryOpenIfDue(Font hudFont)
        {
            if (IsOpen || !Attendance.ShouldOpen(Attendance.TodayKey)) return;
            var screen = Resolve();
            if (screen == null) return;
            if (hudFont != null) screen.font = hudFont;
            screen.Open();
        }

        /// <summary>씬 Prefab 인스턴스를 찾는다. 없으면 null (런타임 셸 생성 없음).</summary>
        public static AttendanceScreen Resolve()
        {
            if (Instance != null)
            {
                if (!Instance.gameObject.activeSelf) Instance.gameObject.SetActive(true);
                return Instance;
            }
            var found = Object.FindAnyObjectByType<AttendanceScreen>(FindObjectsInactive.Include);
            if (found == null)
            {
                Debug.LogError(
                    "[AttendanceScreen] Prefab 인스턴스가 씬에 없습니다. " +
                    "Main 씬에 AttendanceScreen Prefab 인스턴스를 배치하세요.");
                return null;
            }
            if (!found.gameObject.activeSelf) found.gameObject.SetActive(true);
            Instance = found;
            return found;
        }

        public void Open()
        {
            if (!EnsureShell()) return;
            ApplyFont();
            IsOpen = true;
            busy = false;
            SpeechGate.Silence();
            root.SetActive(true);
            boardPanel.SetActive(true);
            throwPanel.SetActive(false);
            dialogPanel.SetActive(false);
            closeButton.gameObject.SetActive(true);
            for (int i = 0; i < Throws; i++) throwResultTexts[i].text = "";
            guideText.text = "오늘 칸을 눌러 엽전을 받으세요";
            RefreshDays(Attendance.NextDayIndex, claimed: false);
        }

        void RefreshDays(int todayIndex, bool claimed)
        {
            var rewards = AttendanceCatalog.Rewards;
            for (int i = 0; i < Days; i++)
            {
                bool exists = i < rewards.Length;
                dayCircles[i].gameObject.SetActive(exists);
                if (!exists) continue;
                dayRewardTexts[i].text = (i + 1) + "일차\n엽전 " + rewards[i];
                bool done = i < todayIndex || (claimed && i == todayIndex);
                bool today = i == todayIndex && !claimed;
                dayCircles[i].color = done ? DoneColor : today ? TodayColor : FutureColor;
                dayButtons[i].interactable = today;
            }
        }

        void OnDayTapped(int index)
        {
            int today = Attendance.TodayKey;
            if (busy || index != Attendance.NextDayIndex || !Attendance.ShouldOpen(today)) return;
            int amount = Attendance.Claim(today, AttendanceCatalog.Rewards);
            busy = true;
            closeButton.gameObject.SetActive(false);
            RefreshDays(index, claimed: true);
            guideText.text = "엽전 " + amount + " 받았어요!";
            GameSaveBridge.SaveFromWorld();
            StartCoroutine(ThrowRoutine());
        }

        /// <summary>받지 않고 닫기 — 그날은 다시 안 뜬다(칸은 그대로).</summary>
        void OnCloseTapped()
        {
            if (busy) return;
            Attendance.Dismiss(Attendance.TodayKey);
            GameSaveBridge.SaveFromWorld();
            Finish();
        }

        IEnumerator ThrowRoutine()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            throwPanel.SetActive(true);

            int gua = Attendance.RollGua(max => Random.Range(0, max), out int a, out int b, out int c);
            int[] results = { a, b, c };
            var panel = (RectTransform)throwPanel.transform;
            var land = throwLandZone != null ? throwLandZone : panel;
            // 18장: 윷놀이에서 이무기가 던지는 연출 — 윷가락이 빠르게 흔들리다 결과 면을 드러낸다 (3번)
            ClearSticks();
            var stickImages = YutMiniGame.CreateStickRow(land, sticks);
            for (int t = 0; t < Throws; t++)
            {
                yield return YutMiniGame.ShakeRevealSticks(stickImages, ToThrowResult(results[t]));
                throwResultTexts[t].text = Attendance.ThrowName(results[t]);
                yield return new WaitForSecondsRealtime(0.6f);
            }

            yield return new WaitForSecondsRealtime(0.4f);
            ClearSticks();
            ShowFortune(gua, results);
        }

        /// <summary>윷점 0~3 → 윷놀이 결과 (윷점의 윷 = 등 4개, 모는 안 나옴).</summary>
        static YutThrowResult ToThrowResult(int v) => v switch
        {
            0 => YutThrowResult.Do,
            1 => YutThrowResult.Gae,
            2 => YutThrowResult.Geol,
            _ => YutThrowResult.Yut,
        };

        void ClearSticks()
        {
            for (int i = 0; i < sticks.Length; i++)
            {
                if (sticks[i] != null) Destroy(sticks[i].gameObject);
                sticks[i] = null;
            }
        }

        /// <summary>주입된 한글 폰트로 Prefab 텍스트를 덮는다.</summary>
        void ApplyFont()
        {
            if (font == null || root == null) return;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
                t.font = font;
        }

        void ShowFortune(int gua, int[] results)
        {
            boardPanel.SetActive(false);
            throwPanel.SetActive(false);
            dialogPanel.SetActive(true);

            var okto = FindOkto();
            dialogPortrait.sprite = okto != null ? CharacterSpawner.FirstSprite(okto.Data) : null;
            dialogPortrait.enabled = dialogPortrait.sprite != null;
            dialogNameText.text = okto != null && okto.Data != null && !string.IsNullOrEmpty(okto.Data.displayName)
                ? okto.Data.displayName : "옥토끼";

            string guaName = Attendance.ThrowName(results[0]) + "·" + Attendance.ThrowName(results[1]) + "·"
                             + Attendance.ThrowName(results[2]);
            var f = AttendanceCatalog.Get(gua);
            string line = PickLine(f);
            string title = f != null && !string.IsNullOrEmpty(f.name) ? guaName + ", " + f.name + "이에요." : guaName + "이 나왔어요.";
            dialogBodyText.text = title + "\n" + line;
            busy = false;
        }

        static string PickLine(AttendanceCatalog.Fortune f)
        {
            if (f?.lines == null) return "오늘은 괘가 흐릿하네요. 그래도 좋은 하루 보내세요!";
            // 세 줄(일/사람/마음) 중 하나 — 빈 줄은 건너뛴다
            int start = Random.Range(0, 3);
            for (int i = 0; i < 3; i++)
            {
                int k = (start + i) % 3;
                if (k < f.lines.Length && !string.IsNullOrEmpty(f.lines[k])) return f.lines[k];
            }
            return "오늘은 괘가 흐릿하네요. 그래도 좋은 하루 보내세요!";
        }

        void OnDialogTapped()
        {
            if (busy) return;
            Finish();
        }

        void Finish()
        {
            ClearSticks();
            if (root != null) root.SetActive(false);
            IsOpen = false;
            busy = false;
            SpeechGate.Release();
        }

        /// <summary>콜드스타트 등에서 오버레이를 강제로 닫을 때.</summary>
        public void Close() => Finish();

        static CharacterAgent FindOkto()
        {
            foreach (var a in CharacterAgent.All)
                if (a != null && a.Data != null && a.Data.id == CharacterId.Rabbit) return a;
            return null;
        }

        // ---------------- 셸 (Prefab) ----------------

        /// <summary>Prefab 셸만 사용. 없으면 에러 (런타임 생성 없음).</summary>
        bool EnsureShell()
        {
            if (root != null)
            {
                WireListeners();
                return true;
            }
            Debug.LogError(
                "[AttendanceScreen] Prefab 셸이 없습니다. " +
                "Main 씬에 AttendanceScreen Prefab 인스턴스를 배치하세요.");
            return false;
        }

        bool wired;

        void WireListeners()
        {
            if (wired) return;
            wired = true;
            for (int i = 0; i < Days; i++)
            {
                int idx = i;
                if (dayButtons[i] != null) dayButtons[i].onClick.AddListener(() => OnDayTapped(idx));
            }
            if (closeButton != null) closeButton.onClick.AddListener(OnCloseTapped);
            if (dialogButton != null) dialogButton.onClick.AddListener(OnDialogTapped);
        }
    }
}
