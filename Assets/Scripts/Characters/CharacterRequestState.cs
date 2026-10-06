using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.UI;

namespace Yoegoe.Characters
{
    /// <summary>10장 음식 요구. CharacterAgent가 소유.</summary>
    public class CharacterRequestState
    {
        // 시트 game_settings
        public static float OfferingDurationSeconds => GameSettings.RequestShowSeconds;
        public static float RequestIntervalMin => GameSettings.RequestIntervalMinSeconds;
        public static float RequestIntervalMax => GameSettings.RequestIntervalMaxSeconds;
        /// <summary>기력 구간에 처음 들어왔을 때(소환·로드 포함) 첫 체크까지.</summary>
        public const float EnterBandCheckMin = 0f;
        public const float EnterBandCheckMax = 10f;
        /// <summary>
        /// 기력 ≤ 최대 − 이 값 이면 요구 후보.
        /// v1.3: 10 (음식 1회 회복 +10 기준).
        /// 고라니 소환(1/25)도 바로 후보.
        /// </summary>
        public static float StaminaRequestMargin => GameSettings.FoodRequestMargin; // 시트 game_settings (v1.3: 10)

        public static event System.Action<string> GiftBundleAwarded;

        public OfferingData OfferingRequest { get; private set; }
        public float OfferingExpireAt { get; private set; }
        public float NextRequestCheckAt { get; private set; }

        public bool HasOfferingRequest => OfferingRequest != null;

        SpriteRenderer offeringIcon;
        readonly CharacterAgent owner;
        bool wasInLowStaminaBand;

        const string DefaultThanksLine = "너무 맛있어. 고마워.";
        const string DefaultGiftLine = "이거… 챙겨뒀어.";

        public CharacterRequestState(CharacterAgent agent)
        {
            owner = agent;
            wasInLowStaminaBand = InLowStaminaBand();
            // 소환 직후(기력 1/25) 등은 3~5분을 기다리지 않고 곧 체크
            if (wasInLowStaminaBand) ScheduleSoonCheck();
            else ScheduleNextCheck();
        }

        /// <summary>
        /// 매 프레임 CharacterAgent.Update()에서 호출된다.
        /// dt = 이번 프레임에 흐른 시간(초). 현재 로직에서는 직접 쓰지 않고
        /// Time.time(게임 시작 후 누적 시간)으로 만료·인터벌을 판단한다.
        /// </summary>
        public void Tick(float dt)
        {
            // ── 1. 요구 만료 체크 ──────────────────────────────────────────
            // 음식 요구가 있는데 만료 시각(OfferingExpireAt)이 지났으면 제거한다.
            // OfferingExpireAt = 요구 생성 시각 + 60초(OfferingDurationSeconds)
            if (HasOfferingRequest && Time.time >= OfferingExpireAt)
                ClearOfferingRequest();

            bool inBand = InLowStaminaBand();
            // 풀피 → 저기력으로 들어오는 순간에도 곧 한 번 체크
            if (inBand && !wasInLowStaminaBand && !HasOfferingRequest)
                ScheduleSoonCheck();
            wasInLowStaminaBand = inBand;

            // ── 2. 새 음식 요구 생성 체크 ─────────────────────────────────
            // 아래 조건을 모두 만족할 때만 새 요구를 만든다:
            //   ① 현재 요구가 없을 것 (!HasOfferingRequest)
            //   ② 기절 상태가 아닐 것  (CanSpawnOfferingRequest)
            //   ③ 기력 ≤ 최대 − 8 (음식 +8 기준, InLowStaminaBand)
            //   ④ 다음 체크 시각이 됐을 것 (구간 진입 직후 0~10초 / 이후 3~5분)
            if (!HasOfferingRequest
                && CanSpawnOfferingRequest()
                && inBand
                && Time.time >= NextRequestCheckAt)
            {
                TryStartOfferingRequest(); // 음식 결정 + 머리 위 아이콘 표시
                ScheduleNextCheck();       // 다음 체크 시각을 3~5분 뒤로 예약
            }

            // ── 3. 머리 위 아이콘 위치 갱신 ──────────────────────────────
            // 캐릭터가 이동하므로 매 프레임 아이콘 위치를 캐릭터 머리 위로 맞춘다.
            UpdateVisualPositions();
        }

        void ScheduleNextCheck()
        {
            NextRequestCheckAt = Time.time + Random.Range(RequestIntervalMin, RequestIntervalMax);
        }

        void ScheduleSoonCheck()
        {
            NextRequestCheckAt = Time.time + Random.Range(EnterBandCheckMin, EnterBandCheckMax);
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

        public void TryStartOfferingRequest()
        {
            if (!CanSpawnOfferingRequest() || HasOfferingRequest) return;
            if (!InLowStaminaBand()) return;

            var offering = PickFoodRequest(GameEconomy.Instance);
            if (offering == null) return;

            OfferingRequest = offering;
            OfferingExpireAt = Time.time + OfferingDurationSeconds;
            EnsureOfferingIcon();
            HideMonologueIfAny();
        }

        public void ClearOfferingRequest()
        {
            OfferingRequest = null;
            if (offeringIcon != null) offeringIcon.gameObject.SetActive(false);
        }

        public void ClearAll() => ClearOfferingRequest();

        /// <summary>머리 위 음식 요구 아이콘의 월드 범위 (떠 있을 때만).</summary>
        public bool TryGetIconBounds(out Bounds bounds)
        {
            bounds = default;
            if (!HasOfferingRequest || offeringIcon == null || !offeringIcon.gameObject.activeInHierarchy) return false;
            bounds = offeringIcon.bounds;
            return true;
        }

        /// <summary>상세에서 급여. true면 처리 완료(호출측에서 인벤 차감·리프레시).</summary>
        public bool TryHandleFeed(OfferingData offering, bool isWater, bool isPreferred,
            out int staminaGain, out float intimacyGain, out bool fulfilledRequest)
        {
            staminaGain = offering != null ? offering.ResolveStaminaGain(isPreferred) : 3;
            intimacyGain = offering != null ? offering.ResolveIntimacyGain(isPreferred) : 0f;
            fulfilledRequest = false;

            if (isWater || !HasOfferingRequest)
                return false;

            bool matches = offering != null && OfferingRequest != null
                && string.Equals(offering.BaseId, OfferingRequest.offeringId,
                    System.StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                // 음식 요구 완료: 음식 기력 + 보너스(시트 game_settings) · 친밀도 없음
                staminaGain = GameSettings.FoodStamina + GameSettings.RequestFulfillBonus;
                intimacyGain = 0f;
                fulfilledRequest = true;
                ClearOfferingRequest();
                ScheduleNextCheck();
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

            // 다른 것 주면 기력(일반 효과)만 오르고 요구 삭제
            ClearOfferingRequest();
            ScheduleNextCheck();
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
                var go = new GameObject(owner.name + "_ReqOffering");
                offeringIcon = go.AddComponent<SpriteRenderer>();
                offeringIcon.sortingOrder = 1210;
            }
            offeringIcon.sprite = OfferingRequest != null ? OfferingRequest.icon : null;
            offeringIcon.color = Color.white;
            offeringIcon.transform.localScale = Vector3.one * 0.45f;
            offeringIcon.gameObject.SetActive(offeringIcon.sprite != null);
            if (offeringIcon.sprite == null)
            {
                offeringIcon.sprite = WhiteSprite();
                offeringIcon.color = new Color(1f, 0.85f, 0.4f, 0.95f);
                offeringIcon.transform.localScale = Vector3.one * 0.25f;
                offeringIcon.gameObject.SetActive(true);
            }
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

        static Sprite s_white;
        static Sprite WhiteSprite()
        {
            if (s_white != null) return s_white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            s_white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return s_white;
        }

        public void DestroyVisuals()
        {
            if (offeringIcon != null) Object.Destroy(offeringIcon.gameObject);
            offeringIcon = null;
        }
    }
}
