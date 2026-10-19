using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yoegoe.Cooking;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 공양간(요리 미니게임). Prefab: Assets/Prefabs/UI/GongyangganScreen.prefab
    ///
    /// 영역별로 파일을 나눠뒀다 — 모두 같은 partial class라 필드·헬퍼는 공유:
    /// - GongyangganScreen.cs (여기): 필드, 생명주기(Start/Update), Open/Close, 셸/배선
    /// - GongyangganScreen.Session.cs: 요리 시작/부적 선택/연장 — 판(세션) 관리
    /// - GongyangganScreen.Grid.cs: 그리드 셀 입력 + 부적 버튼
    /// - GongyangganScreen.View.cs: 화면 전체 리프레시(타이머·상태·그리드·부적 버튼 표시)
    /// - GongyangganScreen.Result.cs: 결과 팝업 텍스트
    /// </summary>
    public partial class GongyangganScreen : SingletonBehaviour<GongyangganScreen>
    {
        public Font font;

        [SerializeField] GameObject root;
        [SerializeField] Text titleText;
        [SerializeField] Text timerText;
        [SerializeField] Text statusText;
        [SerializeField] Transform gridHost;
        [SerializeField] Transform charmRail;
        [SerializeField] Button startButton;
        [SerializeField] Button nagariButton;
        [SerializeField] Button extendButton;
        [SerializeField] Button closeButton;
        [SerializeField] GameObject resultPopup;
        [SerializeField] Text resultBody;
        // 요리책(19장) — Prefab 이름으로 바인딩
        [SerializeField] Button codexButton;
        [SerializeField] Text codexButtonLabel;
        [SerializeField] Text makeableText;
        [SerializeField] Button rekindleButton;

        readonly Image[,] cellImages = new Image[CookingSession.GridSize, CookingSession.GridSize];
        readonly Text[,] cellLabels = new Text[CookingSession.GridSize, CookingSession.GridSize];
        readonly Button[] charmButtons = new Button[5];
        CookingSession session;
        CookingCharmType selectedCharm = CookingCharmType.None;
        bool pointerDown;

        /// <summary>'만들 수 있는 요리' 캐시 — 판의 2~3칸 조합을 전부 보므로 무겁다. 판·도감이 바뀔 때만 다시 계산.</summary>
        CookingSession makeableSession;
        int makeableBoardVersion = -1;
        int makeableDiscovered = -1;

        int nagariLabelHeld = -1;
        GameObject nagariLockMark;

        void OnEnable()
        {
            Instance = this;
        }

        /// <summary>비활성 Prefab 인스턴스도 찾아 켠 뒤 반환.</summary>
        public static GongyangganScreen Resolve()
        {
            if (Instance != null)
            {
                if (!Instance.gameObject.activeSelf)
                    Instance.gameObject.SetActive(true);
                return Instance;
            }

            var found = Object.FindAnyObjectByType<GongyangganScreen>(FindObjectsInactive.Include);
            if (found != null)
            {
                if (!found.gameObject.activeSelf)
                    found.gameObject.SetActive(true);
                Instance = found;
                return found;
            }

            var prefab = Resources.Load<GameObject>("UI/GongyangganScreen");
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab);
            go.name = "GongyangganScreen";
            var screen = go.GetComponent<GongyangganScreen>();
            if (screen != null) Instance = screen;
            return screen;
        }

        void Start()
        {
            if (!EnsureShell()) return;
            WireRuntimeListeners();
            if (root != null) root.SetActive(false);
            IsOpen = false;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Yoegoe.Core.UiBlockGate.Unregister(this);
            if (session != null)
            {
                session.Changed -= RefreshView;
                session.RoundEnded -= OnRoundEnded;
                session.TimeUp -= OnTimeUp;
            }
        }

        void Update()
        {
            // 요리책을 여는 동안 타이머는 멈춘다 (19장)
            if (session != null && root != null && root.activeInHierarchy && !CodexScreen.IsShowing)
                session.Tick(Time.unscaledDeltaTime);
        }

        /// <summary>set 될 때마다 UiBlockGate에 자동 등록/해제 — 열려 있는 동안 맵 입력 차단.</summary>
        bool isOpen;
        public bool IsOpen
        {
            get => isOpen;
            private set
            {
                if (isOpen == value) return;
                isOpen = value;
                if (value) Yoegoe.Core.UiBlockGate.Register(this);
                else Yoegoe.Core.UiBlockGate.Unregister(this);
            }
        }

        public void Open()
        {
            if (!EnsureShell()) return;
            WireRuntimeListeners();
            selectedCharm = CookingCharmType.None;
            session = new CookingSession();
            session.Changed += RefreshView;
            session.RoundEnded += OnRoundEnded;
            session.TimeUp += OnTimeUp;
            session.Prepare(CookingCharmType.None);
            if (resultPopup != null) resultPopup.SetActive(false);
            root.SetActive(true);
            IsOpen = true;
            RefreshView();
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
            IsOpen = false;
            if (session != null)
            {
                // 요리 중·연장 대기 중에 닫으면 만든 것까지 정산 (결과 팝업 없이)
                ConfirmPopup.Dismiss();
                session.RoundEnded -= OnRoundEnded;
                session.FinishNow();
                GameSaveBridge.RequestSave();
                session.Changed -= RefreshView;
                session.RoundEnded -= OnRoundEnded;
                session.TimeUp -= OnTimeUp;
                session = null;
            }
        }

        bool EnsureShell()
        {
            BindMissingRefsFromHierarchy();
            if (root != null) return true;
            Debug.LogError(
                "[GongyangganScreen] Prefab 셸이 없습니다. Main 씬에 GongyangganScreen Prefab 인스턴스를 배치하세요.");
            return false;
        }

        void WireRuntimeListeners()
        {
            if (root == null) return;
            BindMissingRefsFromHierarchy();
            CacheGridCells();
            CacheCharmButtons();

            if (startButton != null)
            {
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(OnStart);
            }
            if (nagariButton != null)
            {
                nagariButton.onClick.RemoveAllListeners();
                nagariButton.onClick.AddListener(() => session?.CancelNagari());
            }
            if (extendButton != null)
            {
                extendButton.onClick.RemoveAllListeners();
                extendButton.onClick.AddListener(OnTimeUp);
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }
            if (codexButton != null)
            {
                codexButton.onClick.RemoveAllListeners();
                codexButton.onClick.AddListener(() => CodexScreen.Instance?.Open());
            }
            if (rekindleButton != null)
            {
                rekindleButton.onClick.RemoveAllListeners();
                rekindleButton.onClick.AddListener(OnRekindle);
            }
            if (resultPopup != null)
            {
                var closeRes = resultPopup.transform.Find("Box/Close");
                if (closeRes != null)
                {
                    var b = closeRes.GetComponent<Button>() ?? closeRes.gameObject.AddComponent<Button>();
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(() =>
                    {
                        resultPopup.SetActive(false);
                        Close();
                    });
                }
            }
        }

        void CacheGridCells()
        {
            if (gridHost == null) return;
            for (int y = 0; y < CookingSession.GridSize; y++)
            for (int x = 0; x < CookingSession.GridSize; x++)
            {
                var cell = gridHost.Find($"Cell_{x}_{y}");
                if (cell == null) continue;
                cellImages[x, y] = cell.GetComponent<Image>();
                // Bake 구조: Cell/Text(래퍼)/Text(실제 UI.Text) — 래퍼에는 Text가 없음
                var labelHost = cell.Find("Text");
                cellLabels[x, y] = labelHost != null
                    ? labelHost.GetComponentInChildren<Text>(true)
                    : null;
                EnsureCellPointer(cell.gameObject, x, y);
            }
        }

        void CacheCharmButtons()
        {
            if (charmRail == null) return;
            for (int i = 0; i < charmButtons.Length; i++)
            {
                var t = charmRail.Find("Charm_" + i);
                if (t == null) continue;
                charmButtons[i] = t.GetComponent<Button>();
                int captured = i;
                if (charmButtons[i] != null)
                {
                    charmButtons[i].onClick.RemoveAllListeners();
                    charmButtons[i].onClick.AddListener(() => SelectCharm(CharmAt(captured)));
                }
            }
        }

        void EnsureCellPointer(GameObject cell, int x, int y)
        {
            var proxy = cell.GetComponent<GongyangganCellProxy>()
                        ?? cell.AddComponent<GongyangganCellProxy>();
            proxy.Bind(this, x, y);
        }

        void BindMissingRefsFromHierarchy()
        {
            if (root == null)
            {
                var canvas = transform.Find("Canvas_Gongyanggan");
                if (canvas != null) root = canvas.Find("Root")?.gameObject;
            }
            if (root == null)
            {
                // Prefab 인스턴스에서 Find 경로가 어긋날 때 대비
                var transforms = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    var t = transforms[i];
                    if (t != null && t.name == "Root" && t.parent != null
                        && t.parent.name == "Canvas_Gongyanggan")
                    {
                        root = t.gameObject;
                        break;
                    }
                }
            }
            if (root == null) return;
            var rt = root.transform;
            if (titleText == null) titleText = FindUiText(rt, "Title/Text");
            if (timerText == null) timerText = FindUiText(rt, "Timer/Text");
            if (statusText == null) statusText = FindUiText(rt, "Status/Text");
            if (gridHost == null) gridHost = rt.Find("Grid");
            if (charmRail == null) charmRail = rt.Find("CharmRail");
            if (startButton == null) startButton = rt.Find("Start")?.GetComponent<Button>();
            if (nagariButton == null) nagariButton = rt.Find("Nagari")?.GetComponent<Button>();
            if (extendButton == null) extendButton = rt.Find("Extend")?.GetComponent<Button>();
            if (closeButton == null) closeButton = rt.Find("Close")?.GetComponent<Button>();
            if (resultPopup == null) resultPopup = rt.Find("ResultPopup")?.gameObject;
            if (resultPopup != null && resultBody == null)
                resultBody = FindUiText(resultPopup.transform, "Box/Body/Text");
            if (codexButton == null) codexButton = rt.Find("CodexButton")?.GetComponent<Button>();
            if (codexButtonLabel == null) codexButtonLabel = FindUiText(rt, "CodexButton/Label");
            if (makeableText == null) makeableText = FindUiText(rt, "Makeable");
            if (resultPopup != null && rekindleButton == null)
                rekindleButton = resultPopup.transform.Find("Box/Rekindle")?.GetComponent<Button>();
        }

        static Text FindUiText(Transform root, string path)
        {
            var t = root.Find(path);
            if (t == null) return null;
            return t.GetComponent<Text>() ?? t.GetComponentInChildren<Text>(true);
        }
    }

    /// <summary>그리드 셀 드래그 입력.</summary>
    public class GongyangganCellProxy : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerUpHandler
    {
        GongyangganScreen screen;
        int x, y;

        public void Bind(GongyangganScreen s, int cx, int cy)
        {
            screen = s;
            x = cx;
            y = cy;
        }

        public void OnPointerDown(PointerEventData eventData) => screen?.OnCellDown(x, y);
        public void OnPointerEnter(PointerEventData eventData) => screen?.OnCellEnter(x, y);
        public void OnPointerUp(PointerEventData eventData) => screen?.OnCellUp();
    }
}
