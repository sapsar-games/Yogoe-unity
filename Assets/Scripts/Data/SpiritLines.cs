using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yoegoe.Data
{
    /// <summary>
    /// 나루터 혼령 대사 — 대사 시트 spirit_lines 탭(Resources/spirit_lines.json, npm run spirits).
    /// kind: woman · man · elder · child (SpiritKind 순서), type: ask({dish}) · thanks · bye.
    /// </summary>
    public static class SpiritLines
    {
        [Serializable] class Row { public string kind; public string type; public string text; }
        [Serializable] class File { public Row[] lines; }

        public const string ResourcePath = "spirit_lines";
        static readonly string[] Kinds = { "woman", "man", "elder", "child" };
        static Dictionary<string, List<string>> byKey;

        /// <summary>kindIndex = SpiritKind 정수. dish 는 ask 의 {dish} 자리.</summary>
        public static string Ask(int kindIndex, string dish) => Pick(kindIndex, "ask", "‘{dish}’ 주세요.").Replace("{dish}", dish ?? "");
        public static string Thanks(int kindIndex) => Pick(kindIndex, "thanks", "고마워요.");
        public static string Bye(int kindIndex) => Pick(kindIndex, "bye", "…배가 떠나네요.");

        static string Pick(int kindIndex, string type, string fallback)
        {
            Ensure();
            string kind = kindIndex >= 0 && kindIndex < Kinds.Length ? Kinds[kindIndex] : Kinds[0];
            if (byKey.TryGetValue(kind + "|" + type, out var list) && list.Count > 0)
                return list[UnityEngine.Random.Range(0, list.Count)];
            return fallback;
        }

        static void Ensure()
        {
            if (byKey != null) return;
            LoadFromJson(Resources.Load<TextAsset>(ResourcePath)?.text);
        }

        /// <summary>spirit_lines.json 으로 다시 채운다 (테스트·핫리로드).</summary>
        public static void LoadFromJson(string json)
        {
            byKey = new Dictionary<string, List<string>>();
            var file = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<File>(json);
            if (file?.lines == null) return;
            foreach (var r in file.lines)
            {
                if (r == null || string.IsNullOrEmpty(r.text)) continue;
                string key = r.kind + "|" + r.type;
                if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = new List<string>();
                list.Add(r.text);
            }
        }
    }
}
