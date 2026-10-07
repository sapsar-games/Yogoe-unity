using System;
using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Cooking;

namespace Yoegoe.Data
{
    /// <summary>
    /// 기물 밸런스 카탈로그. 소스: Resources/props.json
    /// JSON은 직접 고치지 말고 구글 시트(props / prop_drop_tables 탭)에서
    /// `npm run props` 로 생성한다 — Tools/export_props.py.
    /// </summary>
    public static class PropCatalog
    {
        [Serializable]
        public class Entry
        {
            public string propId;
            public string displayName;
            public string resourceType;
            public float cycleMinutes;
            public int baseCapacity;
            public double meritPerMinute;
            public double levelGrowth;
            public float meritCapacityMinutes;
            /// <summary>공덕 보관량 레벨당 배수 (v1.3 제단 1.1). 0이면 1.</summary>
            public double capacityGrowth;
            public bool intimacyBonus;
            public double ownerMultiplier;
            public bool upgradable;
            public double upgradeBaseCost;
            public double upgradeCostMultiplier;

            public PropResourceType ResourceType =>
                Enum.TryParse(resourceType, true, out PropResourceType t) ? t : PropResourceType.None;
        }

        [Serializable]
        public class Drop
        {
            public string table;       // hunt / gather
            public string ingredient;  // CookingIngredientId 이름 (Rice …) 또는 SpecialItemId (GoldenRice …)
            public float weight;
        }

        /// <summary>v1.3 사냥터·채집터 목적지 (시트 destinations 탭). 재료 확률은 dropTables 의 table = id.</summary>
        [Serializable]
        public class Destination
        {
            public string id;
            public string prop;          // Hunt / Gather
            public string name;
            public string rarity;        // 하 · 중 · 상
            public float minIntimacy;    // 입장 친밀도
            public float goldenChance;   // 황금 확률 % (황금 재료 개편 전까지 안 씀)

            public PropResourceType PropType =>
                Enum.TryParse(prop, true, out PropResourceType t) ? t : PropResourceType.None;
        }

        [Serializable]
        public class Settings
        {
            /// <summary>기물 구매 비용 = purchaseBaseCost × purchaseCostGrowth^(n−1), n = 구매 순서.</summary>
            public double purchaseBaseCost = 300;
            public double purchaseCostGrowth = 1.35;
        }

        [Serializable]
        class Root
        {
            public Entry[] props;
            public Drop[] dropTables;
            public Settings settings;
            public Destination[] destinations;
        }

        static Settings settings;

        /// <summary>시트 prop_settings 탭 (전역 값).</summary>
        public static Settings Global
        {
            get { EnsureLoaded(); return settings ?? (settings = new Settings()); }
        }

        static Dictionary<string, Entry> byId;
        /// <summary>드롭 코드: 요리 재료 = (int)CookingIngredientId, 특수 수집품 = SpecialCodeBase + (int)SpecialItemId.</summary>
        public const int SpecialCodeBase = 1000;

        public static bool IsSpecialCode(int code) => code >= SpecialCodeBase;
        public static SpecialItemId SpecialOf(int code) => (SpecialItemId)(code - SpecialCodeBase);

        static Dictionary<PropResourceType, List<(int code, float weight)>> drops;
        static Dictionary<string, List<(int code, float weight)>> destDrops;
        static List<Destination> destinations;

        /// <summary>그 기물(Hunt/Gather)의 목적지 — 시트 순서 그대로.</summary>
        public static List<Destination> DestinationsFor(PropResourceType type)
        {
            EnsureLoaded();
            var list = new List<Destination>();
            if (destinations != null)
                foreach (var d in destinations) if (d.PropType == type) list.Add(d);
            return list;
        }

        public static Destination FindDestination(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id) || destinations == null) return null;
            foreach (var d in destinations) if (string.Equals(d.id, id, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        /// <summary>목적지 재료 (드롭 코드, 가중치) — 창에 아이콘·이름으로 보여 줄 때.</summary>
        public static IReadOnlyList<(int code, float weight)> DestinationDrops(string id)
        {
            EnsureLoaded();
            return destDrops != null && id != null && destDrops.TryGetValue(id, out var l) ? l : (IReadOnlyList<(int, float)>)Array.Empty<(int, float)>();
        }

        /// <summary>목적지 표에서 1개 뽑기. 목적지가 없거나 표가 비면 예전 Hunt/Gather 표.</summary>
        public static int RollDrop(PropResourceType table, string destinationId, float random01)
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(destinationId) && destDrops != null
                && destDrops.TryGetValue(destinationId, out var list) && list.Count > 0)
                return Roll(list, random01, true);
            return RollDrop(table, random01);
        }

        static int Roll(List<(int code, float weight)> list, float random01, bool includeSpecial)
        {
            float total = 0f; int last = -1;
            foreach (var (code, w) in list) { if (!includeSpecial && IsSpecialCode(code)) continue; total += w; last = code; }
            if (last < 0) return (int)CookingIngredientId.Rice;
            float r = Mathf.Min(Mathf.Clamp01(random01) * total, total - 0.0001f);
            foreach (var (code, w) in list)
            {
                if (!includeSpecial && IsSpecialCode(code)) continue;
                if (r < w) return code;
                r -= w;
            }
            return last;
        }

        public static void EnsureLoaded()
        {
            if (byId != null) return;
            byId = new Dictionary<string, Entry>();
            drops = new Dictionary<PropResourceType, List<(int, float)>>();
            destDrops = new Dictionary<string, List<(int, float)>>(StringComparer.OrdinalIgnoreCase);
            destinations = new List<Destination>();

            var text = Resources.Load<TextAsset>("props");
            if (text == null)
            {
                Debug.LogError("[PropCatalog] Resources/props.json 을 찾지 못했습니다.");
                return;
            }
            var root = JsonUtility.FromJson<Root>(text.text);
            settings = root?.settings;
            if (root?.destinations != null)
                foreach (var d in root.destinations)
                    if (d != null && !string.IsNullOrEmpty(d.id)) destinations.Add(d);
            if (root?.props != null)
                foreach (var e in root.props)
                    if (e != null && !string.IsNullOrEmpty(e.propId))
                        byId[e.propId] = e;

            if (root?.dropTables != null)
            {
                foreach (var d in root.dropTables)
                {
                    if (d == null || d.weight <= 0f) continue;
                    bool isDest = FindDestInList(d.table) != null;
                    PropResourceType table = PropResourceType.None;
                    if (!isDest && !Enum.TryParse(d.table, true, out table)) continue;
                    int code;
                    if (Enum.TryParse(d.ingredient, true, out SpecialItemId special))
                        code = SpecialCodeBase + (int)special;
                    else if (Enum.TryParse(d.ingredient, true, out CookingIngredientId ing))
                        code = (int)ing;
                    else continue;
                    if (isDest)
                    {
                        if (!destDrops.TryGetValue(d.table, out var dl)) destDrops[d.table] = dl = new List<(int, float)>();
                        dl.Add((code, d.weight));
                        continue;
                    }
                    if (!drops.TryGetValue(table, out var list))
                        drops[table] = list = new List<(int, float)>();
                    list.Add((code, d.weight));
                }
            }
        }

        static Destination FindDestInList(string id)
        {
            if (destinations == null || string.IsNullOrEmpty(id)) return null;
            foreach (var d in destinations) if (string.Equals(d.id, id, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        public static bool TryGet(string propId, out Entry entry)
        {
            EnsureLoaded();
            entry = null;
            return !string.IsNullOrEmpty(propId) && byId != null && byId.TryGetValue(propId, out entry);
        }

        /// <summary>
        /// 에셋을 복제해 시트 값을 입힌 런타임 사본을 돌려준다 (기물 스폰 시). 에셋 파일은 건드리지 않는다 —
        /// 원본에 직접 쓰면 에디터 플레이 중 값이 .asset에 저장돼 git 변경으로 섞인다.
        /// </summary>
        public static PropData RuntimeCopy(PropData source)
        {
            if (source == null) return null;
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name;
            ApplyTo(copy);
            return copy;
        }

        /// <summary>시트 값을 덮어쓴다 — 런타임 사본에만 (에셋 원본이면 경고).</summary>
        static void ApplyTo(PropData data)
        {
            if (data == null || !TryGet(data.propId, out var e)) return;
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.Contains(data))
                Debug.LogWarning($"[PropCatalog] 에셋 원본({data.name})에 시트 값을 덮어쓰려 했습니다 — RuntimeCopy를 쓰세요.");
#endif

            if (!string.IsNullOrEmpty(e.displayName)) data.displayName = e.displayName;
            data.resourceType = e.ResourceType;
            data.baseProductionPerMinute = e.meritPerMinute;
            data.levelGrowth = e.levelGrowth > 0 ? e.levelGrowth : 1.0;
            data.meritCapacityMinutes = e.meritCapacityMinutes;
            data.capacityGrowth = e.capacityGrowth > 0 ? e.capacityGrowth : 1.0;
            data.intimacyBonus = e.intimacyBonus;
            data.ownerMultiplier = e.ownerMultiplier > 0 ? e.ownerMultiplier : 1.0;
            data.cycleMinutes = e.cycleMinutes;
            data.baseCapacity = e.baseCapacity;
            data.upgradable = e.upgradable;
            if (e.upgradeBaseCost > 0) data.upgradeBaseCost = e.upgradeBaseCost;
            if (e.upgradeCostMultiplier > 0) data.upgradeCostMultiplier = e.upgradeCostMultiplier;
        }

        /// <summary>사냥(Hunt)·채집(Gather) 1개 뽑기 (시트 prop_drop_tables 가중치). 반환 = 드롭 코드.
        /// includeSpecial=false면 황금쌀·황금꿀을 빼고 나머지 가중치로 뽑는다(요리재료만).</summary>
        public static int RollDrop(PropResourceType table, float random01, bool includeSpecial = true)
        {
            EnsureLoaded();
            if (drops == null || !drops.TryGetValue(table, out var list) || list.Count == 0)
                return (int)CookingIngredientId.Rice;
            float total = 0f;
            int last = -1;
            foreach (var (code, w) in list)
            {
                if (!includeSpecial && IsSpecialCode(code)) continue;
                total += w;
                last = code;
            }
            if (last < 0) return (int)CookingIngredientId.Rice;
            float r = Mathf.Min(Mathf.Clamp01(random01) * total, total - 0.0001f);
            foreach (var (code, w) in list)
            {
                if (!includeSpecial && IsSpecialCode(code)) continue;
                if (r < w) return code;
                r -= w;
            }
            return last;
        }
    }
}
