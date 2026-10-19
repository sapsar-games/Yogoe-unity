using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.UI;

namespace Yoegoe.Minigames.Yut
{
    /// <summary>
    /// 윷놀이 전체화면 UI. 보드·말·던지기 입력 표시와 이벤트를 담당하고,
    /// 규칙·재화는 호출부(YutScreen)가 처리한다.
    /// </summary>
    public partial class YutMiniGame : MonoBehaviour
    {
        /// <summary>
        /// 한글 표시용 폰트. 비워두면 유니티 기본 폰트로 나오는데, WebGL에선 한글 글리프가 없어서
        /// 글씨가 아예 안 보인다 — 호출부(YutScreen)가 프로젝트 한글 폰트(DOSGothic 등)를 넣어준다.
        /// </summary>
        public Font font;

        [Header("말 크기 (Inspector에서 조절)")]
        [Tooltip("보드 칸 한 변 대비 말 크기 비율. 기본 0.64")]
        [SerializeField, Range(0.2f, 1.2f)] float boardPieceFill = 0.64f;
        [Tooltip("로스터 슬롯 대기말 — 초상 영역 inset(0~0.45). 키울수록 말 작아짐. 기본 0.05")]
        [SerializeField, Range(0f, 0.45f)] float waitingPieceInSlotInset = 0.05f;
        [Tooltip("한 칸에 업힌 말들의 가로 간격(말 폭 대비). 기본 0.62")]
        [SerializeField, Range(0.3f, 1f)] float stackedPieceSpread = 0.62f;

        /// <summary>보드에 나간(또는 완주한) 요괴 슬롯 초상 — 스프라이트 형태를 남기는 실루엣.</summary>
        static readonly Color RosterSilhouetteColor = new(0.16f, 0.14f, 0.12f, 0.92f);

        /// <summary>탭이 아니라 아래→위 슬라이드로 던지기가 완료됐을 때. power(0~1)는 슬라이드
        /// 속도 기반 — 던지는 연출(아치 높이·회전·착지 퍼짐)에만 쓰고 결과 확률엔 영향 없다.</summary>
        public event Action<float> OnThrowPressed;
        public event Action OnLeavePressed;
        /// <summary>윷 토큰(하트) 옆 [+] 버튼 — YutScreen이 DualActionPopup 윷 토큰 상점을 연다.</summary>
        public event Action OnBuyTokensPressed;
        /// <summary>FlashCandidates로 보드 위에 띄운 후보 중 하나를 유저가 탭했을 때 — 그 요괴 id와
        /// (갈림길 후보였다면) 지름길을 골랐는지 여부.</summary>
        public event Action<string, bool> OnCandidateTapped;

        Image[] _heartIcons;
        GameObject _throwZone;
        YutThrowSwipeZone _throwSwipe;
        Button _leaveButton;
        RectTransform _boardRoot;
        RectTransform _opponentPiece;
        Image[] _pads;
        RectTransform[] _parkedSticks;
        RectTransform[] _quadrants; // YutBoardQuadrant 순서대로
        readonly List<GameObject> _candidateMarkers = new();
        readonly List<(RectTransform rect, string pieceId, bool useShortcut, string[] stackMemberIds)> _candidateHits = new();
        /// <summary>후보 표시 중 "이미 그 칸에 있는 실제 말"을 옆으로 비켜 놓은 칸 — ClearCandidates에서 원위치.</summary>
        readonly List<int> _shiftedOccupantNodes = new();
        GameObject _miniThrowContainer;
        Image[] _miniThrowSticks;

        // 대화 말풍선(각 요괴 말 근처)과 놀이기록(윷 토큰 옆 버튼으로 여는 팝업)은 분리 —
        // 말풍선은 타이머 없이 다음 액션(던지기/말 선택/턴 전환/화면 닫기)까지 유지한다.
        const string OpponentBubbleKey = "__opponent__";
        readonly Dictionary<string, GameObject> _activeBubbles = new();
        /// <summary>말풍선이 따라다닐 말 RectTransform — LateUpdate에서 위치를 맞춘다.</summary>
        readonly Dictionary<string, RectTransform> _bubbleAnchors = new();
        Button _playLogButton;
        GameObject _playLogPanel;
        RectTransform _playLogContent;
        RectTransform _playLogViewport;
        ScrollRect _playLogScroll;
        readonly List<string> _playLogEntries = new();
        const int MaxPlayLogEntries = 200;

        // 본게임 — 보유 요괴 전체를 동시에 말로 표시(id → 말 오브젝트/이니셜 라벨).
        readonly Dictionary<string, RectTransform> _yokaiPieces = new();
        readonly Dictionary<string, Text> _yokaiPieceLabels = new();
        /// <summary>Inspector에서 말 크기를 바꿀 때 Play 중에도 다시 깔 수 있게, 마지막 Show 입력을 기억한다.</summary>
        List<YokaiPieceInfo> _lastYokaiPieces;
        bool _suppressPieceSlide;

        RectTransform _rosterPanel;
        RectTransform _rosterRow;
        Text _rosterCaption;
        readonly List<RosterChip> _rosterChips = new();
        GameObject _summonSlotChip;

        readonly struct RosterChip
        {
            public readonly string Id;
            public readonly RectTransform Root;
            public readonly Image Portrait;
            public readonly Text Name;
            public readonly Text Stats;
            public readonly Text Status;

            public RosterChip(string id, RectTransform root, Image portrait, Text name, Text stats, Text status)
            {
                Id = id;
                Root = root;
                Portrait = portrait;
                Name = name;
                Stats = stats;
                Status = status;
            }
        }

        /// <summary>윷놀이 화면 최하단 요괴 명단 한 줄(초상·이름·기력♦친밀도♡·보드 위치) 항목.</summary>
        public readonly struct RosterEntry
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly int Stamina;
            public readonly int Intimacy;
            public readonly string StatusLabel;
            /// <summary>true면 아직 출발 전(또는 잡혀 복귀) — 슬롯에 말이 대기하고 초상은 숨긴다.
            /// false면 보드/완주 — 슬롯 초상은 실루엣.</summary>
            public readonly bool WaitingInSlot;

            public RosterEntry(string id, string displayName, int stamina, int intimacy, string statusLabel,
                bool waitingInSlot = false)
            {
                Id = id;
                DisplayName = displayName;
                Stamina = stamina;
                Intimacy = intimacy;
                StatusLabel = statusLabel;
                WaitingInSlot = waitingInSlot;
            }
        }

        static readonly Color YutStickFront = new(0.92f, 0.88f, 0.78f);
        static readonly Color YutStickBack = new(0.35f, 0.3f, 0.26f);
        static readonly Color BaekdoMarkColor = new(0.85f, 0.25f, 0.3f);

        static Sprite _stickFront;
        static Sprite _stickFrontBaekdo;
        static Sprite _stickBack;

        static readonly Color HeartOn = new(0.95f, 0.25f, 0.35f);
        static readonly Color HeartOff = new(0.3f, 0.15f, 0.18f, 0.6f);

        // 우상단 한 줄: [기록] [+ 토큰구매] [하트(윷 토큰)] — 하트가 오른쪽 끝(HeartsRightEdge)에
        // 붙고, 그 왼쪽으로 +·기록 버튼이 이어진다. 세 자리 모두 여기 상수 하나로 관리한다.
        const float HeartsRightEdge = 0.93f;
        const float TokenPlusLeft = 0.673f;
        const float TokenPlusRight = 0.713f;
        const float PlayLogLeft = 0.533f;
        const float PlayLogRight = 0.663f;
        Button _tokenPlusButton;
        Text _challengeBannerText;

        public void BindFromHierarchy()
        {
            EnsureThrowSwipeZone();

            _leaveButton = transform.Find("Leave")?.GetComponent<Button>();
            if (_leaveButton == null)
            {
                _leaveButton = CreateButton(transform, "Leave", "나가기", null);
                SetAnchor(_leaveButton.GetComponent<RectTransform>(), 0.02f, 0.93f, 0.18f, 0.99f, 0, 0, 0, 0);
            }

            WireButton(_leaveButton, () => OnLeavePressed?.Invoke());
            EnsureChallengeBanner();
        }

        public void Show()
        {
            gameObject.SetActive(true);
            EnsureBoard();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            ClearParkedSticks();
            ClearCandidates();
            ClearBubbles();
            ClearPlayLog();
        }

        public bool IsThrowVisible =>
            _throwZone != null && _throwZone.activeSelf && _throwZone.activeInHierarchy;

        public void SetLeaveVisible(bool on)
        {
            if (_leaveButton == null) return;
            _leaveButton.gameObject.SetActive(on);
            if (on) EnsureButtonLabel(_leaveButton, "나가기");
        }

        public readonly struct YokaiPieceInfo
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly int NodeId;

            public YokaiPieceInfo(string id, string displayName, int nodeId)
            {
                Id = id;
                DisplayName = displayName;
                NodeId = nodeId;
            }
        }

        // 웹(yokai-yut-garden) animateMove와 동일: 칸당 170ms ease-out + 완주 페이드 150ms.
        // (웹 step-land는 border만 살짝 바뀌는데, 우리 칸은 Image 채움이라 색을 건드리면 티가 너무 나서 생략)
        const float HopDuration = 0.17f;
        const float HopFinishFade = 0.15f;

        static readonly Dictionary<string, Sprite> PieceSpriteCache = new Dictionary<string, Sprite>();

        /// <summary>FlashCandidates 그룹 키 — 완주(날 경우) 후보는 참(0)이 아니라 동 구역으로 묶는다.</summary>
        const int FinishCandidateSlot = -1;

        public readonly struct YokaiMoveCandidate
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly int DestinationNode;
            public readonly bool UseShortcut;
            /// <summary>이번 이동으로 완주(참을 지나 날아감)하는지. true면 후보·반짝임을 동(東)에 띄운다.</summary>
            public readonly bool WillFinish;
            /// <summary>업힌 말 전원(대표 Id 포함). 한 마리면 길이 1. 후보 칸에 전원 초상을 그릴 때 쓴다.</summary>
            public readonly string[] StackMemberIds;
            /// <summary>StackMemberIds와 같은 길이의 표시 이름(이니셜 폴백용).</summary>
            public readonly string[] StackDisplayNames;

            public YokaiMoveCandidate(
                string id,
                string displayName,
                int destinationNode,
                bool useShortcut,
                string[] stackMemberIds = null,
                string[] stackDisplayNames = null,
                bool willFinish = false)
            {
                Id = id;
                DisplayName = displayName;
                DestinationNode = destinationNode;
                UseShortcut = useShortcut;
                WillFinish = willFinish;
                if (stackMemberIds != null && stackMemberIds.Length > 0)
                    StackMemberIds = stackMemberIds;
                else
                    StackMemberIds = new[] { id };

                if (stackDisplayNames != null && stackDisplayNames.Length == StackMemberIds.Length)
                    StackDisplayNames = stackDisplayNames;
                else
                {
                    StackDisplayNames = new string[StackMemberIds.Length];
                    for (int i = 0; i < StackMemberIds.Length; i++)
                        StackDisplayNames[i] = StackMemberIds[i] == id ? displayName : StackMemberIds[i];
                }
            }
        }

        // 한 칸에 후보가 여럿(대기 말 여럿이 같은 결과로 동시 입장 가능 등) 겹칠 때, 그 칸을
        // 중심으로 위/아래/왼쪽/오른쪽에 하나씩 붙여서 보여준다 — 팀이 최대 4마리라 방향 4개면 충분.
        static readonly Vector2[] CrossDirs = { new(0, 1), new(0, -1), new(-1, 0), new(1, 0) };

        readonly Dictionary<int, Color> _highlightedPadOriginals = new();
        Image _eastFinishHighlight;
        Color _eastFinishHighlightOriginal;
        bool _eastFinishHighlightActive;
        bool _padHighlightRunning;

        /// <summary>Prefab에 이미 있는 자식이면 레이아웃(앵커·색)은 건드리지 않고 그대로 쓴다.
        /// 없을 때만 코드 기본값으로 새로 만든다 — Prefab이 레이아웃 최종 소스.</summary>
        RectTransform FindOrCreatePanel(Transform parent, string name, float xmin, float ymin, float xmax, float ymax,
            Color color, out bool created)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing != null)
            {
                created = false;
                return existing;
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            SetAnchor(rt, xmin, ymin, xmax, ymax, 0, 0, 0, 0);
            rt.GetComponent<Image>().color = color;
            created = true;
            return rt;
        }

        void EnsureBoard()
        {
            if (_boardRoot != null && _pads != null) return;

            _boardRoot = FindOrCreatePanel(transform, "YutBoard", 0.1f, 0.2f, 0.9f, 0.65f,
                new Color(0.12f, 0.22f, 0.18f, 0.92f), out _);

            BindOrCreateHearts();
            EnsureTokenPlusButton();
            EnsureChallengeBanner();
            if (!TryBindPads())
                CreatePads();
            BindOrCreateOpponentPiece();

            EnsureQuadrants();
            EnsureOpponentMiniThrowPanel();
            EnsureRosterPanel();
            EnsurePlayLogButton();
            EnsurePlayLogPanel();
        }

        /// <summary>하트(윷 토큰) — Prefab에 있으면 그 자리를 유지하고, 없을 때만 코드 기본 좌표로 만든다.</summary>
        void BindOrCreateHearts()
        {
            if (_heartIcons != null) return;

            const int heartCount = 5;
            _heartIcons = new Image[heartCount];
            bool foundAll = true;
            for (int i = 0; i < heartCount; i++)
            {
                var t = transform.Find($"Heart{i}");
                if (t == null) { foundAll = false; break; }
                _heartIcons[i] = t.GetComponent<Image>();
            }

            if (foundAll)
                return;

            _heartIcons = new Image[heartCount];
            const float iconW = 0.035f;
            const float gap = 0.008f;
            float totalW = heartCount * iconW + (heartCount - 1) * gap;
            float startX = HeartsRightEdge - totalW;
            for (int i = 0; i < heartCount; i++)
            {
                var heartGo = new GameObject($"Heart{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                heartGo.transform.SetParent(transform, false);
                _heartIcons[i] = heartGo.GetComponent<Image>();
                float x = startX + i * (iconW + gap);
                SetAnchor(_heartIcons[i].rectTransform, x, 0.9f, x + iconW, 0.97f, 0, 0, 0, 0);
            }
        }

        bool TryBindPads()
        {
            if (_boardRoot == null || _boardRoot.Find("Node0") == null) return false;

            _pads = new Image[YutBoardLayout.NodeCount];
            for (int i = 0; i < YutBoardLayout.NodeCount; i++)
            {
                var t = _boardRoot.Find($"Node{i}");
                if (t == null)
                {
                    _pads = null;
                    return false;
                }
                _pads[i] = t.GetComponent<Image>();
            }

            // Prefab에 구운 보드 칸 위치는 유지하고, 특수 칸 색만 매치 규칙에 맞게 다시 칠한다.
            for (int i = 0; i < _pads.Length; i++)
                RecolorPad(_pads[i], i);
            return true;
        }

        static void RecolorPad(Image pad, int nodeId)
        {
            if (pad == null) return;
            pad.color = YutBoardLayout.IsSpecialReward(nodeId)
                ? new Color(0.35f, 0.55f, 0.85f, 0.9f) // 특수 칸 — 엽전/재료보따리/보물상자/물
                : IsWaypoint(nodeId)
                    ? new Color(0.7f, 0.55f, 0.3f, 0.85f)
                    : new Color(0.35f, 0.32f, 0.28f, 0.9f);
        }

        /// <summary>이무기 한 바퀴 시 특수 칸 재배치 연출 — from→to 한 쌍.</summary>
        public readonly struct SpecialSquareFlight
        {
            public readonly int FromNode;
            public readonly int ToNode;
            public readonly Sprite Icon;

            public SpecialSquareFlight(int fromNode, int toNode, Sprite icon)
            {
                FromNode = fromNode;
                ToNode = toNode;
                Icon = icon;
            }
        }

        void CreatePads()
        {
            _pads = new Image[YutBoardLayout.NodeCount];
            for (int i = 0; i < YutBoardLayout.NodeCount; i++)
            {
                var pos = YutBoardLayout.Normalized(i);
                float half = IsWaypoint(i) ? 0.05f : 0.032f;
                var padGo = new GameObject($"Node{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                padGo.transform.SetParent(_boardRoot, false);
                var padRt = padGo.GetComponent<RectTransform>();
                SetAnchor(padRt, pos.x - half, pos.y - half, pos.x + half, pos.y + half, 0, 0, 0, 0);
                var padImg = padGo.GetComponent<Image>();
                RecolorPad(padImg, i);
                _pads[i] = padImg;
            }
        }

        /// <summary>
        /// 두 대각선이 나누는 4개 삼각형 구역의 컨테이너를 만든다. 아직 내용은 비어 있고,
        /// 각 구역을 담당할 기능이 GetQuadrant()로 받아서 자기 UI를 채워 넣는 자리(베이스)다.
        /// </summary>
        void EnsureQuadrants()
        {
            if (_quadrants != null) return;

            _quadrants = new RectTransform[3];
            _quadrants[(int)YutBoardQuadrant.North] =
                FindOrCreateQuadrant("Quadrant_North", 0.42f, 0.49f, 0.63f, 0.575f);
            _quadrants[(int)YutBoardQuadrant.East] =
                FindOrCreateQuadrant("Quadrant_East", 0.67f, 0.32f, 0.87f, 0.53f);
            _quadrants[(int)YutBoardQuadrant.South] =
                FindOrCreateQuadrant("Quadrant_South", 0.3f, 0.22f, 0.7f, 0.33f);
        }

        RectTransform FindOrCreateQuadrant(string name, float xmin, float ymin, float xmax, float ymax)
        {
            // 투명 Image라도 raycastTarget=true면 보드 위 후보/말 클릭·드래그를 가로챈다.
            var rt = FindOrCreatePanel(transform, name, xmin, ymin, xmax, ymax, new Color(0, 0, 0, 0), out _);
            var img = rt.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
            return rt;
        }

        /// <summary>다른 기능(특수능력/완주말+보물 등)이 자기 UI를 붙일 구역 컨테이너.
        /// 대기말은 로스터 슬롯을 쓴다(South는 비움).</summary>
        public RectTransform GetQuadrant(YutBoardQuadrant quadrant)
        {
            EnsureBoard();
            return _quadrants[(int)quadrant];
        }

        /// <summary>동(東) 구역에 모은 아이템 한 칸 — 아이콘 + 개수.</summary>
        public struct CollectedItemView
        {
            public Sprite Icon;
            public int Count;
            public string Label;
            /// <summary>보통은 null(흰색 그대로). 보물상자 윷 토큰이 평소 상한을 넘긴 "보너스"일
            /// 때처럼 다른 색으로 눈에 띄게 표시하고 싶을 때만 채운다.</summary>
            public Color? Tint;
        }

        Transform _collectedItemsRoot;
        static Sprite _yeopjeonIcon;
        static Sprite _waterIcon;
        static Sprite _treasureChestOpenIcon;
        static Sprite _ingredientBagIcon;

        Transform _finishedPiecesRoot;
        /// <summary>동 구역 완주 초상 — 말풍선 앵커용 (pieceId → RectTransform).</summary>
        readonly Dictionary<string, RectTransform> _finishedPieceAnchors = new();

        // North 구역 로컬(0~1) 기준 — 구역을 Scene에서 옮겨도 결과가 같이 따라간다.
        static readonly Vector2[] ParkSpotsInZone =
        {
            new(0.14f, 0.5f), new(0.38f, 0.5f), new(0.62f, 0.5f), new(0.86f, 0.5f),
        };
        static readonly Vector2 ParkedStickSize = new(10f, 42f);

        // 뒤집힌(등 보임) 가락 개수 = 0(모)/1(도·빽도)/2(개)/3(걸)/4(윷).
        // 1개만 뒤집혔을 때, 그게 0번 "빽도 가락"이면 빽도, 다른 가락이면 도로 갈린다.

        void EnsureButtonLabel(Button button, string label)
        {
            if (button == null) return;
            var text = button.GetComponentInChildren<Text>(true);
            if (text == null)
            {
                text = CreateText(button.transform, "Label", label, 38, TextAnchor.MiddleCenter);
                UiRectUtils.Stretch(text.rectTransform);
                text.raycastTarget = false;
            }
            text.text = label;
            text.font = ResolveFont();
            text.fontSize = UiFonts.Size(39);
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
        }

        static void WireButton(Button btn, Action action)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => action?.Invoke());
        }

        Button CreateButton(Transform parent, string name, string label, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.25f, 0.22f, 0.18f, 0.95f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            var text = CreateText(go.transform, "Label", label, 34, TextAnchor.MiddleCenter);
            UiRectUtils.Stretch(text.rectTransform);
            text.raycastTarget = false;
            return btn;
        }

        Text CreateText(Transform parent, string name, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = ResolveFont();
            text.fontSize = UiFonts.Size(size + 1);
            text.color = Color.white;
            text.alignment = anchor;
            return text;
        }

        Font ResolveFont() => font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static void SetAnchor(RectTransform rt, float xmin, float ymin, float xmax, float ymax,
            float left, float bottom, float right, float top)
        {
            rt.anchorMin = new Vector2(xmin, ymin);
            rt.anchorMax = new Vector2(xmax, ymax);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(right, top);
        }

    }
}
