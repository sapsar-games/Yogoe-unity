using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Minigames.Yut;

namespace Yoegoe.UI
{
    /// <summary>윷 복귀 만세 연출용 아이템 한 줄(시각만 — 재화는 이미 지급됨).</summary>
    public struct PostYutLootEntry
    {
        public YutSquareRewardKind Kind;
        public OfferingData Offering;
        public int Amount;
        public Sprite Icon;
        public string Label;
        public CookingIngredientId Ingredient;
        public CookingCharmType Charm;

        public PostYutLootEntry(YutSquareRewardKind kind, OfferingData offering, int amount, Sprite icon, string label,
            CookingIngredientId ingredient = default, CookingCharmType charm = CookingCharmType.None)
        {
            Kind = kind;
            Offering = offering;
            Amount = amount;
            Icon = icon;
            Label = label;
            Ingredient = ingredient;
            Charm = charm;
        }
    }

    /// <summary>
    /// 완주 후 윷 화면 Close: 팀 캐릭터가 획득 아이콘을 들고 있고(머물기 일시정지),
    /// 탭하면 HUD 수량창으로 비행 → 재개 → 3초 후 최저 기력 1명 공양물 요구.
    /// (중도 나가기에는 YutScreen이 Begin을 호출하지 않는다.)
    /// </summary>
    public class PostYutLootPresenter : SingletonBehaviour<PostYutLootPresenter>
    {
        public const float OfferingRequestDelaySeconds = 3f;
        const float FlyDuration = 0.45f;
        const float IconWorldScale = 0.38f;
        const float IconHeight = 0.75f;

        public bool IsActive { get; private set; }

        readonly List<CharacterAgent> pausedTeam = new List<CharacterAgent>();
        readonly List<HeldIcon> held = new List<HeldIcon>();
        Coroutine flyRoutine;
        Coroutine offeringRoutine;

        struct HeldIcon
        {
            public CharacterAgent Owner;
            public YutSquareRewardKind Kind;
            public GameObject Go;
            public SpriteRenderer Sr;
        }

        public static PostYutLootPresenter Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PostYutLootPresenter");
            return go.AddComponent<PostYutLootPresenter>();
        }

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            base.Awake();
        }

        void LateUpdate()
        {
            if (!IsActive || flyRoutine != null || held.Count == 0) return;
            // 캐릭터 머리 위에 아이콘 유지(일시정지 중에도 위치만 맞춤)
            var counts = new Dictionary<CharacterAgent, int>();
            for (int i = 0; i < held.Count; i++)
            {
                var h = held[i];
                if (h.Go == null || h.Owner == null) continue;
                counts.TryGetValue(h.Owner, out int idx);
                counts[h.Owner] = idx + 1;
                float xOff = (idx - 0.5f) * 0.28f;
                h.Go.transform.position = IconAnchor(h.Owner) + new Vector3(xOff, 0f, 0f);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ClearVisuals();
            UnpauseAll();
        }

        /// <summary>팀에게 아이콘을 나눠 쥐이고 행동을 멈춘다. loot가 비면 no-op.</summary>
        public void Begin(IReadOnlyList<CharacterAgent> team, IReadOnlyList<PostYutLootEntry> loot)
        {
            ForceComplete(skipOfferingRequest: true);

            if (team == null || team.Count == 0 || loot == null || loot.Count == 0)
                return;

            var agents = new List<CharacterAgent>();
            for (int i = 0; i < team.Count; i++)
            {
                var a = team[i];
                if (a == null) continue;
                agents.Add(a);
            }
            if (agents.Count == 0) return;

            // 스택 단위로 펼쳐서 라운드로빈 분배
            var units = new List<PostYutLootEntry>();
            for (int i = 0; i < loot.Count; i++)
            {
                var e = loot[i];
                int n = Mathf.Max(1, e.Amount);
                for (int k = 0; k < n; k++)
                {
                    units.Add(new PostYutLootEntry(e.Kind, e.Offering, 1, e.Icon, e.Label));
                }
            }
            if (units.Count == 0) return;

            IsActive = true;
            pausedTeam.Clear();
            for (int i = 0; i < agents.Count; i++)
            {
                agents[i].SetBehaviorPaused(true);
                pausedTeam.Add(agents[i]);
            }

            int[] perAgent = new int[agents.Count];
            for (int i = 0; i < units.Count; i++)
            {
                int ai = i % agents.Count;
                var agent = agents[ai];
                var entry = units[i];
                var icon = SpawnIcon(agent, entry, perAgent[ai]++);
                held.Add(icon);
            }
        }

        HeldIcon SpawnIcon(CharacterAgent agent, PostYutLootEntry entry, int stackIndex)
        {
            var go = new GameObject(agent.name + "_PostYutLoot");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = entry.Icon != null ? entry.Icon : WhiteSprite();
            sr.sortingOrder = 1220;
            sr.color = Color.white;
            go.transform.localScale = Vector3.one * IconWorldScale;

            float xOff = (stackIndex - 0.5f) * 0.28f;
            go.transform.position = IconAnchor(agent) + new Vector3(xOff, 0f, 0f);

            return new HeldIcon
            {
                Owner = agent,
                Kind = entry.Kind,
                Go = go,
                Sr = sr,
            };
        }

        static Vector3 IconAnchor(CharacterAgent agent)
        {
            float top = 0.55f;
            var body = agent != null ? agent.GetComponentInChildren<SpriteRenderer>() : null;
            if (body != null && body.sprite != null)
                top = body.bounds.extents.y + 0.2f;
            Vector3 pos = agent != null ? agent.transform.position : Vector3.zero;
            return pos + Vector3.up * (top + IconHeight * 0.5f);
        }

        /// <summary>이 요괴가 머리 위에 든 획득품 아이콘을 눌렀는지.</summary>
        public bool HitHeldIcon(CharacterAgent agent, Vector3 world, float pad)
        {
            for (int i = 0; i < held.Count; i++)
            {
                var h = held[i];
                if (h.Owner != agent || h.Sr == null || !h.Sr.gameObject.activeInHierarchy) continue;
                var b = h.Sr.bounds;
                b.Expand(new Vector3(pad * 2f, pad * 2f, 0f));
                if (world.x >= b.min.x && world.x <= b.max.x && world.y >= b.min.y && world.y <= b.max.y) return true;
            }
            return false;
        }

        /// <summary>캐릭터 탭 — 들고 있으면 전원 수거 연출. true면 탭 소비.</summary>
        public bool TryHandleCharacterTap(CharacterAgent agent)
        {
            if (!IsActive) return false;
            if (agent == null || !pausedTeam.Contains(agent)) return false;
            if (flyRoutine != null) return true;
            flyRoutine = StartCoroutine(FlyAllToHudRoutine());
            return true;
        }

        /// <summary>연출 강제 종료(윷 재오픈 등). skipOfferingRequest면 공양 요구 타이머도 안 켬.</summary>
        public void ForceComplete(bool skipOfferingRequest = false)
        {
            bool hadActive = IsActive || held.Count > 0 || pausedTeam.Count > 0;
            if (flyRoutine != null)
            {
                StopCoroutine(flyRoutine);
                flyRoutine = null;
            }
            if (offeringRoutine != null)
            {
                StopCoroutine(offeringRoutine);
                offeringRoutine = null;
            }

            ClearVisuals();
            var teamCopy = new List<CharacterAgent>(pausedTeam);
            UnpauseAll();
            IsActive = false;

            if (!skipOfferingRequest && hadActive && teamCopy.Count > 0)
                offeringRoutine = StartCoroutine(OfferingRequestAfterDelay(teamCopy));
        }

        IEnumerator FlyAllToHudRoutine()
        {
            var hud = GameHud.Instance;
            var cam = Camera.main;
            float t = 0f;

            var starts = new Vector3[held.Count];
            var ends = new Vector3[held.Count];
            for (int i = 0; i < held.Count; i++)
            {
                starts[i] = held[i].Go != null ? held[i].Go.transform.position : Vector3.zero;
                ends[i] = hud != null
                    ? hud.GetLootFlyTargetWorld(held[i].Kind, cam)
                    : starts[i] + Vector3.up * 2f;
            }

            while (t < FlyDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / FlyDuration);
                float ease = 1f - (1f - u) * (1f - u);
                for (int i = 0; i < held.Count; i++)
                {
                    if (held[i].Go == null) continue;
                    held[i].Go.transform.position = Vector3.Lerp(starts[i], ends[i], ease);
                    if (held[i].Sr != null)
                    {
                        var c = held[i].Sr.color;
                        c.a = 1f - u;
                        held[i].Sr.color = c;
                    }
                }
                yield return null;
            }

            ClearVisuals();
            var teamCopy = new List<CharacterAgent>(pausedTeam);
            UnpauseAll();
            IsActive = false;
            flyRoutine = null;

            offeringRoutine = StartCoroutine(OfferingRequestAfterDelay(teamCopy));
        }

        IEnumerator OfferingRequestAfterDelay(List<CharacterAgent> team)
        {
            yield return new WaitForSecondsRealtime(OfferingRequestDelaySeconds);
            offeringRoutine = null;
            TryStartLowestStaminaOffering(team);
        }

        static void TryStartLowestStaminaOffering(List<CharacterAgent> team)
        {
            CharacterAgent best = null;
            float bestStamina = float.MaxValue;
            for (int i = 0; i < team.Count; i++)
            {
                var a = team[i];
                if (a == null || a.Stats == null || a.Requests == null) continue;
                if (!a.Requests.CanSpawnOfferingRequest()) continue;
                if (a.Requests.HasOfferingRequest) continue;
                float s = a.Stats.Stamina;
                if (s < bestStamina)
                {
                    bestStamina = s;
                    best = a;
                }
            }
            best?.Requests.TryStartOfferingRequest();
        }

        void ClearVisuals()
        {
            for (int i = 0; i < held.Count; i++)
            {
                if (held[i].Go != null) Destroy(held[i].Go);
            }
            held.Clear();
        }

        void UnpauseAll()
        {
            for (int i = 0; i < pausedTeam.Count; i++)
            {
                if (pausedTeam[i] != null)
                    pausedTeam[i].SetBehaviorPaused(false);
            }
            pausedTeam.Clear();
        }

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
    }
}
