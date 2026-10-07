using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Cooking;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 기물 창 (v1.3). 보관함이 빈 기물 탭 · ▲ 딱지 탭 · 사냥터·채집터에 요괴를 끌어다 놓을 때 열린다.
    /// - 빈 기물: (사냥터·채집터) 목적지 3곳 → 보낼 요괴 → [보내기] · 레벨업
    /// - 일하는 중: 요괴 · 어디서 · 보관함 · 기력 → [불러들이기] [받기 n개] · 레벨업
    /// 친밀도가 모자란 요괴를 고르면 "거기는 가기 싫어." 하고 필요한 친밀도를 보여 준다.
    /// 코드로만 만든다(프리팹 없음).
    /// </summary>
    public class PropPanel : MonoBehaviour
    {
        public static PropPanel Instance { get; private set; }
        public Font font;

        PropSlot prop;
        string selectedDest;
        CharacterAgent selectedAgent;
        CharacterAgent refusing;
        float refuseUntil;

        GameObject root;
        RectTransform box, body;
        Text titleText, subText;

        static readonly Color BoxColor = new Color(0.14f, 0.1f, 0.08f, 0.98f);
        static readonly Color TextColor = new Color(1f, 0.95f, 0.85f);
        static readonly Color DimText = new Color(0.85f, 0.78f, 0.66f);
        static readonly Color CardOff = new Color(0.27f, 0.21f, 0.16f, 1f);
        static readonly Color CardOn = new Color(0.62f, 0.45f, 0.2f, 1f);
        static readonly Color CardDisabled = new Color(0.2f, 0.17f, 0.15f, 1f);
        static readonly Color Gold = new Color(0.95f, 0.75f, 0.25f, 1f);
        static readonly Color Warn = new Color(1f, 0.55f, 0.45f, 1f);
        static readonly Color Btn = new Color(0.3f, 0.5f, 0.45f, 1f);
        static readonly Color BtnOff = new Color(0.3f, 0.28f, 0.26f, 1f);

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            MapPointerRouter.PropPanelRequested += Open;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            MapPointerRouter.PropPanelRequested -= Open;
        }

        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>기물 창 열기. preselect = 끌어다 놓은 요괴(미리 골라 둠).</summary>
        public void Open(PropSlot target, CharacterAgent preselect)
        {
            if (target == null || !target.IsBuilt || !target.AcceptsWorkers) return;
            EnsureBuilt();
            prop = target;
            var dests = PropCatalog.DestinationsFor(prop.ResourceType);
            selectedDest = prop.DestinationId;
            if (prop.HasDestinations && (selectedDest == null || PropCatalog.FindDestination(selectedDest) == null))
                selectedDest = dests.Count > 0 ? dests[0].id : null;
            selectedAgent = preselect;
            refusing = null;
            root.SetActive(true);
            Rebuild();
            // 끌어다 놓은 요괴가 못 가는 곳이면 바로 물어본 것처럼
            if (preselect != null && WhyNot(preselect, out bool refuse) != null && refuse) Refuse(preselect);
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
            prop = null;
            selectedAgent = null;
        }

        float refreshTimer;
        bool lastOccupied;
        void Update()
        {
            if (!IsOpen || prop == null) return;
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = 1f;
            bool changed = false;
            if (refusing != null && Time.unscaledTime > refuseUntil) { refusing = null; changed = true; }
            // 일하는 중엔 1초마다 보관함·기력 갱신. 빈 기물은 고르는 중이라 상태가 바뀔 때만 (누르는 중 칸이 사라지지 않게)
            // 손가락이 눌린 동안엔 다시 그리지 않는다 — 누르던 버튼이 사라지면 탭이 씹히므로
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer != null && pointer.press.isPressed) return;
            if (prop.IsOccupied || prop.IsOccupied != lastOccupied || changed) Rebuild();
        }

        // ---------------- 규칙 ----------------

        static IEnumerable<CharacterAgent> Workers()
        {
            foreach (var a in CharacterAgent.All)
                if (a != null && a.Data != null && a.Data.id != CharacterId.Rabbit) yield return a; // 옥토끼는 떡절구만 (v1.3)
        }

        PropCatalog.Destination Dest => PropCatalog.FindDestination(selectedDest);

        /// <summary>못 보내는 이유 (null = 보낼 수 있음). refuse = 친밀도 부족(누르면 거절 대사).</summary>
        string WhyNot(CharacterAgent a, out bool refuse)
        {
            refuse = false;
            if (a.Stats.State == ActionState.Fainted) return "기절했어요\n물로 깨워 주세요";
            var busy = a.CurrentProp;
            if (a.Stats.State == ActionState.Staying && busy != null && busy != prop) return busy.DisplayName + "에서\n일하는 중";
            if (a.Stats.Stamina < 1f) return "지쳐서 쉬는 중\n먹여 주세요";
            if (prop.data != null && prop.data.isEndingProp && prop.data.owner != a.Data.id) return "이 기물은\n못 써요";
            var d = Dest;
            if (d != null && a.Stats.Intimacy + 0.001f < d.minIntimacy)
            {
                refuse = true;
                return $"친밀도 {Mathf.FloorToInt(a.Stats.Intimacy)}/{d.minIntimacy:0}\n누르면 물어봐요";
            }
            return null;
        }

        void Refuse(CharacterAgent a)
        {
            refusing = a;
            refuseUntil = Time.unscaledTime + 2.6f;
            selectedAgent = null;
            a.ShowTempSpeech(GameSettingsText.Refuse);
        }

        void Send()
        {
            if (prop == null || selectedAgent == null) return;
            var a = selectedAgent;
            if (WhyNot(a, out _) != null) { Rebuild(); return; }
            if (prop.IsOccupied || prop.IsStorageHalted) { Rebuild(); return; }
            if (prop.HasDestinations) prop.DestinationId = selectedDest;
            if (a.TrySitOnProp(prop))
            {
                a.SayCatalogLine(e => e.GoLinesFor(prop.ResourceType));
                GameSaveBridge.RequestSave();
            }
            selectedAgent = null;
            Rebuild();
        }

        void Recall()
        {
            var a = prop != null ? prop.Occupant : null;
            if (a == null) return;
            a.EnterPlaying();
            a.SayCatalogLine(e => e.homeLines);
            GameSaveBridge.RequestSave();
            Rebuild();
        }

        void Take()
        {
            if (prop != null && prop.HasPendingCollectible && prop.TryCollect()) GameSaveBridge.RequestSave();
            Rebuild();
        }

        string levelNote;
        void LevelUp()
        {
            if (prop == null) return;
            if (!PropEconomy.TryUpgrade(prop)) { Rebuild(); return; }
            PropUpgradeFx.SpawnWorld(prop.transform.position, "+1 급", font);
            levelNote = prop.ResourceType == PropResourceType.Merit
                ? $"레벨업! 보관 공덕 {((BigNumber)prop.MeritCapacity).ToDisplayString()}"
                : prop.IsResourceProp && prop.level % 10 == 0
                ? "레벨업! 보관함의 총량이 +1 되었어요"
                : $"{prop.DisplayName} Lv{prop.level}";
            GameSaveBridge.SaveFromWorld();
            Rebuild();
        }

        // ---------------- 화면 ----------------

        void Rebuild()
        {
            if (prop == null || body == null) return;
            for (int i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);

            lastOccupied = prop.IsOccupied;
            titleText.text = prop.DisplayName;
            subText.text = SubLine();
            if (prop.IsOccupied) BuildBusy(); else BuildFree();
        }

        string SubLine()
        {
            var c = prop.ProductionConfig;
            if (c.Type == PropResourceType.Merit)
            {
                BigNumber per15 = PropProduction.BaseMeritPerMinute(c, prop.level) * 15.0;
                return $"Lv{prop.level} · 15분에 공덕 {per15.ToDisplayString()} · 보관 {((BigNumber)prop.MeritCapacity).ToDisplayString()}";
            }
            return $"Lv{prop.level} · {c.CycleMinutes:0}분에 1개 · 보관 {prop.ResourceCapacity}";
        }

        void BuildFree()
        {
            float y = 470f;
            var dests = PropCatalog.DestinationsFor(prop.ResourceType);
            if (prop.HasDestinations && dests.Count > 0)
            {
                Label("어디로 보낼까요?  목적지마다 나오는 재료가 달라요", new Vector2(0, y), 26, DimText);
                y -= 150f;
                float w = 270f, gap = 18f, total = dests.Count * w + (dests.Count - 1) * gap;
                for (int i = 0; i < dests.Count; i++)
                {
                    var d = dests[i];
                    var card = Card(new Vector2(-total / 2f + w / 2f + i * (w + gap), y), new Vector2(w, 230),
                        d.id == selectedDest ? CardOn : CardOff, () => { selectedDest = d.id; refusing = null; Rebuild(); });
                    TextIn(card, d.name, new Vector2(0, 80), 30, TextColor, FontStyle.Bold);
                    TextIn(card, d.minIntimacy > 0 ? $"♥ 친밀도 {d.minIntimacy:0} 이상" : "누구나 · 희귀도 하", new Vector2(0, 40), 22, DimText);
                    TextIn(card, DropLine(d.id), new Vector2(0, -30), 22, TextColor, FontStyle.Normal, new Vector2(w - 16, 90));
                    if (d.goldenChance > 0f) TextIn(card, $"황금 {d.goldenChance:0.#}%", new Vector2(0, -92), 22, Gold, FontStyle.Bold);
                }
                y -= 160f;
            }

            Label("누구를 보낼까요?  보관함이 차면 빈 옹달샘·제단으로 옮겨 가요", new Vector2(0, y), 26, DimText);
            y -= 140f;
            var workers = new List<CharacterAgent>(Workers());
            if (selectedAgent != null && WhyNot(selectedAgent, out _) != null) selectedAgent = null;
            float cw = 260f, cg = 16f, ct = workers.Count * cw + Mathf.Max(0, workers.Count - 1) * cg;
            for (int i = 0; i < workers.Count; i++)
            {
                var a = workers[i];
                string why = WhyNot(a, out bool refuse);
                bool on = a == selectedAgent;
                var color = on ? CardOn : (why != null && !refuse ? CardDisabled : CardOff);
                var card = Card(new Vector2(-ct / 2f + cw / 2f + i * (cw + cg), y), new Vector2(cw, 210), color, () =>
                {
                    if (refuse) { Refuse(a); Rebuild(); return; }
                    if (why != null) return;
                    selectedAgent = selectedAgent == a ? null : a;
                    Rebuild();
                });
                TextIn(card, a.Data.displayName, new Vector2(0, 70), 30, TextColor, FontStyle.Bold);
                TextIn(card, $"친밀도 {Mathf.FloorToInt(a.Stats.Intimacy)} · 기력 {Mathf.FloorToInt(a.Stats.Stamina)}/{Mathf.FloorToInt(a.MaxStamina)}",
                    new Vector2(0, 25), 21, DimText, FontStyle.Normal, new Vector2(cw - 10, 40));
                TextIn(card, why ?? "갈 수 있어요", new Vector2(0, -45), 22, refuse ? Warn : (why != null ? DimText : Gold), FontStyle.Normal, new Vector2(cw - 10, 80));
            }
            y -= 150f;

            string summary;
            var dd = Dest;
            if (refusing != null && dd != null)
                summary = $"{refusing.Data.displayName} “{GameSettingsText.Refuse}”  {dd.name}{Josa(dd.name, "은", "는")} 친밀도 {dd.minIntimacy:0}부터 가요 · 지금 {Mathf.FloorToInt(refusing.Stats.Intimacy)}";
            else if (prop.IsStorageHalted)
                summary = "보관함이 가득 찼어요. 먼저 받아 주세요.";
            else if (selectedAgent != null)
            {
                float gi = selectedAgent.Stats.Stamina;
                float cycle, fillMin;
                if (prop.ResourceType == PropResourceType.Merit)
                {
                    double perMin = PropProduction.BaseMeritPerMinute(prop.ProductionConfig, prop.level);
                    cycle = 15f;
                    fillMin = perMin > 0 ? (float)((prop.MeritCapacity - prop.PendingMerit.ToDouble()) / perMin) : 0f;
                }
                else
                {
                    int room = Mathf.Max(0, prop.ResourceCapacity - prop.StoredResources);
                    cycle = prop.ProductionConfig.CycleMinutes;
                    fillMin = room * cycle;
                }
                float workMin = gi * GameSettings.Get("staminaDrainMinutes");
                summary = workMin <= fillMin
                    ? $"기력 {Mathf.FloorToInt(gi)}이면 약 {Dur(workMin)} 일하고 쉬어요 ({Mathf.FloorToInt(workMin / Mathf.Max(1f, cycle))}개쯤)"
                    : $"약 {Dur(fillMin)}이면 보관함이 차고, 남은 기력으로 빈 옹달샘·제단에 옮겨 가요";
            }
            else summary = (dd != null ? dd.name + "(으)로 " : "") + "보낼 요괴를 골라 주세요." + (prop.StoredResources > 0 ? $"  보관함에 {prop.StoredResources}개가 있어요." : "");
            Label(summary, new Vector2(0, y), 24, refusing != null || prop.IsStorageHalted ? Warn : TextColor, new Vector2(860, 70));
            y -= 100f;

            bool canSend = selectedAgent != null && !prop.IsStorageHalted;
            Button(selectedAgent != null ? selectedAgent.Data.displayName + " 보내기" : "보내기", new Vector2(0, y), new Vector2(380, 90),
                canSend ? Btn : BtnOff, canSend ? Send : (System.Action)null);
            y -= 120f;
            if (prop.HasPendingCollectible)
            {
                Button(TakeLabel(), new Vector2(0, y), new Vector2(340, 70), Btn, Take);
                y -= 90f;
            }
            BuildLevelRow(y);
        }

        void BuildBusy()
        {
            var a = prop.Occupant;
            float y = 420f;
            string where = prop.HasDestinations && PropCatalog.FindDestination(prop.DestinationId) is var pd && pd != null
                ? pd.name + "에서" : prop.DisplayName + "에서";
            Label($"{a.Data.displayName} · {where} 일하는 중", new Vector2(0, y), 32, TextColor);
            y -= 90f;

            int cap = prop.ResourceCapacity, n = prop.StoredResources;
            if (prop.ResourceType == PropResourceType.Merit)
            {
                double mc = prop.MeritCapacity, mp = prop.PendingMerit.ToDouble();
                Label($"쌓인 공덕  {prop.PendingMerit.ToDisplayString()} / {((BigNumber)mc).ToDisplayString()}" + (prop.IsStorageHalted ? "  (가득 — 받아 주세요)" : ""),
                    new Vector2(0, y), 28, prop.IsStorageHalted ? Warn : TextColor);
                y -= 60f;
                Bar(new Vector2(0, y), mc > 0 && !double.IsInfinity(mc) ? (float)(mp / mc) : 0f, Gold);
                y -= 70f;
                n = prop.HasPendingMerit ? 1 : 0;
            }
            else if (prop.IsResourceProp)
            {
                Label($"보관함  {n}/{cap}" + (prop.IsStorageHalted ? "  (가득 — 받아 주세요)" : ""), new Vector2(0, y), 28, prop.IsStorageHalted ? Warn : TextColor);
                y -= 60f;
                Bar(new Vector2(0, y), cap > 0 ? n / (float)cap : 0f, Gold);
                y -= 70f;
            }
            float max = a.MaxStamina;
            Label($"기력  {Mathf.FloorToInt(a.Stats.Stamina)}/{Mathf.FloorToInt(max)}", new Vector2(0, y), 28, TextColor);
            y -= 60f;
            Bar(new Vector2(0, y), max > 0 ? a.Stats.Stamina / max : 0f, new Color(0.45f, 0.75f, 0.4f));
            y -= 80f;

            if (prop.IsResourceProp)
            {
                prop.CaptureStorage(out _, out float progress, out _, out _);
                float left = Mathf.Max(1f, prop.ProductionConfig.CycleMinutes - progress / 60f);
                string gold = a.IsGolden ? "  (황금 요리 효과 중)" : "";
                Label($"다음 것까지 약 {left:0}분 · 보관함이 차면 남은 기력으로 빈 옹달샘·제단에 옮겨 가요{gold}",
                    new Vector2(0, y), 24, DimText, new Vector2(860, 70));
                y -= 110f;
            }

            Button("불러들이기", new Vector2(-170, y), new Vector2(300, 90), BtnOff, Recall);
            Button(n > 0 ? TakeLabel() : "받기", new Vector2(170, y), new Vector2(300, 90), n > 0 ? Btn : BtnOff, n > 0 ? Take : (System.Action)null);
            y -= 130f;
            BuildLevelRow(y);
            Label("기물마다 한 번에 한 마리만 일해요.", new Vector2(0, -600), 22, DimText);
        }

        void BuildLevelRow(float y)
        {
            if (!PropEconomy.CanUpgrade(prop)) return;
            var cost = PropEconomy.GetUpgradeCost(prop);
            var eco = GameEconomy.Instance;
            bool ok = eco != null && eco.MeritPile >= cost;
            string have = eco != null ? eco.MeritPile.ToDisplayString() : "0";
            Label(prop.IsResourceProp ? $"지금 Lv{prop.level} · 보관함 {prop.ResourceCapacity}칸"
                  : prop.ResourceType == PropResourceType.Merit ? $"지금 Lv{prop.level} · 보관 공덕 {((BigNumber)prop.MeritCapacity).ToDisplayString()} (레벨마다 ×{prop.ProductionConfig.CapacityGrowth:0.##})"
                  : $"지금 Lv{prop.level}", new Vector2(0, y), 24, DimText);
            y -= 70f;
            var b = Button($"레벨업 Lv{prop.level} → {prop.level + 1}", new Vector2(0, y), new Vector2(420, 80), ok ? Gold * 0.85f : BtnOff, ok ? LevelUp : (System.Action)null);
            y -= 60f;
            Label(ok ? $"공덕 {cost.ToDisplayString()}" : $"공덕 {cost.ToDisplayString()} 필요 · 지금 {have}", new Vector2(0, y), 22, ok ? Gold : Warn);
            if (!string.IsNullOrEmpty(levelNote))
            {
                Label(levelNote, new Vector2(0, y - 40), 24, Gold);
                levelNote = null;
            }
        }

        string TakeLabel() => prop.ResourceType == PropResourceType.Merit
            ? $"받기 공덕 {prop.PendingMerit.ToDisplayString()}"
            : $"받기 {prop.StoredResources}개";

        static string DropLine(string destId)
        {
            var sb = new StringBuilder();
            foreach (var (code, w) in PropCatalog.DestinationDrops(destId))
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(PropCatalog.IsSpecialCode(code) ? PropSlot.SpecialItemName(PropCatalog.SpecialOf(code))
                    : CookingRecipeCatalog.DisplayName((CookingIngredientId)code));
                sb.Append(' ').Append(w.ToString("0"));
            }
            return sb.ToString();
        }

        static string Dur(float minutes)
        {
            int m = Mathf.Max(0, Mathf.RoundToInt(minutes));
            if (m < 60) return m + "분";
            int h = m / 60, mm = m % 60;
            return mm > 0 ? $"{h}시간 {mm}분" : $"{h}시간";
        }

        static string Josa(string word, string withBatchim, string without)
        {
            if (string.IsNullOrEmpty(word)) return without;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return without;
            return (c - 0xAC00) % 28 != 0 ? withBatchim : without;
        }

        // ---------------- 만들기 ----------------

        void EnsureBuilt()
        {
            if (root != null) return;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("Canvas_PropPanel");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 870;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            root = new GameObject("Panel", typeof(RectTransform));
            var rootRt = (RectTransform)root.transform;
            rootRt.SetParent(canvasGO.transform, false);
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            var dim = root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            var dimBtn = root.AddComponent<UnityEngine.UI.Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Close);

            box = Rect("Box", rootRt, Vector2.zero, new Vector2(940, 1360));
            box.gameObject.AddComponent<Image>().color = BoxColor;
            box.gameObject.AddComponent<UnityEngine.UI.Button>().transition = Selectable.Transition.None;

            titleText = TextIn(box, "", new Vector2(0, 620), 44, TextColor, FontStyle.Bold, new Vector2(700, 70));
            subText = TextIn(box, "", new Vector2(0, 560), 26, DimText, FontStyle.Normal, new Vector2(800, 44));
            var x = Rect("Close", box, new Vector2(415, 625), new Vector2(80, 80));
            var xi = x.gameObject.AddComponent<Image>();
            xi.color = new Color(0.35f, 0.27f, 0.2f);
            var xb = x.gameObject.AddComponent<UnityEngine.UI.Button>();
            xb.targetGraphic = xi;
            xb.onClick.AddListener(Close);
            TextIn(x, "X", Vector2.zero, 36, TextColor, FontStyle.Bold, new Vector2(80, 80));

            body = Rect("Body", box, Vector2.zero, new Vector2(940, 1360));
            root.SetActive(false);
        }

        static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Text TextIn(Transform parent, string msg, Vector2 pos, int size, Color color, FontStyle style = FontStyle.Normal, Vector2? box = null)
        {
            var t = Rect("Text", parent, pos, box ?? new Vector2(860, 50)).gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = UiFonts.Size(size);
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = msg;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        Text Label(string msg, Vector2 pos, int size, Color color, Vector2? boxSize = null) =>
            TextIn(body, msg, pos, size, color, FontStyle.Normal, boxSize ?? new Vector2(880, 50));

        RectTransform Card(Vector2 pos, Vector2 size, Color color, System.Action onClick)
        {
            var rt = Rect("Card", body, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var b = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return rt;
        }

        UnityEngine.UI.Button Button(string label, Vector2 pos, Vector2 size, Color color, System.Action onClick)
        {
            var rt = Rect("Button", body, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var b = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            b.interactable = onClick != null;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            TextIn(rt, label, Vector2.zero, 28, TextColor, FontStyle.Bold, size);
            return b;
        }

        void Bar(Vector2 pos, float t, Color color)
        {
            var bg = Rect("Bar", body, pos, new Vector2(760, 26));
            bg.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.06f, 0.05f, 1f);
            var fill = Rect("Fill", bg, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<Image>().color = color;
        }
    }

    /// <summary>기물 창 문구 (시트로 옮길 후보).</summary>
    static class GameSettingsText
    {
        public const string Refuse = "거기는 가기 싫어.";
    }
}
