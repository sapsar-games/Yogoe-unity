using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 나루터 화면 (v1.3) — 맵의 나루터 시설을 누르면 열린다. 그림은 모두 코드로 그린 임시 그림.
    /// - 선착장에 혼령 최대 5명: 머리 위 청하는 요리 이름표(창고에 있으면 금빛) · 남은 시간
    /// - 혼령을 누르면 혼령 창: 주문 대사 · 청하는 요리(음식/공양물 · 아직 지어 본 적 없음) · 창고 n그릇 · 받을 조각
    ///   → [내주기] 또는 [부엌으로]
    /// - 위 줄: 기억 조각 · 줄 n/5 · 다음 혼령까지 · 바로 내줄 수 있는 혼령 수
    /// 맵의 나루터 위에는 기다리는 혼령 수 배지를 띄운다. 규칙은 <see cref="SpiritPier"/>.
    /// </summary>
    public class PierScreen : MonoBehaviour
    {
        public static PierScreen Instance { get; private set; }
        public Font font;

        static readonly string[] KindNames = { "여자", "남자", "노인", "어린이" };
        static readonly Color[] KindTints =
        {
            new Color(0.86f, 0.9f, 1f, 0.85f), new Color(0.8f, 0.88f, 0.95f, 0.85f),
            new Color(0.9f, 0.9f, 0.86f, 0.85f), new Color(0.88f, 0.96f, 0.9f, 0.85f),
        };
        static readonly Color TextColor = new Color(1f, 0.95f, 0.85f);
        static readonly Color DimText = new Color(0.82f, 0.78f, 0.7f);
        static readonly Color Gold = new Color(0.95f, 0.75f, 0.25f, 1f);
        static readonly Color Warn = new Color(1f, 0.55f, 0.45f, 1f);

        GameObject root, sheet;
        RectTransform ghostsLayer, sheetBox;
        Text piecesText, stripText, toastText;
        float toastUntil, refreshTimer;
        int sheetSpiritId = -1;
        static Sprite ghostSprite;

        // 맵 배지
        TextMesh badge;
        PropSlot pierProp;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SpiritPier.Served += OnServed;
            SpiritPier.Left += OnLeft;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SpiritPier.Served -= OnServed;
            SpiritPier.Left -= OnLeft;
        }

        public bool IsOpen => root != null && root.activeSelf;

        public void Open()
        {
            EnsureBuilt();
            root.SetActive(true);
            CloseSheet();
            Rebuild();
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
        }

        void Update()
        {
            UpdateMapBadge();
            if (!IsOpen) return;
            if (toastText != null && toastText.gameObject.activeSelf && Time.unscaledTime > toastUntil)
                toastText.gameObject.SetActive(false);
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = 1f;
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer != null && pointer.press.isPressed) return; // 누르는 중엔 다시 그리지 않음
            Rebuild();
        }

        // ---------------- 규칙 연결 ----------------

        static string DishName(string id)
        {
            var o = OfferingCatalog.Find(id);
            return o != null && !string.IsNullOrEmpty(o.displayName) ? o.displayName : id;
        }

        static bool IsOffering(string id) =>
            CookingCodex.TryGetRecipe(id, out var r) && r.Kind == CookingResultKind.Offering;

        static string Remain(float minutes)
        {
            int m = Mathf.Max(0, Mathf.FloorToInt(minutes));
            return m >= 60 ? $"{m / 60}시간 {m % 60}분" : $"{m}분";
        }

        void Serve(int spiritId)
        {
            var s = SpiritPier.Find(spiritId);
            if (s == null) { CloseSheet(); Rebuild(); return; }
            if (!SpiritPier.TryServe(spiritId, GameEconomy.Instance, out _))
            {
                Toast($"창고에 {DishName(s.DishId)}{Josa(DishName(s.DishId), "이", "가")} 없어요", Warn);
                return;
            }
            GameSaveBridge.RequestSave();
        }

        void OnServed(SpiritPier.Spirit s, int pieces)
        {
            if (!IsOpen) return;
            CloseSheet();
            Toast($"“{SpiritLines.Thanks((int)s.Kind)}”  기억 조각 {pieces}개를 받았어요{(pieces > 1 ? " (공양물)" : "")} · 모두 {SpiritPier.MemoryPieces}개", Gold);
            Rebuild();
        }

        void OnLeft(SpiritPier.Spirit s)
        {
            if (!IsOpen) return;
            if (sheetSpiritId == s.Id) CloseSheet();
            Toast($"{KindNames[(int)s.Kind]} 혼령 “{SpiritLines.Bye((int)s.Kind)}”", DimText);
        }

        void GoKitchen(int spiritId)
        {
            var s = SpiritPier.Find(spiritId);
            Close();
            GongyangganScreen.Resolve()?.Open();
            if (s != null) Debug.Log($"[Pier] {DishName(s.DishId)} 을(를) 지어서 나루터로 돌아오세요");
        }

        // ---------------- 화면 ----------------

        void Rebuild()
        {
            if (ghostsLayer == null) return;
            var eco = GameEconomy.Instance;
            for (int i = ghostsLayer.childCount - 1; i >= 0; i--) Destroy(ghostsLayer.GetChild(i).gameObject);

            int max = GameSettings.SpiritQueueMax;
            float w = 1000f / Mathf.Max(1, max);
            int ready = 0;
            foreach (var s in SpiritPier.Spirits)
            {
                int stock = SpiritPier.StockOf(eco, s.DishId);
                if (stock > 0) ready++;
                float x = -500f + w * (s.Slot + 0.5f);
                MakeGhost(s, new Vector2(x, 40), stock > 0);
            }

            piecesText.text = $"기억 조각 {SpiritPier.MemoryPieces}";
            int n = SpiritPier.Spirits.Count;
            string next = n >= max ? "줄이 찼어요"
                : SpiritPier.BuildOrderPool(eco).Count == 0 ? "지을 수 있는 요리가 없어 혼령이 오지 않아요"
                : $"다음 혼령 {Mathf.Max(1, Mathf.CeilToInt(GameSettings.SpiritIntervalMinutes - SpiritPier.TimerMinutes))}분";
            stripText.text = $"줄 {n}/{max}  ·  {next}" + (ready > 0 ? $"  ·  {ready}명에게 바로 내줄 수 있어요" : "");

            if (sheetSpiritId >= 0)
            {
                if (SpiritPier.Find(sheetSpiritId) == null) CloseSheet();
                else BuildSheet(sheetSpiritId);
            }
        }

        void MakeGhost(SpiritPier.Spirit s, Vector2 pos, bool ready)
        {
            var rt = Rect("Spirit" + s.Id, ghostsLayer, pos, new Vector2(180, 360));
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            int id = s.Id;
            btn.onClick.AddListener(() => { sheetSpiritId = id; BuildSheet(id); });

            var body = Rect("Ghost", rt, new Vector2(0, -40), new Vector2(150, 200)).gameObject.AddComponent<Image>();
            body.sprite = GhostSprite();
            body.color = KindTints[(int)s.Kind];
            body.preserveAspect = true;
            body.raycastTarget = false;
            if (ready)
            {
                var glow = body.gameObject.AddComponent<Outline>();
                glow.effectColor = new Color(1f, 0.85f, 0.4f, 0.9f);
                glow.effectDistance = new Vector2(5, -5);
            }

            var tag = Rect("Tag", rt, new Vector2(0, 110), new Vector2(180, 56));
            tag.gameObject.AddComponent<Image>().color = ready ? new Color(0.55f, 0.4f, 0.12f, 0.95f) : new Color(0.12f, 0.1f, 0.12f, 0.85f);
            TextIn(tag, DishName(s.DishId), Vector2.zero, 26, ready ? Gold : TextColor, FontStyle.Bold, new Vector2(176, 56));
            float left = s.MinutesLeft(Yoegoe.Core.TrustedTime.UtcNow);
            TextIn(rt, Remain(left), new Vector2(0, 155), 22, left < 30f ? Warn : DimText, FontStyle.Normal, new Vector2(180, 32));
            TextIn(rt, KindNames[(int)s.Kind], new Vector2(0, -160), 22, DimText, FontStyle.Normal, new Vector2(180, 32));
        }

        void BuildSheet(int spiritId)
        {
            var s = SpiritPier.Find(spiritId);
            if (s == null) { CloseSheet(); return; }
            sheet.SetActive(true);
            for (int i = sheetBox.childCount - 1; i >= 0; i--) Destroy(sheetBox.GetChild(i).gameObject);

            var eco = GameEconomy.Instance;
            string dish = DishName(s.DishId);
            bool offering = IsOffering(s.DishId);
            int have = SpiritPier.StockOf(eco, s.DishId);
            int pieces = SpiritPier.PiecesFor(s.DishId);
            bool made = CookingCodex.IsDiscovered(s.DishId);
            float left = s.MinutesLeft(Yoegoe.Core.TrustedTime.UtcNow);

            TextIn(sheetBox, $"{KindNames[(int)s.Kind]} 혼령", new Vector2(0, 250), 40, TextColor, FontStyle.Bold, new Vector2(700, 60));
            TextIn(sheetBox, $"남은 시간 {Remain(left)}", new Vector2(0, 200), 24, left < 30f ? Warn : DimText, FontStyle.Normal, new Vector2(700, 40));
            TextIn(sheetBox, $"“{SpiritLines.Ask((int)s.Kind, dish)}”", new Vector2(0, 125), 32, TextColor, FontStyle.Normal, new Vector2(720, 80));
            TextIn(sheetBox, "청하는 요리", new Vector2(-150, 35), 22, DimText, FontStyle.Normal, new Vector2(360, 36));
            TextIn(sheetBox, dish, new Vector2(-150, -5), 36, Gold, FontStyle.Bold, new Vector2(360, 54));
            TextIn(sheetBox, (offering ? "공양물" : "음식") + (made ? "" : " · 아직 지어 본 적 없음"), new Vector2(-150, -48), 22, DimText, FontStyle.Normal, new Vector2(360, 36));
            TextIn(sheetBox, $"창고\n{have}그릇", new Vector2(220, 0), 30, have > 0 ? TextColor : Warn, FontStyle.Bold, new Vector2(220, 100));
            TextIn(sheetBox, $"내주면 기억 조각 {pieces}개" + (have > 0 ? " · 바로 내줄 수 있어요" : " · 부엌에서 지어 오세요"),
                new Vector2(0, -115), 24, TextColor, FontStyle.Normal, new Vector2(720, 40));

            int id = s.Id;
            if (have > 0) MakeButton(sheetBox, $"{dish}{Josa(dish, "을", "를")} 내주기", new Vector2(0, -205), new Vector2(420, 90), new Color(0.3f, 0.5f, 0.45f, 1f), () => Serve(id));
            else MakeButton(sheetBox, "부엌으로", new Vector2(0, -205), new Vector2(420, 90), new Color(0.55f, 0.4f, 0.2f, 1f), () => GoKitchen(id));
            MakeButton(sheetBox, "X", new Vector2(330, 255), new Vector2(70, 70), new Color(0.35f, 0.27f, 0.2f), CloseSheet);
        }

        void CloseSheet()
        {
            sheetSpiritId = -1;
            if (sheet != null) sheet.SetActive(false);
        }

        void Toast(string msg, Color color)
        {
            if (toastText == null) return;
            toastText.text = msg;
            toastText.color = color;
            toastText.gameObject.SetActive(true);
            toastUntil = Time.unscaledTime + 2.8f;
        }

        // ---------------- 맵 배지 ----------------

        void UpdateMapBadge()
        {
            if (pierProp == null)
            {
                if (PropManager.Instance == null) return;
                foreach (var p in PropManager.Instance.All)
                    if (p != null && p.data != null && p.data.opensPier) { pierProp = p; break; }
                if (pierProp == null) return;
            }
            int n = SpiritPier.Spirits.Count;
            if (badge == null)
            {
                var go = new GameObject("PierBadge");
                badge = go.AddComponent<TextMesh>();
                badge.anchor = TextAnchor.LowerCenter;
                badge.alignment = TextAlignment.Center;
                badge.fontSize = 48;
                badge.characterSize = 0.035f;
                badge.color = new Color(1f, 0.95f, 0.8f);
                if (font != null)
                {
                    badge.font = font;
                    go.GetComponent<MeshRenderer>().material = font.material;
                }
                go.GetComponent<MeshRenderer>().sortingOrder = 1150;
            }
            bool show = n > 0;
            if (badge.gameObject.activeSelf != show) badge.gameObject.SetActive(show);
            if (!show) return;
            string t = $"혼령 {n}";
            if (badge.text != t) badge.text = t;
            badge.transform.position = pierProp.TopAnchorWorld(0.03f);
        }

        // ---------------- 만들기 ----------------

        void EnsureBuilt()
        {
            if (root != null) return;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("Canvas_Pier");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 850;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            root = new GameObject("Screen", typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.SetParent(canvasGO.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            root.AddComponent<Image>().color = new Color(0.85f, 0.86f, 0.84f, 1f); // 하늘
            root.AddComponent<Button>().transition = Selectable.Transition.None;     // 뒤의 맵을 막음

            // 바다 · 선착장 · 마당 (임시 그림)
            Band(rt, 0.40f, 0.70f, new Color(0.55f, 0.66f, 0.72f, 1f));   // 바다
            Band(rt, 0.355f, 0.40f, new Color(0.42f, 0.29f, 0.18f, 1f));  // 선착장 판자
            Band(rt, 0f, 0.355f, new Color(0.88f, 0.84f, 0.74f, 1f));     // 마당
            var lantern1 = Rect("Lantern", rt, Vector2.zero, new Vector2(40, 60));
            Anchor(lantern1, 0.06f, 0.44f); lantern1.gameObject.AddComponent<Image>().color = new Color(0.94f, 0.64f, 0.29f, 1f);
            var lantern2 = Rect("Lantern", rt, Vector2.zero, new Vector2(40, 60));
            Anchor(lantern2, 0.94f, 0.44f); lantern2.gameObject.AddComponent<Image>().color = new Color(0.94f, 0.64f, 0.29f, 1f);

            ghostsLayer = Rect("Ghosts", rt, Vector2.zero, new Vector2(1000, 400));
            Anchor(ghostsLayer, 0.5f, 0.52f);

            // 위 줄
            var top = Rect("Top", rt, Vector2.zero, new Vector2(1080, 170));
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
            top.offsetMin = new Vector2(0, -170); top.offsetMax = Vector2.zero;
            top.gameObject.AddComponent<Image>().color = new Color(0.14f, 0.1f, 0.08f, 0.92f);
            TextIn(top, "나루터", new Vector2(-380, -45), 44, TextColor, FontStyle.Bold, new Vector2(280, 70));
            piecesText = TextIn(top, "", new Vector2(80, -45), 32, Gold, FontStyle.Bold, new Vector2(400, 60));
            stripText = TextIn(top, "", new Vector2(0, -120), 26, DimText, FontStyle.Normal, new Vector2(1040, 50));
            MakeButton(top, "닫기", new Vector2(430, -45), new Vector2(160, 80), new Color(0.35f, 0.27f, 0.2f), Close);

            TextIn(rt, "혼령은 요리를 먹으면 기억을 잃고 도깨비불로 돌아가 떠나요", new Vector2(0, -560), 24, new Color(0.45f, 0.4f, 0.34f), FontStyle.Normal, new Vector2(1000, 40));

            toastText = TextIn(rt, "", new Vector2(0, -420), 28, Gold, FontStyle.Bold, new Vector2(1000, 80));
            toastText.gameObject.SetActive(false);

            // 혼령 창
            sheet = new GameObject("Sheet", typeof(RectTransform));
            var srt = (RectTransform)sheet.transform;
            srt.SetParent(rt, false);
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = srt.offsetMax = Vector2.zero;
            sheet.AddComponent<Image>().color = new Color(0, 0, 0, 0.5f);
            var dimBtn = sheet.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(CloseSheet);
            sheetBox = Rect("Box", srt, new Vector2(0, -80), new Vector2(800, 620));
            sheetBox.gameObject.AddComponent<Image>().color = new Color(0.14f, 0.1f, 0.08f, 0.98f);
            sheetBox.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            sheet.SetActive(false);

            root.SetActive(false);
        }

        static void Band(RectTransform parent, float yMin, float yMax, Color c)
        {
            var b = Rect("Band", parent, Vector2.zero, Vector2.zero);
            b.anchorMin = new Vector2(0, yMin); b.anchorMax = new Vector2(1, yMax);
            b.offsetMin = b.offsetMax = Vector2.zero;
            var img = b.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
        }

        static void Anchor(RectTransform r, float x, float y)
        {
            r.anchorMin = r.anchorMax = new Vector2(x, y);
            r.anchoredPosition = Vector2.zero;
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

        Text TextIn(Transform parent, string msg, Vector2 pos, int size, Color color, FontStyle style, Vector2 box)
        {
            var t = Rect("Text", parent, pos, box).gameObject.AddComponent<Text>();
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

        Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Color color, System.Action onClick)
        {
            var rt = Rect("Button", parent, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClick());
            TextIn(rt, label, Vector2.zero, 28, TextColor, FontStyle.Bold, size);
            return b;
        }

        static string Josa(string word, string withBatchim, string without)
        {
            if (string.IsNullOrEmpty(word)) return without;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return without;
            return (c - 0xAC00) % 28 != 0 ? withBatchim : without;
        }

        /// <summary>등불을 든 반투명 혼령 (코드로 그린 임시 그림).</summary>
        static Sprite GhostSprite()
        {
            if (ghostSprite != null) return ghostSprite;
            const int W = 24, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color[W * H];
            var body = new Color(1f, 1f, 1f, 0.75f);
            var eye = new Color(0.2f, 0.22f, 0.3f, 0.9f);
            var lamp = new Color(1f, 0.7f, 0.3f, 1f);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color c = new Color(0, 0, 0, 0);
                float cx = 11.5f;
                // 머리 (y 20..29), 몸 (y 3..20, 아래로 넓게), 꼬리 물결
                if ((x - cx) * (x - cx) + (y - 24) * (y - 24) <= 26) c = body;
                if (y >= 3 && y <= 21 && Mathf.Abs(x - cx) <= 4 + (21 - y) * 0.35f) c = body;
                if (y < 5 && (x % 4 == 0)) c = new Color(0, 0, 0, 0);
                if ((x == 9 || x == 14) && y == 24) c = eye;
                // 등불
                if (x >= 18 && x <= 21 && y >= 9 && y <= 12) c = lamp;
                if (x == 19 && y >= 13 && y <= 15) c = new Color(0.3f, 0.2f, 0.1f, 1f);
                px[y * W + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            ghostSprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 16f);
            return ghostSprite;
        }
    }
}
