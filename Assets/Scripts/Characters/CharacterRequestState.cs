using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.UI;

namespace Yoegoe.Characters
{
    /// <summary>
    /// 음식 요구 — v1.3 그릇 말풍선. CharacterAgent가 소유.
    /// 기력이 (최대 − foodRequestMargin) 이하로 떨어지면 일하는 중에도 머리 위에 그릇이 뜨고,
    /// 그보다 높아지는 순간 사라진다. 특정 음식을 요구하지 않는다 — 그릇(또는 노는 요괴)을 누르면 먹이기 창.
    /// 먹여서 그릇을 없애면 고맙다는 말 + 선물꾸러미 판정.
    /// </summary>
    public class CharacterRequestState
    {
        // 시트 game_settings
        public static float StaminaRequestMargin => GameSettings.FoodRequestMargin; // v1.3: 10

        public static event System.Action<string> GiftBundleAwarded;

        /// <summary>v1.3부터 특정 음식 요구는 없다 — 늘 null (예전 상세 화면 강조 경로 호환용).</summary>
        public OfferingData OfferingRequest => null;

        bool hungry;
        /// <summary>배고픔(그릇 말풍선)이 떠 있는지.</summary>
        public bool HasOfferingRequest => hungry;
        public bool IsHungry => hungry;

        SpriteRenderer offeringIcon;
        readonly CharacterAgent owner;

        const string DefaultThanksLine = "너무 맛있어. 고마워.";
        const string DefaultGiftLine = "이거… 챙겨뒀어.";

        public CharacterRequestState(CharacterAgent agent)
        {
            owner = agent;
        }

        /// <summary>매 프레임 CharacterAgent.Update()에서 호출된다.</summary>
        public void Tick(float dt)
        {
            bool want = CanSpawnOfferingRequest() && InLowStaminaBand();
            if (want && !hungry) StartHungry(sayLine: true);
            else if (!want && hungry) ClearOfferingRequest();
            UpdateVisualPositions();
        }

        bool InLowStaminaBand()
        {
            if (owner == null || owner.Stats == null) return false;
            return owner.Stats.Stamina <= owner.MaxStamina - StaminaRequestMargin + 0.001f;
        }

        public bool CanSpawnOfferingRequest()
        {
            if (owner == null || owner.Stats == null) return false;
            if (owner.Stats.State == ActionState.Fainted) return false;
            return true;
        }

        /// <summary>배고프면 바로 그릇을 띄운다 (윷 복귀 연출 뒤 등).</summary>
        public void TryStartOfferingRequest()
        {
            if (hungry || !CanSpawnOfferingRequest() || !InLowStaminaBand()) return;
            StartHungry(sayLine: false);
        }

        void StartHungry(bool sayLine)
        {
            hungry = true;
            EnsureOfferingIcon();
            HideMonologueIfAny();
            if (sayLine) owner.SayCatalogLine(e => e.hungryLines);
        }

        public void ClearOfferingRequest()
        {
            hungry = false;
            if (offeringIcon != null) offeringIcon.gameObject.SetActive(false);
        }

        public void ClearAll() => ClearOfferingRequest();

        /// <summary>머리 위 그릇의 월드 범위 (떠 있을 때만).</summary>
        public bool TryGetIconBounds(out Bounds bounds)
        {
            bounds = default;
            if (!hungry || offeringIcon == null || !offeringIcon.gameObject.activeInHierarchy) return false;
            bounds = offeringIcon.bounds;
            return true;
        }

        /// <summary>
        /// 음식·공양물을 먹일 때(물 제외). 이번 그릇으로 배고픔이 풀리면 true — 기력 보너스(requestFulfillBonus)를 더하고
        /// 고맙다는 말 + 선물꾸러미 판정. 아직 배고프면 false (기력·친밀도는 평소대로).
        /// </summary>
        public bool TryHandleFeed(OfferingData offering, bool isWater, bool isPreferred,
            out int staminaGain, out float intimacyGain, out bool fulfilledRequest)
        {
            staminaGain = offering != null ? offering.ResolveStaminaGain(isPreferred) : 3;
            intimacyGain = offering != null ? offering.ResolveIntimacyGain(isPreferred) : 0f;
            fulfilledRequest = false;

            if (isWater || !hungry) return false;
            float after = owner.Stats.Stamina + staminaGain;
            if (after <= owner.MaxStamina - StaminaRequestMargin + 0.001f) return false; // 아직 배고프다

            staminaGain += GameSettings.RequestFulfillBonus;
            fulfilledRequest = true;
            ClearOfferingRequest();
            bool gift = GiftBundle.RollAfterRequestFulfilled();
            // 대사는 characters.json(시트 character_lines) — 없으면 기본 대사
            CharacterCatalog.Entry entry = null;
            if (owner.Data != null) CharacterCatalog.TryGet(owner.Data.id, out entry);
            string thanks = CharacterCatalog.PickLine(entry?.requestThanksLines, DefaultThanksLine);
            if (gift)
            {
                string giftLine = CharacterCatalog.PickLine(entry?.requestGiftLines, DefaultGiftLine);
                owner.ShowTempSpeechSequence(
                    new[] { thanks },
                    () =>
                    {
                        owner.ShowTempSpeech(giftLine);
                        GiftBundleAwarded?.Invoke("선물꾸러미");
                    });
            }
            else
            {
                owner.ShowTempSpeech(thanks);
            }
            return true;
        }

        /// <summary>
        /// 요구할 음식 (Docs/00 10-2) — 음식만, 공양물은 요구하지 않는다.
        /// ① 보유 음식 → ② 보유 재료로 만들 수 있는 음식(공양간 재료 기준) → ③ 전체 음식 중 랜덤.
        /// 각 단계 안에서는 균등 랜덤.
        /// </summary>
        public static OfferingData PickFoodRequest(GameEconomy eco)
        {
            var candidates = new System.Collections.Generic.List<OfferingData>();

            // ① 보유 음식
            if (eco != null)
            {
                var snap = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>();
                eco.CaptureOfferingCounts(snap);
                for (int i = 0; i < snap.Count; i++)
                    AddIfFood(candidates, OfferingCatalog.Find(snap[i].Key));
                if (candidates.Count > 0) return candidates[Random.Range(0, candidates.Count)];
            }

            // ② 보유 재료로 만들 수 있는 음식
            if (eco != null)
            {
                foreach (var r in CookingRecipeCatalog.Recipes)
                {
                    if (r.Kind != CookingResultKind.Food) continue;
                    if (!CookingRecipeCatalog.CanMakeWith(r, eco.GetBoardMaterialCount)) continue;
                    AddIfFood(candidates, OfferingCatalog.Find(r.Id));
                }
                if (candidates.Count > 0) return candidates[Random.Range(0, candidates.Count)];
            }

            // ③ 랜덤 음식
            var all = OfferingCatalog.All;
            for (int i = 0; i < all.Count; i++)
                AddIfFood(candidates, all[i]);
            return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : null;
        }

        /// <summary>요구는 일반 음식으로만 — 황금음식은 원래 음식으로 바꿔 담는다(황금음식을 주면 그대로 요구 완료).</summary>
        static void AddIfFood(System.Collections.Generic.List<OfferingData> into, OfferingData o)
        {
            if (o != null && o.golden) o = OfferingCatalog.Find(o.BaseId);
            if (o != null && o.kind == OfferingKind.Food && !into.Contains(o)) into.Add(o);
        }

        void EnsureOfferingIcon()
        {
            if (offeringIcon == null)
            {
                var go = new GameObject(owner.name + "_ReqBowl");
                offeringIcon = go.AddComponent<SpriteRenderer>();
                offeringIcon.sortingOrder = 1210;
            }
            offeringIcon.sprite = BowlSprite();
            offeringIcon.color = Color.white;
            offeringIcon.transform.localScale = Vector3.one * 0.18f; // 20px · 16ppu → 폭 약 0.22
            offeringIcon.gameObject.SetActive(true);
        }

        static Sprite s_bowl;
        /// <summary>그릇 말풍선 — 흰 말풍선 안에 밥그릇 (코드로 그린 임시 그림, 아트가 오면 교체).</summary>
        static Sprite BowlSprite()
        {
            if (s_bowl != null) return s_bowl;
            const int W = 20, H = 18;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var clear = new Color(0, 0, 0, 0);
            var bubble = new Color(1f, 0.98f, 0.92f, 0.95f);
            var edge = new Color(0.35f, 0.25f, 0.18f, 1f);
            var bowl = new Color(0.62f, 0.32f, 0.2f, 1f);
            var rice = Color.white;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color c = clear;
                // 말풍선 몸통 (y 4..17) + 꼬리 (아래 가운데)
                bool body = y >= 4 && x >= 1 && x <= W - 2 && !((x == 1 || x == W - 2) && (y == 4 || y == H - 1));
                bool tail = y < 4 && Mathf.Abs(x - W / 2) <= y - 1;
                if (body || tail) c = bubble;
                if (body && (x == 1 || x == W - 2 || y == 4 || y == H - 1)) c = edge;
                // 그릇 (y 6..10), 밥 (y 11..13)
                int cx = W / 2;
                if (y >= 6 && y <= 10 && Mathf.Abs(x - cx + 0.5f) <= 3.5f + (y - 6) * 0.6f) c = bowl;
                if (y >= 11 && y <= 13 && Mathf.Abs(x - cx + 0.5f) <= 5.5f - (y - 11) * 1.5f) c = rice;
                px[y * W + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            s_bowl = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.2f), 16f);
            return s_bowl;
        }

        void UpdateVisualPositions()
        {
            if (offeringIcon == null || !offeringIcon.gameObject.activeSelf) return;

            float top = 0.55f;
            var body = owner.GetComponentInChildren<SpriteRenderer>();
            if (body != null && body.sprite != null)
                top = body.bounds.extents.y + 0.35f;

            offeringIcon.transform.position = owner.transform.position + Vector3.up * (top + 0.35f);
        }

        void HideMonologueIfAny() => owner.HideMonologueForRequest();


        public void DestroyVisuals()
        {
            if (offeringIcon != null)
            {
                if (Application.isPlaying) Object.Destroy(offeringIcon.gameObject);
                else Object.DestroyImmediate(offeringIcon.gameObject); // 에디터 테스트
            }
            offeringIcon = null;
        }
    }
}
