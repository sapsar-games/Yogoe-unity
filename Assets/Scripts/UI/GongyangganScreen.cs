using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yoegoe.Cooking;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
/// <summary>
/// 공양간(요리 미니게임). Prefab: Assets/Prefabs/UI/GongyangganScreen.prefab
/// </summary>
public class GongyangganScreen : MonoBehaviour
{
    public static GongyangganScreen Instance { get; private set; }

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

    void Awake()
    {
        Instance = this;
    }

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

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
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

    public bool IsOpen { get; private set; }

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

    /// <summary>
    /// 시작 버튼. 재료가 20개 미만이면 "재료가 모자라는데도 요리할까요?" 확인 (19장).
    /// 보이는 판(미리보기)을 그대로 시작하고, 재료·부적은 이때 처음 차감된다.
    /// </summary>
    void OnStart()
    {
        if (session == null || session.Running || session.Finished) return;
        if (session.MaterialsOnBoard < CookingSession.MinMaterialsToCook)
        {
            if (statusText != null) statusText.text = "재료가 모자라 요리할 수 없어요";
            return;
        }
        if (session.IsShortBoard)
            ConfirmPopup.Show("재료가 모자라는데도 요리할까요?", BeginRound);
        else
            BeginRound();
    }

    void BeginRound()
    {
        if (session == null || session.Running || session.Finished) return;
        if (selectedCharm != CookingCharmType.None
            && (GameEconomy.Instance == null || !GameEconomy.Instance.TrySpendCharm(selectedCharm)))
        {
            // 부적이 그사이 없어졌으면 부적 없는 판으로 다시 깐다
            selectedCharm = CookingCharmType.None;
            RebuildSession();
            return;
        }
        if (!session.StartRound())
        {
            // 판에 깔린 재료가 인벤에 더는 없음 — 부적은 돌려주고 판을 다시 깐다
            if (selectedCharm != CookingCharmType.None && GameEconomy.Instance != null)
                GameEconomy.Instance.AddCharm(selectedCharm, 1);
            RebuildSession();
            return;
        }
        GameSaveBridge.RequestSave(); // 재료·부적 차감
        RefreshView();
    }

    /// <summary>선택한 부적으로 판(미리보기)을 새로 깐다. 재료는 차감하지 않는다.</summary>
    void RebuildSession()
    {
        if (session != null)
        {
            session.Changed -= RefreshView;
            session.RoundEnded -= OnRoundEnded;
            session.TimeUp -= OnTimeUp;
        }
        session = new CookingSession();
        session.Changed += RefreshView;
        session.RoundEnded += OnRoundEnded;
        session.TimeUp += OnTimeUp;
        session.Prepare(selectedCharm);
        RefreshView();
    }

    void SelectCharm(CookingCharmType charm)
    {
        if (session != null && session.Running) return;
        if (charm != CookingCharmType.None)
        {
            int held = GameEconomy.Instance != null ? GameEconomy.Instance.GetCharmCount(charm) : 0;
            if (held <= 0 && selectedCharm != charm) return;
        }
        selectedCharm = selectedCharm == charm ? CookingCharmType.None : charm;
        RebuildSession();
    }

    /// <summary>시간 종료 → "광고 보고 15초 더?" (19장, 무제한). 아니오·바깥 탭 = 그만하고 정산.
    /// 광고 SDK 미연동 — 예를 누르면 바로 연장(스텁).</summary>
    void OnTimeUp()
    {
        if (session == null || !session.AwaitingExtend) return;
        var s = session;
        ConfirmPopup.Show("시간이 다 됐어요!\n광고 보고 15초 더 할까요?",
            () => { if (session == s) s.ExtendByAd(); },
            () => { if (session == s) s.FinishAfterTimeUp(); },
            yes: "광고 보고 +15초", no: "그만하기");
    }

    /// <summary>결과창 (19장): 이번에 만든 요리 · 새로 얻은 레시피(금색) · 스러진 재료(회수 부적이면 회수한 재료).</summary>
    // '만들 수 있는 요리' 계산은 판의 2~3칸 조합을 전부 보므로 무겁다 — 판·도감이 바뀔 때만 다시 계산 (타이머 갱신은 매 프레임)
    CookingSession makeableSession;
    int makeableBoardVersion = -1;
    int makeableDiscovered = -1;

    void RefreshMakeable()
    {
        int discovered = CookingCodex.DiscoveredCount;
        if (makeableSession == session && makeableBoardVersion == session.BoardVersion
            && makeableDiscovered == discovered)
            return;
        makeableSession = session;
        makeableBoardVersion = session.BoardVersion;
        makeableDiscovered = discovered;
        if (makeableText != null) makeableText.text = MakeableLine(session);
        if (codexButtonLabel != null) codexButtonLabel.text = "요리책 " + CodexScreen.RateShort;
    }

    /// <summary>상태 줄 '지금 만들 수 있는 요리' — 발견한 요리는 이름, 아직 못 본 건 '?' (19장).</summary>
    public static string MakeableLine(CookingSession s)
    {
        if (s == null || s.Finished) return "";
        var list = s.MakeableNow();
        if (list.Count == 0) return "만들 수 있는 요리 없음";
        var names = new System.Collections.Generic.List<string>();
        int unknown = 0;
        foreach (var r in list)
        {
            if (CookingCodex.IsDiscovered(r.Id)) names.Add(r.DisplayName);
            else unknown++;
        }
        for (int i = 0; i < unknown; i++) names.Add("?");
        return "만들 수 있는 요리: " + string.Join(" · ", names);
    }

    void OnRoundEnded()
    {
        if (resultPopup == null || resultBody == null || session == null) return;
        resultBody.text = BuildResultText(session);
        resultPopup.SetActive(true);
        GameSaveBridge.RequestSave(); // 완성품·도감·회수 재료
        RefreshView();
    }

    public static string BuildResultText(CookingSession s)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("이번에 만든 요리");
        if (s.Results.Count == 0)
            sb.AppendLine("· 없음");
        foreach (var (recipe, count, golden) in s.Results)
            sb.AppendLine(golden
                ? $"· <color=#FFD54A>황금 {recipe.DisplayName}</color> x{count}"
                : $"· {recipe.DisplayName} x{count}");

        var guest = s.GuestOrder;
        if (guest != null)
        {
            sb.AppendLine();
            sb.AppendLine("주문 요괴");
            if (guest.Fulfilled)
            {
                string how = guest.Perfect ? "완벽하게 " : "";
                sb.AppendLine($"· {guest.DisplayName}에게 {guest.OfferingName}을(를) {how}대접했어요");
                sb.AppendLine(guest.RevivedFromFaint
                    ? $"· 기절에서 깨어났어요 · 기력 +{guest.StaminaGain}"
                    : $"· 기력 +{guest.StaminaGain} · 친밀도 +{guest.IntimacyGain:0.#}");
            }
            else if (guest.Failed)
                sb.AppendLine($"· {guest.DisplayName}이(가) 아쉬워하며 돌아갔어요 — 실망…");
        }

        if (s.PerfectCollectCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"김 오를 때 꺼낸 요리 {s.PerfectCollectCount}번");
        }

        if (s.NewlyDiscovered.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("새로 얻은 레시피");
            foreach (var id in s.NewlyDiscovered)
                if (CookingCodex.TryGetRecipe(id, out var r))
                    sb.AppendLine($"<color=#FFD54A>· {r.DisplayName}</color>");
        }

        if (s.Leftover.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(s.PreCharm == CookingCharmType.Recycle ? "회수한 재료" : "스러진 재료");
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            var order = new System.Collections.Generic.List<string>();
            foreach (var item in s.Leftover)
            {
                string name = (item.Golden ? "황금" : "") + CookingRecipeCatalog.DisplayName(item.Id);
                if (!counts.ContainsKey(name)) { counts[name] = 0; order.Add(name); }
                counts[name]++;
            }
            var parts = new System.Collections.Generic.List<string>();
            foreach (var name in order) parts.Add($"{name} {counts[name]}");
            sb.AppendLine(string.Join(" · ", parts));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>'다시 지피다' — 같은 부적(남아 있으면)으로 새 판을 깔고 바로 시작 절차(재료 부족 확인 포함).</summary>
    void OnRekindle()
    {
        if (resultPopup != null) resultPopup.SetActive(false);
        if (selectedCharm != CookingCharmType.None
            && (GameEconomy.Instance == null || GameEconomy.Instance.GetCharmCount(selectedCharm) <= 0))
            selectedCharm = CookingCharmType.None;
        RebuildSession();
        OnStart();
    }

    void RefreshView()
    {
        if (session == null) return;
        if (timerText != null)
        {
            timerText.text = $"{session.TimeLeft:0.0}s";
            timerText.color = session.TimeLeft <= 5f
                ? new Color(0.9f, 0.25f, 0.2f)
                : Color.white;
        }
        if (statusText != null)
        {
            string charm = selectedCharm == CookingCharmType.None
                ? "부적 없음"
                : $"{CharmLabel(selectedCharm)} (보유 {(GameEconomy.Instance != null ? GameEconomy.Instance.GetCharmCount(selectedCharm) : 0)})";
            string guest = GuestStatusLine(session);
            if (session.Running)
            {
                statusText.text = string.IsNullOrEmpty(guest)
                    ? $"요리 중 · {charm}"
                    : guest;
            }
            else if (session.Finished)
                statusText.text = string.IsNullOrEmpty(guest) ? $"끝 · {charm}" : guest;
            else
                statusText.text = $"준비 · 재료 {session.MaterialsOnBoard}개 · {charm}";
        }
        if (titleText != null && session.Running && session.GuestOrder != null
            && !session.GuestOrder.Fulfilled && !session.GuestOrder.Failed)
            titleText.text = "공양간 · " + session.GuestOrder.DisplayName;
        else if (titleText != null)
            titleText.text = "공양간";
        if (makeableText != null || codexButtonLabel != null) RefreshMakeable();
        if (nagariButton != null)
        {
            bool showNagari = session.ShowNagari;
            nagariButton.gameObject.SetActive(showNagari);
            if (showNagari)
            {
                var label = nagariButton.GetComponentInChildren<Text>(true);
                int held = GameEconomy.Instance != null ? GameEconomy.Instance.GetCharmCount(CookingCharmType.Cancel) : 0;
                if (label != null && nagariLabelHeld != held)
                {
                    nagariLabelHeld = held;
                    label.text = $"나가리 ×{held}";
                }
                // 손님에게 요리를 건넨 뒤엔 사용 불가 — 버튼 위에 X
                bool locked = session.CharmsLocked;
                nagariButton.interactable = !locked;
                var x = EnsureNagariLockMark(label != null ? label.font : null);
                if (x != null && x.activeSelf != locked) x.SetActive(locked);
            }
        }
        if (extendButton != null)
            extendButton.gameObject.SetActive(session.AwaitingExtend);
        if (startButton != null)
            startButton.interactable = !session.Running && !session.Finished;

        for (int y = 0; y < CookingSession.GridSize; y++)
        for (int x = 0; x < CookingSession.GridSize; x++)
        {
            var img = cellImages[x, y];
            var label = cellLabels[x, y];
            if (img == null) continue;
            bool onPath = false;
            if (session.Path != null)
            {
                for (int i = 0; i < session.Path.Count; i++)
                    if (session.Path[i].x == x && session.Path[i].y == y) { onPath = true; break; }
            }

            if (session.Locked[x, y] || !session.Grid[x, y].HasValue)
            {
                var cook = session.CookAt(x, y);
                if (cook != null)
                {
                    img.color = cook.Phase == CookingCookPhase.Steam
                        ? new Color(0.95f, 0.85f, 0.45f, 1f)
                        : cook.Phase == CookingCookPhase.Cool
                            ? new Color(0.75f, 0.35f, 0.28f, 1f)
                            : new Color(0.55f, 0.32f, 0.18f, 1f);
                    if (label != null)
                    {
                        string tag = cook.Phase == CookingCookPhase.Steam ? "김!"
                            : cook.Phase == CookingCookPhase.Cool ? "식음"
                            : "익는중";
                        label.text = cook.Recipe.DisplayName + "\n" + tag;
                    }
                }
                else
                {
                    img.color = new Color(0.15f, 0.12f, 0.1f, 0.55f);
                    if (label != null) label.text = "";
                }
            }
            else
            {
                bool golden = session.Golden != null && session.Golden[x, y];
                img.color = onPath
                    ? new Color(0.95f, 0.75f, 0.35f, 1f)
                    : golden
                        ? GoldenSparkle.Pulse(new Color(0.55f, 0.42f, 0.12f, 1f), new Color(0.85f, 0.68f, 0.2f, 1f))
                        : new Color(0.35f, 0.28f, 0.22f, 1f);
                if (label != null)
                    label.text = golden
                        ? "황금" + CookingRecipeCatalog.DisplayName(session.Grid[x, y].Value)
                        : CookingRecipeCatalog.DisplayName(session.Grid[x, y].Value);
            }
        }

        for (int i = 0; i < charmButtons.Length; i++)
        {
            if (charmButtons[i] == null) continue;
            var img = charmButtons[i].GetComponent<Image>();
            var t = CharmAt(i);
            int held = GameEconomy.Instance != null ? GameEconomy.Instance.GetCharmCount(t) : 0;
            bool sel = selectedCharm == t;
            bool canUse = held > 0;
            if (img != null)
            {
                if (sel) img.color = new Color(0.85f, 0.65f, 0.3f);
                else if (canUse) img.color = new Color(0.4f, 0.35f, 0.3f);
                else img.color = new Color(0.22f, 0.2f, 0.18f, 0.7f);
            }
            charmButtons[i].interactable = canUse || sel;
            var label = charmButtons[i].GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = $"{CharmLabel(t)}\n×{held}";
        }
    }

    static CookingCharmType CharmAt(int i) => i switch
    {
        0 => CookingCharmType.PlusFive,
        1 => CookingCharmType.Diagonal,
        2 => CookingCharmType.Clairvoyance,
        3 => CookingCharmType.Recycle,
        4 => CookingCharmType.Double,
        _ => CookingCharmType.None
    };

    static string CharmLabel(CookingCharmType t) => t switch
    {
        CookingCharmType.PlusFive => "+5초",
        CookingCharmType.Diagonal => "대각선",
        CookingCharmType.Clairvoyance => "천리안",
        CookingCharmType.Recycle => "회수",
        CookingCharmType.Double => "몰빵",
        CookingCharmType.Cancel => "나가리",
        _ => ""
    };

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

    public void OnCellDown(int x, int y)
    {
        if (session != null && session.CookAt(x, y) != null)
        {
            bool guestWasDone = session.GuestOrder != null && session.GuestOrder.Fulfilled;
            if (!session.TryCollectCook(x, y) && statusText != null)
            {
                var job = session.CookAt(x, y);
                if (job != null && job.Phase == CookingCookPhase.Cooking)
                    statusText.text = "아직 익는 중";
            }
            else
            {
                var g = session.GuestOrder;
                if (g != null && g.Fulfilled && !guestWasDone && g.IntimacyGain > 0f)
                    IntimacyHeartFx.PlayFromAgent(g.Yokai, g.IntimacyGain);
            }
            return;
        }
        pointerDown = true;
        session?.TryBeginPath(x, y);
    }

    int nagariLabelHeld = -1;
    GameObject nagariLockMark;

    /// <summary>나가리 버튼 위 X 표시 (동적 콘텐츠 — 처음 필요할 때 한 번 만든다).</summary>
    GameObject EnsureNagariLockMark(Font labelFont)
    {
        if (nagariLockMark != null || nagariButton == null) return nagariLockMark;
        var go = new GameObject("LockedX", typeof(RectTransform));
        go.transform.SetParent(nagariButton.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = go.AddComponent<Text>();
        t.font = labelFont != null ? labelFont : font;
        t.text = "X";
        t.fontStyle = FontStyle.Bold;
        t.fontSize = 64;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(0.9f, 0.2f, 0.2f, 0.95f);
        t.raycastTarget = false;
        go.SetActive(false);
        nagariLockMark = go;
        return go;
    }

    static string GuestStatusLine(CookingSession s)
    {
        var g = s?.GuestOrder;
        if (g == null) return "";
        if (g.Failed) return $"{g.DisplayName}: 실망…";
        if (g.Fulfilled && g.RevivedFromFaint)
            return $"{g.DisplayName}: 깨어났어! 기력 +{g.StaminaGain}";
        if (g.Fulfilled)
            return g.Perfect
                ? $"{g.DisplayName}: 최고야! 친밀도 +{g.IntimacyGain:0.#} · 기력 +{g.StaminaGain}"
                : $"{g.DisplayName}: 고마워! 친밀도 +{g.IntimacyGain:0.#} · 기력 +{g.StaminaGain}";
        return $"{g.DisplayName}: {g.WaitLine} ({g.OfferingName})";
    }

    public void OnCellEnter(int x, int y)
    {
        if (!pointerDown) return;
        session?.TryExtendPath(x, y);
    }

    public void OnCellUp()
    {
        if (!pointerDown) return;
        pointerDown = false;
        session?.EndPath();
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
