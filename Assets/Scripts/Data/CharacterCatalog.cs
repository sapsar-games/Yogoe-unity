using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Yoegoe.Data
{
    /// <summary>
    /// 캐릭터 기획 카탈로그. 소스: Resources/characters.json
    /// 이름·선호공양·엔딩기물·설명·대사는 JSON, 스프라이트는 CharacterData SO.
    /// JSON은 직접 고치지 말고 구글 시트(characters / character_preferences / character_lines 탭)에서
    /// `npm run characters` 로 생성한다 — Tools/export_characters.py.
    /// </summary>
    public static class CharacterCatalog
    {
        [Serializable]
        public class PreferredOffering
        {
            public string id;
            public string name;
        }

        [Serializable]
        public class Entry
        {
            public string id;
            public string displayName;
            public PreferredOffering[] preferredOfferings;
            public string endingPropId;
            public string detailDescription;
            public string[] monologueLines;
            /// <summary>음식 요구 완료 시 대사. 비면 기본 대사.</summary>
            public string[] requestThanksLines;
            /// <summary>요구 완료 후 선물꾸러미를 줄 때 대사. 비면 기본 대사.</summary>
            public string[] requestGiftLines;
            /// <summary>황금 재료 수거 시 대사. {item} = 황금쌀/황금꿀. 비면 기본 대사.</summary>
            public string[] goldenFindLines;
            /// <summary>접속 인사 — 앱을 켜거나 오래 비웠다 돌아왔을 때 놀고 있던 요괴. 비면 기본 대사.</summary>
            public string[] greetingLines;

            // v1.2 시연 대사 세트 (시트 character_lines type) — 비면 대사 없이 넘어간다.
            /// <summary>기물로 보낼 때 (go_hunt · go_gather · go_spring · go_altar).</summary>
            public string[] goHuntLines, goGatherLines, goSpringLines, goAltarLines;
            /// <summary>일하는 중 탭 (work_hunt · work_gather · work_spring · work_altar).</summary>
            public string[] workHuntLines, workGatherLines, workSpringLines, workAltarLines;
            /// <summary>보관함이 차서 다른 기물로 옮겨 갈 때. {d} = 옮겨 갈 곳.</summary>
            public string[] fullLines;
            /// <summary>보관함이 찼는데 갈 곳이 없을 때.</summary>
            public string[] fullIdleLines;
            /// <summary>일하다 기력이 0이 됐을 때.</summary>
            public string[] tiredLines;
            /// <summary>불러들일 때 · 배고플 때 · 음식 · 황금 요리 · 공양물 받았을 때 (아직 쓰는 곳 없음 — 기획 데이터).</summary>
            public string[] homeLines, hungryLines, fedLines, goldLines, offerLines;

            /// <summary>기물 종류별 보내기/일하기 대사. 사냥=Hunt, 채집=Gather, 옹달샘=Water, 제단=Merit.</summary>
            public string[] GoLinesFor(PropResourceType t) => t switch
            {
                PropResourceType.Hunt => goHuntLines,
                PropResourceType.Gather => goGatherLines,
                PropResourceType.Water => goSpringLines,
                PropResourceType.Merit => goAltarLines,
                _ => null
            };

            public string[] WorkLinesFor(PropResourceType t) => t switch
            {
                PropResourceType.Hunt => workHuntLines,
                PropResourceType.Gather => workGatherLines,
                PropResourceType.Water => workSpringLines,
                PropResourceType.Merit => workAltarLines,
                _ => null
            };

            public bool TryParseId(out CharacterId characterId)
                => Enum.TryParse(id, ignoreCase: true, out characterId);
        }

        [Serializable]
        class Root
        {
            public Entry[] characters;
        }

        static Dictionary<CharacterId, Entry> _byId;
        static OfferingData[] _offerings;
        static bool _loggedMissingAsset;

        public static void EnsureLoaded()
        {
            if (_byId != null) return;

            _byId = new Dictionary<CharacterId, Entry>();
            var text = Resources.Load<TextAsset>("characters");
            if (text == null)
            {
                Debug.LogError("[CharacterCatalog] Resources/characters.json 을 찾지 못했습니다.");
                return;
            }

            var root = JsonUtility.FromJson<Root>(text.text);
            if (root?.characters == null)
            {
                Debug.LogError("[CharacterCatalog] characters.json 파싱 실패.");
                return;
            }

            foreach (var entry in root.characters)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                if (!entry.TryParseId(out var cid))
                {
                    Debug.LogWarning($"[CharacterCatalog] 알 수 없는 id: {entry.id}");
                    continue;
                }
                _byId[cid] = entry;
            }
        }

        public static void SetOfferings(OfferingData[] offerings) => _offerings = offerings;

        /// <summary>lines에서 하나 랜덤, 비어 있으면 fallback.</summary>
        public static string PickLine(string[] lines, string fallback)
        {
            if (lines == null || lines.Length == 0) return fallback;
            var line = lines[UnityEngine.Random.Range(0, lines.Length)];
            return string.IsNullOrEmpty(line) ? fallback : line;
        }

        public static bool TryGet(CharacterId id, out Entry entry)
        {
            EnsureLoaded();
            if (_byId != null && _byId.TryGetValue(id, out entry)) return true;
            entry = null;
            return false;
        }

        public static Entry Get(CharacterId id)
        {
            TryGet(id, out var entry);
            return entry;
        }

        /// <summary>
        /// 에셋을 복제해 시트 값(이름·선호·설명·대사·엔딩기물)을 입힌 런타임 사본을 돌려준다 (요괴 스폰 시).
        /// 에셋 파일은 건드리지 않는다.
        /// </summary>
        public static CharacterData RuntimeCopy(CharacterData source)
        {
            if (source == null) return null;
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name;
            ApplyTo(copy);
            return copy;
        }

        /// <summary>시트 값을 덮어쓴다 — 런타임 사본·코드로 만든 스텁에만 (에셋 원본이면 경고).</summary>
        public static void ApplyTo(CharacterData data)
        {
            if (data == null) return;
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.Contains(data))
                Debug.LogWarning($"[CharacterCatalog] 에셋 원본({data.name})에 시트 값을 덮어쓰려 했습니다 — RuntimeCopy를 쓰세요.");
#endif

            EnsureLoaded();
            if (!TryGet(data.id, out var entry) || entry == null) return;

            if (!string.IsNullOrEmpty(entry.displayName))
                data.displayName = entry.displayName;

            if (entry.detailDescription != null)
                data.detailDescription = entry.detailDescription;

            data.monologueLines = entry.monologueLines != null
                ? (string[])entry.monologueLines.Clone()
                : Array.Empty<string>();

            data.preferredOfferings = ResolveOfferings(entry.preferredOfferings);

            if (!string.IsNullOrEmpty(entry.endingPropId))
            {
                var ending = PropLayoutSettings.Get().FindByPropId(entry.endingPropId);
                if (ending != null)
                    data.endingProp = ending;
            }
        }

        public static OfferingData FindOffering(string offeringId)
        {
            if (string.IsNullOrEmpty(offeringId) || _offerings == null) return null;
            for (int i = 0; i < _offerings.Length; i++)
            {
                var o = _offerings[i];
                if (o == null) continue;
                if (string.Equals(o.offeringId, offeringId, StringComparison.OrdinalIgnoreCase))
                    return o;
            }
            return null;
        }

        static OfferingData[] ResolveOfferings(PreferredOffering[] prefs)
        {
            if (prefs == null || prefs.Length == 0) return Array.Empty<OfferingData>();
            if (_offerings == null || _offerings.Length == 0)
            {
                if (!_loggedMissingAsset)
                {
                    Debug.LogWarning("[CharacterCatalog] Offering 테이블이 비어 있습니다.");
                    _loggedMissingAsset = true;
                }
                return Array.Empty<OfferingData>();
            }

            var list = new List<OfferingData>(prefs.Length);
            foreach (var pref in prefs)
            {
                if (pref == null || string.IsNullOrEmpty(pref.id)) continue;
                var found = FindOffering(pref.id);
                if (found != null) list.Add(found);
                else
                    Debug.LogWarning($"[CharacterCatalog] 공양물 에셋 없음: {pref.id}");
            }
            return list.ToArray();
        }
    }
}
