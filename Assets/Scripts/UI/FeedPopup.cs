using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.UI
{
    /// <summary>
    /// 먹이기 창 (v1.3) — 요괴를 먹이는 일은 이 창 하나에서 끝난다.
    /// 여는 법: 그릇 말풍선 탭, 노는 요괴 탭 (MapPointerRouter.FeedRequested).
    /// - 맨 위: 요괴 이름 · 친밀도 · 기력 막대 · '음식 n그릇이면 가득'
    /// - [아무거나 먹이기]: 고른 범위에서 많은 순, 황금 요리 · 나루터 혼령이 청한 요리는 건너뜀
    /// - [음식만 / 공양물 포함]: 기기에 기억. 공양물 포함이면 (공개된) 선호 공양물이 금빛으로 맨 앞
    /// - 목록: 많은 순. 황금 요리는 따로 한 칸. 혼령이 청한 요리는 '혼령' 표시(직접 누르면 먹일 수 있음)
    /// - 짧게 누르면 한 그릇, 0.3초 넘게 누르면 0.3초마다 한 그릇씩 — 기력이 가득 차거나 떨어지면 멈춤.
    ///   누른 채 손가락이 움직이면 스크롤로 보고 멈춤.
    /// 셸은 Prefab(Assets/Prefabs/UI/FeedPopup.prefab), 음식 칸 목록만 동적 콘텐츠로 코드로 만든다.
    /// </summary>
    public class FeedPopup : MonoBehaviour
    {
        public static FeedPopup Instance { get; private set; }
        public Font font;

        [SerializeField] GameObject root;
        [SerializeField] Text titleText, subText, giText, statusText, anyLabel, emptyText;
        [SerializeField] Image giFill;
        [SerializeField] Button anyButton, foodOnlyButton, inclButton, goKitchenButton, closeButton, dimButton;
        [SerializeField] RectTransform listContent;
        [SerializeField] ScrollRect scroll;

        const string PrefIncludeOfferings = "feed.includeOfferings";
        const float HoldDelay = 0.3f, HoldRepeat = 0.3f;

        CharacterAgent agent;
        readonly List<GameObject> cells = new List<GameObject>();
        readonly Dictionary<string, Text> cellCounts = new Dictionary<string, Text>();

        static readonly Color TextColor = new Color(1f, 0.95f, 0.85f);
        static readonly Color DimText = new Color(0.85f, 0.78f, 0.66f);
        static readonly Color CellColor = new Color(0.27f, 0.21f, 0.16f, 1f);
        static readonly Color GoldColor = new Color(0.95f, 0.75f, 0.25f, 1f);
        static readonly Color SegOn = new Color(0.78f, 0.58f, 0.25f, 1f);
        static readonly Color SegOff = new Color(0.32f, 0.25f, 0.19f, 1f);

        struct Entry
        {
            public OfferingData Data;
            public int Count;
            public bool Preferred, Ghost;
        }

        static bool IncludeOfferings
        {
            get { try { return PlayerPrefs.GetInt(PrefIncludeOfferings, 0) == 1; } catch { return false; } }
            set { try { PlayerPrefs.SetInt(PrefIncludeOfferings, value ? 1 : 0); PlayerPrefs.Save(); } catch { } }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (root != null) root.SetActive(false);
            MapPointerRouter.FeedRequested += Open;
        }

        void Start()
        {
            Wire(closeButton, Close);
            Wire(dimButton, Close);
            if (foodOnlyButton != null) { foodOnlyButton.onClick.RemoveAllListeners(); foodOnlyButton.onClick.AddListener(() => { IncludeOfferings = false; Refresh(); }); }
            if (inclButton != null) { inclButton.onClick.RemoveAllListeners(); inclButton.onClick.AddListener(() => { IncludeOfferings = true; Refresh(); }); }
            if (goKitchenButton != null)
            {
                goKitchenButton.onClick.RemoveAllListeners();
                goKitchenButton.onClick.AddListener(() => { Close(); GongyangganScreen.Resolve()?.Open(); });
            }
            if (anyButton != null)
            {
                var anyHold = anyButton.GetComponent<FeedHoldButton>();
                if (anyHold == null) anyHold = anyButton.gameObject.AddComponent<FeedHoldButton>();
                anyHold.Setup(this, null, null);
            }
        }

        static void Wire(Button b, UnityEngine.Events.UnityAction action)
        {
            if (b == null) return;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(action);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            MapPointerRouter.FeedRequested -= Open;
        }

        public bool IsOpen => root != null && root.activeSelf;

        public void Open(CharacterAgent target)
        {
            if (target == null || target.Stats.State == ActionState.Fainted || root == null) return;
            agent = target;
            statusText.text = "";
            root.SetActive(true);
            Refresh();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        public void Close()
        {
            StopHold();
            if (root != null) root.SetActive(false);
            agent = null;
        }

        // ---------------- 목록 ----------------

        List<Entry> BuildEntries()
        {
            var list = new List<Entry>();
            var eco = GameEconomy.Instance;
            if (eco == null || agent == null) return list;
            bool incl = IncludeOfferings;
            var ghostWants = GhostWants();
            var snap = new List<KeyValuePair<string, int>>();
            eco.CaptureOfferingCounts(snap);
            foreach (var kv in snap)
            {
                if (kv.Value <= 0) continue;
                var o = OfferingCatalog.Find(kv.Key);
                if (o == null || o.IsWater) continue;
                bool isFood = o.kind == OfferingKind.Food;
                if (!isFood && !incl) continue;
                bool pref = incl && !isFood && agent.IsPreferredOffering(o) && agent.Stats.IsPreferenceRevealed(o.offeringId);
                list.Add(new Entry { Data = o, Count = kv.Value, Preferred = pref, Ghost = ghostWants.Contains(o.BaseId) });
            }
            list.Sort((a, b) =>
            {
                int c = b.Preferred.CompareTo(a.Preferred);
                if (c != 0) return c;
                c = b.Data.golden.CompareTo(a.Data.golden);
                if (c != 0) return c;
                c = b.Count.CompareTo(a.Count);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Data.displayName, b.Data.displayName);
            });
            return list;
        }

        static HashSet<string> GhostWants()
        {
            var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var s in SpiritPier.Spirits) if (!string.IsNullOrEmpty(s.DishId)) set.Add(s.DishId);
            return set;
        }

        /// <summary>아무거나: 고른 범위에서 많은 순, 황금 요리 · 혼령이 청한 요리는 뺀다.</summary>
        Entry? AnyPick(List<Entry> entries)
        {
            Entry? best = null;
            foreach (var e in entries)
            {
                if (e.Data.golden || e.Ghost) continue;
                if (best == null || e.Count > best.Value.Count) best = e;
            }
            return best;
        }

        void Refresh()
        {
            if (agent == null || root == null) return;
            var stats = agent.Stats;
            float max = agent.MaxStamina;
            titleText.text = (agent.Data != null ? agent.Data.displayName : "요괴") + " 먹이기";
            subText.text = $"친밀도 {Mathf.FloorToInt(stats.Intimacy)} · 음식 +{GameSettings.FoodStamina} · 공양물 +{GameSettings.OfferingStamina}";
            giFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(max > 0 ? stats.Stamina / max : 0), 1f);
            int need = Mathf.Max(0, Mathf.CeilToInt((max - stats.Stamina) / Mathf.Max(1, GameSettings.FoodStamina)));
            giText.text = $"기력 {Mathf.FloorToInt(stats.Stamina)}/{Mathf.FloorToInt(max)}" + (need > 0 ? $" · 음식 {need}그릇이면 가득" : " · 가득");

            bool incl = IncludeOfferings;
            foodOnlyButton.image.color = incl ? SegOff : SegOn;
            inclButton.image.color = incl ? SegOn : SegOff;

            var entries = BuildEntries();
            var any = AnyPick(entries);
            anyButton.interactable = any != null;
            anyLabel.text = any != null ? $"아무거나 먹이기\n{any.Value.Data.displayName} ×{any.Value.Count}" : "아무거나 먹이기\n먹일 게 없어요";

            // 꾹 누르는 동안에는 칸을 다시 만들지 않는다 — 누르던 칸이 사라지면 손을 떼도 멈추지 않으므로
            if (holding)
            {
                var counts = new Dictionary<string, int>();
                foreach (var e in entries) counts[e.Data.offeringId] = e.Count;
                foreach (var kv in cellCounts)
                    kv.Value.text = "×" + (counts.TryGetValue(kv.Key, out var n) ? n : 0);
                return;
            }
            foreach (var c in cells) Destroy(c);
            cells.Clear();
            cellCounts.Clear();
            foreach (var e in entries) cells.Add(MakeCell(e));

            bool empty = entries.Count == 0;
            emptyText.gameObject.SetActive(empty);
            goKitchenButton.gameObject.SetActive(empty);
            emptyText.text = incl ? "창고에 요리가 없어요.\n화덕에서 요리판을 돌려 지어 오세요." : "창고에 음식이 없어요.\n화덕에서 요리판을 돌려 지어 오세요.";
        }

        GameObject MakeCell(Entry e)
        {
            var go = new GameObject("Dish_" + e.Data.offeringId, typeof(RectTransform));
            go.transform.SetParent(listContent, false);
            var bg = go.AddComponent<Image>();
            bg.color = CellColor;
            if (e.Preferred || e.Data.golden)
            {
                var outline = go.AddComponent<Outline>();
                outline.effectColor = GoldColor;
                outline.effectDistance = new Vector2(4, -4);
            }

            var icon = MakeRect("Icon", go.transform, new Vector2(0, 28), new Vector2(96, 96)).gameObject.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            if (e.Data.icon != null) icon.sprite = e.Data.icon;
            else icon.color = e.Data.kind == OfferingKind.Food ? new Color(0.75f, 0.55f, 0.3f) : new Color(0.6f, 0.45f, 0.7f);
            if (e.Data.golden) icon.color = new Color(1f, 0.88f, 0.45f);

            MakeText(go.transform, e.Data.displayName, 24, new Vector2(0, -45), new Vector2(180, 36), TextColor);
            cellCounts[e.Data.offeringId] = MakeText(go.transform, "×" + e.Count, 22, new Vector2(0, -75), new Vector2(180, 30), DimText);
            string tag = e.Preferred ? "좋아함" : e.Ghost ? "혼령" : null;
            if (tag != null)
            {
                var t = MakeText(go.transform, tag, 20, new Vector2(0, 80), new Vector2(180, 28), e.Preferred ? GoldColor : new Color(0.7f, 0.85f, 1f));
                t.fontStyle = FontStyle.Bold;
            }

            var hold = go.AddComponent<FeedHoldButton>();
            hold.Setup(this, scroll, e.Data);
            return go;
        }

        // ---------------- 먹이기 ----------------

        /// <summary>한 그릇. 성공하면 true. 가득·없음이면 상태 줄에 이유.</summary>
        internal bool FeedOnce(OfferingData o, bool fromHold)
        {
            var eco = GameEconomy.Instance;
            if (agent == null || o == null || eco == null) return false;
            // 꾹 누르기는 기력이 가득 차면 멈춘다 (공양물도)
            if (fromHold && agent.Stats.Stamina >= agent.MaxStamina - 0.001f)
            {
                statusText.text = $"{agent.Data?.displayName} · 기력 가득";
                return false;
            }
            var r = agent.TryFeed(o, eco);
            if (!r.Success)
            {
                statusText.text = r.Block switch
                {
                    FeedBlock.StaminaFull => $"{agent.Data?.displayName}{Josa(agent.Data?.displayName, "은", "는")} 배가 불러요",
                    FeedBlock.StaminaAndIntimacyFull => "기력도 친밀도도 가득이에요",
                    FeedBlock.FaintedNeedsWater => "기절했어요 — 물로 깨워 주세요",
                    _ => $"{o.displayName}{Josa(o.displayName, "이", "가")} 다 떨어졌어요",
                };
                Refresh();
                return false;
            }

            statusText.text = $"{o.displayName} · 기력 +{r.StaminaGain}" + (r.IntimacyGain > 0.001f ? $" · 친밀도 +{r.IntimacyGain:0.#}" : "")
                              + (r.GoldenBuff ? " · 황금!" : "");
            if (r.IntimacyGain > 0.001f) IntimacyHeartFx.PlayFromAgent(agent, r.IntimacyGain);
            if (!r.RequestFulfilled)
            {
                if (r.GoldenBuff) agent.SayCatalogLine(e => e.goldLines);
                else if (o.kind == OfferingKind.Food) agent.SayCatalogLine(e => e.fedLines);
                else agent.SayCatalogLine(e => e.offerLines);
            }
            Yoegoe.Save.GameSaveBridge.RequestSave();
            Refresh();
            return true;
        }

        void FeedAny(bool fromHold)
        {
            var any = AnyPick(BuildEntries());
            if (any == null) { statusText.text = "먹일 게 없어요"; return; }
            FeedOnce(any.Value.Data, fromHold);
        }

        static string Josa(string word, string withBatchim, string without)
        {
            if (string.IsNullOrEmpty(word)) return without;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return without;
            return (c - 0xAC00) % 28 != 0 ? withBatchim : without;
        }

        // ---------------- 꾹 누르기 ----------------

        OfferingData holdTarget;
        bool holdAny, holdFired;
        float holdTimer;
        bool holding;

        internal void BeginHold(OfferingData o, bool any)
        {
            holding = true; holdFired = false; holdTarget = o; holdAny = any;
            holdTimer = HoldDelay;
        }

        /// <summary>손을 뗌 — 꾹 누르기가 시작되기 전이면 한 그릇.</summary>
        internal void EndHold(bool cancelled)
        {
            if (!holding) return;
            bool tap = !holdFired && !cancelled;
            var target = holdTarget; bool any = holdAny;
            StopHold();
            if (!tap) return;
            if (any) FeedAny(false); else FeedOnce(target, false);
        }

        internal void StopHold()
        {
            bool was = holding;
            holding = false; holdTarget = null;
            if (was && IsOpen) Refresh(); // 누르는 동안 미뤄 둔 목록 정리
        }

        void Update()
        {
            if (!holding) return;
            if (!IsOpen) { StopHold(); return; }
            holdTimer -= Time.unscaledDeltaTime;
            if (holdTimer > 0f) return;
            holdFired = true;
            holdTimer = HoldRepeat;
            bool ok;
            if (holdAny)
            {
                var any = AnyPick(BuildEntries());
                ok = any != null && FeedOnce(any.Value.Data, true);
                if (any == null) statusText.text = "먹일 게 없어요";
            }
            else ok = FeedOnce(holdTarget, true);
            if (!ok) StopHold();
        }

        // ---------------- 동적 칸 (listContent 안) ----------------

        static RectTransform MakeRect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Font FontOrBuiltin()
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }

        Text MakeText(Transform parent, string msg, int size, Vector2 pos, Vector2 box, Color color)
        {
            var t = MakeRect("Text", parent, pos, box).gameObject.AddComponent<Text>();
            t.font = FontOrBuiltin();
            t.fontSize = UiFonts.Size(size);
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = msg;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }

    /// <summary>
    /// 먹이기 창 칸·[아무거나] 누르기: 짧게 = 한 그릇, 꾹 = 연속. 손가락이 움직이면 스크롤로 넘기고 멈춤.
    /// </summary>
    public class FeedHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        FeedPopup popup;
        ScrollRect scroll;
        OfferingData target;
        bool dragging;

        public void Setup(FeedPopup p, ScrollRect s, OfferingData o)
        {
            popup = p; scroll = s; target = o;
        }

        bool IsAny => target == null;

        public void OnPointerDown(PointerEventData e)
        {
            dragging = false;
            if (IsAny)
            {
                var b = GetComponent<Button>();
                if (b != null && !b.interactable) return;
            }
            popup.BeginHold(target, IsAny);
        }

        public void OnPointerUp(PointerEventData e) => popup.EndHold(dragging);

        // 누르던 칸이 꺼지면(창 닫힘 등) 연속 먹이기도 멈춘다
        void OnDisable() { if (popup != null) popup.StopHold(); }

        public void OnInitializePotentialDrag(PointerEventData e) => scroll?.OnInitializePotentialDrag(e);

        public void OnBeginDrag(PointerEventData e)
        {
            dragging = true;
            popup.StopHold();
            scroll?.OnBeginDrag(e);
        }

        public void OnDrag(PointerEventData e) => scroll?.OnDrag(e);
        public void OnEndDrag(PointerEventData e) => scroll?.OnEndDrag(e);
        public void OnScroll(PointerEventData e) => scroll?.OnScroll(e);
    }
}
