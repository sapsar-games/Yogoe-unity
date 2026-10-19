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
    /// 셸은 Prefab(Assets/Prefabs/UI/PropPanel.prefab), 목적지·요괴 칸 등 동적 콘텐츠만 코드로 만든다.
    /// </summary>
    public class PropPanel : SingletonBehaviour<PropPanel>
    {
        public Font font;

        [SerializeField] GameObject root;
        [SerializeField] RectTransform body;
        [SerializeField] Text titleText;
        [SerializeField] Text subText;
        [SerializeField] Button dimButton;
        [SerializeField] Button closeButton;

        PropSlot prop;             // 지금 열려 있는 기물 (허브가 아니라 실제 앉을 자리로 치환된 뒤의 값)
        string selectedDest;       // 고른 목적지 id (사냥터·채집터일 때만 의미 있음)
        CharacterAgent selectedAgent; // 보낼 요괴로 고른 카드
        CharacterAgent refusing;   // 방금 "거기는 가기 싫어"라고 거절한 요괴 (말풍선 잠깐 보여주는 용도)
        float refuseUntil;         // refusing을 지울 시각 (Time.unscaledTime 기준)

        // 카드·버튼 배경색 팔레트
        static readonly Color TextColor = new Color(1f, 0.95f, 0.85f);
        static readonly Color DimText = new Color(0.85f, 0.78f, 0.66f);
        static readonly Color CardOff = new Color(0.27f, 0.21f, 0.16f, 1f);
        static readonly Color CardOn = new Color(0.62f, 0.45f, 0.2f, 1f);
        static readonly Color CardDisabled = new Color(0.2f, 0.17f, 0.15f, 1f);
        static readonly Color Gold = new Color(0.95f, 0.75f, 0.25f, 1f);
        static readonly Color Warn = new Color(1f, 0.55f, 0.45f, 1f);
        static readonly Color Btn = new Color(0.3f, 0.5f, 0.45f, 1f);
        static readonly Color BtnOff = new Color(0.3f, 0.28f, 0.26f, 1f);

        protected override void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            base.Awake();
            if (root != null) root.SetActive(false);
            MapPointerRouter.PropPanelRequested += Open;
        }

        void Start()
        {
            UiButtonUtils.Bind(dimButton, Close);
            UiButtonUtils.Bind(closeButton, Close);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            MapPointerRouter.PropPanelRequested -= Open;
        }

        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>기물 창 열기. preselect = 끌어다 놓은 요괴(미리 골라 둠).</summary>
        public void Open(PropSlot target, CharacterAgent preselect)
        {
            if (target == null || !target.IsBuilt || !target.AcceptsWorkers || root == null) return;
            prop = ResolveOpenTarget(target);
            prop.ApplyFixedDestination();
            var dests = PropCatalog.DestinationsFor(prop.ResourceType);
            selectedDest = prop.DestinationId;
            if (prop.NeedsDestinationPick && (selectedDest == null || PropCatalog.FindDestination(selectedDest) == null))
                selectedDest = dests.Count > 0 ? dests[0].id : null;
            else if (prop.HasFixedDestination)
                selectedDest = prop.DestinationId;
            selectedAgent = preselect;
            refusing = null;
            root.SetActive(true);
            Rebuild();
            // 끌어다 놓은 요괴가 못 가는 곳이면 바로 물어본 것처럼
            if (preselect != null && WhyNot(preselect, out bool refuse) != null && refuse) Refuse(preselect);
        }

        /// <summary>사냥터·채집터(허브)를 열었는데 그 목적지 중 하나가 이미 일하는 중이면
        /// 그 개별 기물(멧돼지 앞산 등)을 바로 보여준다 — 허브 자신은 앉는 자리가 아니다.
        /// 개별 기물을 직접 열었으면(일하는 중 탭 등) 그대로.</summary>
        static PropSlot ResolveOpenTarget(PropSlot target)
        {
            if (!target.NeedsDestinationPick || PropManager.Instance == null) return target;
            foreach (var p in PropManager.Instance.All)
                if (p != null && p != target && p.ResourceType == target.ResourceType
                    && (p.IsOccupied || p.HasPendingCollectible)) return p;
            return target;
        }

        /// <summary>창 닫기. 선택 상태(selectedAgent 등)도 같이 비운다.</summary>
        public void Close()
        {
            if (root != null) root.SetActive(false);
            prop = null;
            selectedAgent = null;
        }

        float refreshTimer;
        bool lastOccupied;
        /// <summary>창이 열려 있는 동안 1초마다 내용을 갱신 (보관함·기력 등은 시간이 지나면 바뀌므로).
        /// 일하는 중(IsOccupied)이 아니면 누르던 칸이 사라지지 않게 상태가 실제로 바뀔 때만 다시 그린다.</summary>
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

        // ---------------- 규칙 (누구를 보낼 수 있는지, 보내면 무슨 일이 일어나는지) ----------------

        /// <summary>"보낼 요괴" 칸에 올라올 후보 전체 (옥토끼 제외 — 옥토끼는 떡절구 전용, v1.3).</summary>
        static IEnumerable<CharacterAgent> Workers()
        {
            foreach (var a in CharacterAgent.All)
                if (a != null && a.Data != null && a.Data.id != CharacterId.Rabbit) yield return a; // 옥토끼는 떡절구만 (v1.3)
        }

        /// <summary>지금 고른 목적지의 destinations 시트 데이터 (이름·친밀도·드랍률 등).</summary>
        PropCatalog.Destination Dest => PropCatalog.FindDestination(selectedDest);

        /// <summary>못 보내는 이유 (null = 보낼 수 있음). refuse = 친밀도 부족(누르면 거절 대사).</summary>
        string WhyNot(CharacterAgent a, out bool refuse)
        {
            refuse = false;
            var s = a.Stats;
            var busy = a.CurrentProp;
            var d = Dest;

            bool fainted = s.State == ActionState.Fainted;
            bool workingElsewhere = s.State == ActionState.Staying && busy != null && busy != prop;
            bool exhausted = s.Stamina < 1f;
            bool endingLocked = prop.data != null && prop.data.isEndingProp && prop.data.owner != a.Data.id;
            bool intimacyTooLow = d != null && s.Intimacy + 0.001f < d.minIntimacy;

            if (fainted) return "기절했어요\n물로 깨워 주세요";
            if (workingElsewhere) return busy.DisplayName + "에서\n일하는 중";
            if (exhausted) return "지쳐서 쉬는 중\n먹여 주세요";
            if (endingLocked) return "이 기물은\n못 써요";
            if (intimacyTooLow)
            {
                refuse = true;
                return $"친밀도 {Mathf.FloorToInt(s.Intimacy)}/{d.minIntimacy:0}\n누르면 물어봐요";
            }
            return null;
        }

        /// <summary>친밀도가 모자란 요괴 카드를 눌렀을 때 — 거절 말풍선을 2.6초 띄우고 선택은 취소한다.</summary>
        void Refuse(CharacterAgent a)
        {
            refusing = a;
            refuseUntil = Time.unscaledTime + 2.6f;
            selectedAgent = null;
            a.ShowTempSpeech(GameSettingsText.Refuse);
        }

        /// <summary>[보내기] 버튼. 고른 요괴를 고른 목적지의 실제 기물로 걷게 한다 (CharacterAgent.WalkToProp).</summary>
        void Send()
        {
            if (prop == null || selectedAgent == null) return;
            var a = selectedAgent;
            if (WhyNot(a, out _) != null) { Rebuild(); return; }
            // 고른 목적지(selectedDest)와 propId가 같은 실제 기물로 걸어간다 —
            // 드래그로 건드린 기물(prop)이 아니라 고른 목적지가 진짜 자리다.
            var seatProp = ResolveSeatProp(prop, selectedDest);
            if (seatProp == null || seatProp.IsOccupied || seatProp.IsStorageHalted) { Rebuild(); return; }
            if (seatProp.HasDestinations) seatProp.DestinationId = selectedDest;
            if (a.WalkToProp(seatProp))
            {
                a.SayCatalogLine(e => e.GoLinesFor(seatProp.ResourceType));
                GameSaveBridge.RequestSave();
                Close(); // 걸어가는 중이라 도착 전까진 IsOccupied가 안 바뀌어 화면이 그대로 보이므로, 보내면 바로 닫는다
                return;
            }
            selectedAgent = null;
            Rebuild();
        }

        /// <summary>목적지 id(destinations 탭)와 fixedDestinationId가 같은 실제 기물 —
        /// 없으면(목적지 선택 전 등) 드래그로 건드린 기물 그대로. propId가 아니라 fixedDestinationId로
        /// 맞춰야 한다 — propId는 한글 이름(예: 멧돼지 앞산), destId는 영문 id(예: BoarHill)라 서로 다르다.</summary>
        static PropSlot ResolveSeatProp(PropSlot touched, string destId)
        {
            if (string.IsNullOrEmpty(destId)) return touched;
            if (touched != null && touched.data != null && touched.data.fixedDestinationId == destId) return touched;
            if (PropManager.Instance == null) return touched;
            foreach (var p in PropManager.Instance.All)
                if (p != null && p.data != null && p.data.fixedDestinationId == destId) return p;
            return touched;
        }

        /// <summary>[불러들이기] 버튼. 일하던 요괴를 내려오게 하고(EnterPlaying) 점유를 비운다.</summary>
        void Recall()
        {
            var a = prop != null ? prop.Occupant : null;
            if (a == null) return;
            a.EnterPlaying();
            a.SayCatalogLine(e => e.homeLines);
            GameSaveBridge.RequestSave();
            Rebuild();
        }

        /// <summary>[받기] 버튼. 쌓인 보관함/공덕을 수거한다.</summary>
        void Take()
        {
            if (prop != null && prop.HasPendingCollectible && prop.TryCollect()) GameSaveBridge.RequestSave();
            Rebuild();
        }

        string levelNote; // 레벨업 직후 한 번만 보여줄 안내 문구 (다음 Rebuild에서 지워짐)
        /// <summary>[레벨업] 버튼. 비용을 내고 레벨을 올린다 (실패하면 그냥 다시 그리기만).</summary>
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

        // ---------------- 화면 (body 안의 동적 콘텐츠를 매번 지우고 다시 그림) ----------------

        /// <summary>body를 싹 비우고 지금 상태(점유 여부)에 맞는 화면을 새로 그린다.
        /// 칸 개수가 상태마다 달라서(목적지 수·요괴 수 등) Prefab 대신 매번 코드로 그리는 방식.</summary>
        void Rebuild()
        {
            if (prop == null || body == null || titleText == null || subText == null) return;
            for (int i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);

            lastOccupied = prop.IsOccupied;
            titleText.text = prop.DisplayName;
            subText.text = SubLine();
            if (prop.IsOccupied) BuildBusy(); else BuildFree();
        }

        /// <summary>제목 밑 한 줄 요약 — 레벨·생산 주기·보관량.</summary>
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

        /// <summary>비어 있는 기물 화면: (사냥터·채집터면) 목적지 3칸 → 보낼 요괴 카드들 → 상황 요약 →
        /// [보내기]/[받기] → 레벨업. y를 계속 깎아가며 위에서 아래로 쌓는 수동 레이아웃.</summary>
        void BuildFree()
        {
            float y = 470f;
            // v1.3: 사냥터·채집터는 어느 개별 기물(멧돼지 앞산 등)에 드래그해도 똑같이
            // "어디로 보낼까요" 3곳 선택창이 뜬다 — 6개는 배경일 뿐, 실제 목적지는 여기서 고른다.
            // (고른 destId와 같은 propId를 가진 실제 기물에 앉힌다 — ResolveSeatProp.)
            var dests = PropCatalog.DestinationsFor(prop.ResourceType);
            if (dests.Count > 0)
            {
                Label("어디로 보낼까요?  목적지마다 나오는 재료가 달라요", new Vector2(0, y), 26, DimText);
                y -= 150f;
                float w = 270f, gap = 18f, total = dests.Count * w + (dests.Count - 1) * gap;
                for (int i = 0; i < dests.Count; i++)
                {
                    var d = dests[i];
                    var destSeatProp = ResolveSeatProp(prop, d.id);
                    bool destBusy = destSeatProp != null && destSeatProp != prop && destSeatProp.IsOccupied;
                    var card = Card(new Vector2(-total / 2f + w / 2f + i * (w + gap), y), new Vector2(w, 230),
                        destBusy ? CardDisabled : d.id == selectedDest ? CardOn : CardOff,
                        destBusy ? null : () => { selectedDest = d.id; refusing = null; Rebuild(); });
                    TextIn(card, d.name, new Vector2(0, 80), 30, TextColor, FontStyle.Bold);
                    TextIn(card, d.minIntimacy > 0 ? $"♥ 친밀도 {d.minIntimacy:0} 이상" : "누구나 · 희귀도 하", new Vector2(0, 40), 22, DimText);
                    if (destBusy)
                        TextIn(card, "다른 요괴가 일하는 중", new Vector2(0, -30), 22, DimText, FontStyle.Normal, new Vector2(w - 16, 90));
                    else
                        TextIn(card, DropLine(d.id), new Vector2(0, -30), 22, TextColor, FontStyle.Normal, new Vector2(w - 16, 90));
                    if (!destBusy && d.goldenChance > 0f) TextIn(card, $"황금 {d.goldenChance:0.#}%", new Vector2(0, -92), 22, Gold, FontStyle.Bold);
                }
                y -= 160f;
            }
            else if (prop.HasFixedDestination && Dest != null)
            {
                Label($"{Dest.name} · " + (Dest.minIntimacy > 0 ? $"♥ 친밀도 {Dest.minIntimacy:0} 이상" : "누구나"),
                    new Vector2(0, y), 26, DimText);
                y -= 50f;
                Label(DropLine(Dest.id), new Vector2(0, y), 22, TextColor, new Vector2(860, 50));
                y -= 70f;
            }

            var seatProp = ResolveSeatProp(prop, selectedDest);
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
            else if (seatProp.IsStorageHalted)
                summary = "보관함이 가득 찼어요. 먼저 받아 주세요.";
            else if (selectedAgent != null)
            {
                float gi = selectedAgent.Stats.Stamina;
                float cycle, fillMin;
                if (seatProp.ResourceType == PropResourceType.Merit)
                {
                    double perMin = PropProduction.BaseMeritPerMinute(seatProp.ProductionConfig, seatProp.level);
                    cycle = 15f;
                    fillMin = perMin > 0 ? (float)((seatProp.MeritCapacity - seatProp.PendingMerit.ToDouble()) / perMin) : 0f;
                }
                else
                {
                    int room = Mathf.Max(0, seatProp.ResourceCapacity - seatProp.StoredResources);
                    cycle = seatProp.ProductionConfig.CycleMinutes;
                    fillMin = room * cycle;
                }
                float workMin = gi * GameSettings.Get("staminaDrainMinutes");
                summary = workMin <= fillMin
                    ? $"기력 {Mathf.FloorToInt(gi)}이면 약 {Dur(workMin)} 일하고 쉬어요 ({Mathf.FloorToInt(workMin / Mathf.Max(1f, cycle))}개쯤)"
                    : $"약 {Dur(fillMin)}이면 보관함이 차고, 남은 기력으로 빈 옹달샘·제단에 옮겨 가요";
            }
            else summary = (dd != null ? dd.name + "(으)로 " : "") + "보낼 요괴를 골라 주세요." + (prop.StoredResources > 0 ? $"  보관함에 {prop.StoredResources}개가 있어요." : "");
            Label(summary, new Vector2(0, y), 24, refusing != null || seatProp.IsStorageHalted ? Warn : TextColor, new Vector2(860, 70));
            y -= 100f;

            bool canSend = selectedAgent != null && !seatProp.IsOccupied && !seatProp.IsStorageHalted;
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

        /// <summary>점유된 기물 화면: 누가·어디서 일하는지 → 보관함/기력 게이지 → [불러들이기]/[받기] → 레벨업.</summary>
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

        /// <summary>바닥에 붙는 레벨업 행 (비용·버튼). 더 못 올리면 아무것도 안 그린다.</summary>
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

        /// <summary>[받기] 버튼 라벨 — 공덕이면 금액, 자원이면 개수.</summary>
        string TakeLabel() => prop.ResourceType == PropResourceType.Merit
            ? $"받기 공덕 {prop.PendingMerit.ToDisplayString()}"
            : $"받기 {prop.StoredResources}개";

        /// <summary>목적지 카드에 보이는 "재료 n · 재료 n · 황금 n%" 한 줄 요약.</summary>
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
            var dest = PropCatalog.FindDestination(destId);
            if (dest != null && dest.goldenChance > 0f)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append($"황금 {dest.goldenChance:0.#}%");
            }
            return sb.ToString();
        }

        /// <summary>분을 "n분"/"n시간 m분"으로.</summary>
        static string Dur(float minutes)
        {
            int m = Mathf.Max(0, Mathf.RoundToInt(minutes));
            if (m < 60) return m + "분";
            int h = m / 60, mm = m % 60;
            return mm > 0 ? $"{h}시간 {mm}분" : $"{h}시간";
        }

        /// <summary>받침 유무에 따라 조사(은/는, 이/가 등)를 골라 붙인다.</summary>
        static string Josa(string word, string withBatchim, string without)
        {
            if (string.IsNullOrEmpty(word)) return without;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return without;
            return (c - 0xAC00) % 28 != 0 ? withBatchim : without;
        }

        // ---------------- 동적 칸 (body 안) ----------------
        // 아래는 전부 범용 UI 레이아웃 헬퍼 — Build*()가 매번 새로 그릴 때 쓰는 블록 조립 함수들.
        // pos는 body(또는 넘긴 parent) 중심 기준 anchoredPosition.

        /// <summary>인스펙터에 font가 안 꽂혀 있으면 Unity 기본 폰트로 대체.</summary>
        Font FontOrBuiltin()
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }

        /// <summary>중앙 피벗·anchoredPosition으로 세팅된 빈 RectTransform 하나 생성.</summary>
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

        /// <summary>parent 밑에 텍스트 한 줄(또는 wrap 박스)을 만든다. 클릭을 가리지 않게 raycastTarget은 꺼둠.</summary>
        Text TextIn(Transform parent, string msg, Vector2 pos, int size, Color color, FontStyle style = FontStyle.Normal, Vector2? box = null)
        {
            var t = Rect("Text", parent, pos, box ?? new Vector2(860, 50)).gameObject.AddComponent<Text>();
            t.font = FontOrBuiltin();
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

        /// <summary>body에 바로 붙는 일반 텍스트 한 줄 (TextIn의 단축형).</summary>
        Text Label(string msg, Vector2 pos, int size, Color color, Vector2? boxSize = null) =>
            TextIn(body, msg, pos, size, color, FontStyle.Normal, boxSize ?? new Vector2(880, 50));

        /// <summary>목적지·요괴 선택용 큰 클릭 카드 (배경 Image + Button). 내용(텍스트)은 호출부에서 TextIn으로 따로 채운다.</summary>
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

        /// <summary>라벨 텍스트까지 포함한 버튼. onClick이 null이면 비활성(회색, 못 누름)으로 그려진다.</summary>
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

        /// <summary>보관함·기력 등에 쓰는 가로 진행바. t는 0~1로 클램프.</summary>
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
