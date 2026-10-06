using System.Collections;
using UnityEngine;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.UI;

namespace Yoegoe.Characters
{
    // 혼잣말 말풍선(6-4장), 임시 대사 시퀀스.
    // 단일 탭: 표시·순환·10초 연장. 더블탭: 상세화면 (말풍선은 더블탭 판정 후에만).
    public partial class CharacterAgent
    {
        [Header("혼잣말 (6-4장)")]
        [Tooltip("혼잣말 말풍선에 쓸 한글 폰트. 비워두면 유니티 기본 폰트로 나와서 한글이 깨질 수 있음.")]
        public Font bubbleFont;

        private TextMesh bubbleTextMesh;
        private SpriteRenderer bubbleBg;
        private float monologueTimer;
        private bool monologueShowing;
        private int monologueIndex;
        private const float MonologueDisplaySeconds = 10f;
        private const float MonologueMinInterval = 30f;
        private const float MonologueMaxInterval = 60f;

        private static Sprite sharedBubbleSprite;
        Coroutine tempSpeechRoutine;
        private bool showingFaintedEllipsis;
        private const string FaintedBubbleText = "...";

        private bool CanShowMonologue =>
            !SpeechGate.YokaiSilenced
            && (Stats.State == ActionState.Walking
            || Stats.State == ActionState.Playing
            || Stats.State == ActionState.Staying);

        private bool CanTapMonologue => CanShowMonologue;

        private void UpdateMonologue(float dt)
        {
            if (Stats.State == ActionState.Fainted)
            {
                if (!showingFaintedEllipsis) ShowFaintedEllipsis();
                else FollowBubblePosition();
                return;
            }

            if (showingFaintedEllipsis)
            {
                showingFaintedEllipsis = false;
                HideMonologue();
            }

            if (HasOfferingRequest) return;
            if (Data == null || Data.monologueLines == null || Data.monologueLines.Length == 0) return;

            FollowBubblePosition();

            if (monologueShowing)
            {
                monologueTimer -= dt;
                if (monologueTimer <= 0f) HideMonologue();
                return;
            }

            // 자동 팝업은 걷기/놀기/머물기에서만 (기절은 "..." 말풍선)
            if (!CanShowMonologue) return;

            monologueTimer -= dt;
            if (monologueTimer <= 0f) ShowMonologue();
        }

        void FollowBubblePosition()
        {
            if (!monologueShowing || bubbleTextMesh == null) return;
            float spriteTop = spriteRenderer != null ? spriteRenderer.bounds.extents.y : 0.3f;
            // 말풍선 아랫변을 머리 위에 고정 — 여러 줄이 돼도 아래로 늘어나 머리를 가리지 않게
            float halfH = bubbleBg != null ? bubbleBg.transform.localScale.y * 0.5f : 0.25f;
            Vector3 bubblePos = transform.position + Vector3.up * (spriteTop + 0.3f + halfH);
            bubbleTextMesh.transform.position = bubblePos;
            if (bubbleBg != null) bubbleBg.transform.position = bubblePos;
        }

        /// <summary>
        /// 단일 탭 확정 시(더블탭이 아님) MapPointerRouter가 호출.
        /// 기절: "..." 갱신. 그 외: 혼잣말.
        /// </summary>
        public void OnTapped()
        {
            if (Stats.State == ActionState.Fainted)
            {
                ShowFaintedEllipsis();
                return;
            }

            if (!CanTapMonologue) return;
            // 인사·요구 감사 같은 임시 대사 중엔 끝까지 보여 준다 (뒤에 선물꾸러미 등이 이어질 수 있음)
            if (tempSpeechRoutine != null) return;
            // 일하는 중 탭 → 그 기물의 일 대사 (v1.2 시연). 없으면 혼잣말.
            if (Stats.State == ActionState.Staying && currentProp != null
                && TrySayCatalogLine(e => e.WorkLinesFor(currentProp.ResourceType)))
                return;
            if (Data == null || Data.monologueLines == null || Data.monologueLines.Length == 0) return;
            // 떠 있으면 다음 대사로 바뀌고 10초 다시 (6-4)
            ShowMonologue();
        }

        /// <summary>기절 중 머리 위 말풍선. 탭하면 같은 문구로 갱신.</summary>
        void ShowFaintedEllipsis()
        {
            showingFaintedEllipsis = true;
            EnsureBubble();
            bubbleTextMesh.text = FaintedBubbleText;
            ApplyBubbleScale();
            bubbleTextMesh.gameObject.SetActive(true);
            bubbleBg.gameObject.SetActive(true);

            var renderer = bubbleTextMesh.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 1001;
                Bounds bounds = renderer.bounds;
                bubbleBg.transform.localScale = new Vector3(bounds.size.x + BubblePadX * UiTextScale.Bubble, bounds.size.y + BubblePadY * UiTextScale.Bubble, 1f);
            }

            monologueShowing = true;
            monologueTimer = float.PositiveInfinity;
            FollowBubblePosition();
        }

        private void ShowMonologue()
        {
            EnsureBubble();
            bubbleTextMesh.text = WrapBubbleText(PickMonologueLine());
            ApplyBubbleScale();
            bubbleTextMesh.gameObject.SetActive(true);
            bubbleBg.gameObject.SetActive(true);

            // 배경 판을 텍스트 실제 크기에 맞춰 다시 그림 (말풍선처럼 보이게).
            // 배경과 텍스트는 서로 형제 오브젝트라, 배경 스케일을 바꿔도 텍스트 크기엔 영향 없음.
            var renderer = bubbleTextMesh.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 1001; // 상태 점(1000)보다 위
            Bounds bounds = renderer.bounds;
            bubbleBg.transform.localScale = new Vector3(bounds.size.x + BubblePadX * UiTextScale.Bubble, bounds.size.y + BubblePadY * UiTextScale.Bubble, 1f);

            monologueShowing = true;
            monologueTimer = MonologueDisplaySeconds;
        }

        /// <summary>혼잣말1 → 2 → 3 … 순서로 순환.</summary>
        private string PickMonologueLine()
        {
            var lines = Data.monologueLines;
            if (lines.Length == 0) return string.Empty;
            if (monologueIndex < 0 || monologueIndex >= lines.Length) monologueIndex = 0;
            string line = lines[monologueIndex];
            monologueIndex = (monologueIndex + 1) % lines.Length;
            return line;
        }

        private void HideMonologue()
        {
            if (bubbleTextMesh != null) bubbleTextMesh.gameObject.SetActive(false);
            if (bubbleBg != null) bubbleBg.gameObject.SetActive(false);
            monologueShowing = false;
            showingFaintedEllipsis = false;
            monologueTimer = Random.Range(MonologueMinInterval, MonologueMaxInterval);
        }

        public void HideMonologueForRequest() => HideMonologue();

        /// <summary>
        /// 머리 위 표시(혼잣말 말풍선·음식 요구 아이콘·윷 획득품)를 눌렀는지 — 7장 표 "말풍선/아이템 탭 = 몸 탭과 동일".
        /// MapPointerRouter가 몸과 같은 요괴 탭으로 처리한다.
        /// </summary>
        public bool HitOverhead(Vector3 world, float pad)
        {
            if (bubbleBg != null && bubbleBg.gameObject.activeInHierarchy
                && Contains2D(bubbleBg.bounds, world, pad))
                return true;
            if (Requests.TryGetIconBounds(out var icon) && Contains2D(icon, world, pad))
                return true;
            var loot = PostYutLootPresenter.Instance;
            return loot != null && loot.HitHeldIcon(this, world, pad);
        }

        static bool Contains2D(Bounds b, Vector3 world, float pad)
        {
            b.Expand(new Vector3(pad * 2f, pad * 2f, 0f));
            return world.x >= b.min.x && world.x <= b.max.x && world.y >= b.min.y && world.y <= b.max.y;
        }

        /// <summary>시트 character_lines 에서 고른 한 줄을 말한다. {d} 는 d 로 바꾼다. 대사가 없으면 false.</summary>
        bool TrySayCatalogLine(System.Func<CharacterCatalog.Entry, string[]> select, string d = null)
        {
            if (Data == null || select == null) return false;
            if (!CharacterCatalog.TryGet(Data.id, out var entry) || entry == null) return false;
            string line = CharacterCatalog.PickLine(select(entry), null);
            if (string.IsNullOrEmpty(line)) return false;
            if (d != null) line = line.Replace("{d}", d);
            ShowTempSpeech(line);
            return true;
        }

        /// <summary>delay초 뒤 한 줄 말한다 (접속 인사 등 여러 요괴가 순서대로 말할 때).</summary>
        public void SayAfter(float delay, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (delay <= 0f) { ShowTempSpeech(line); return; }
            StartCoroutine(SayAfterRoutine(delay, line));
        }

        IEnumerator SayAfterRoutine(float delay, string line)
        {
            yield return new WaitForSecondsRealtime(delay);
            ShowTempSpeech(line);
        }

        public void ShowTempSpeech(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            ShowTempSpeechSequence(new[] { line }, null);
        }

        /// <summary>대사를 순서대로 표시한 뒤 onComplete 호출. 요구→꾸러미 연출용.</summary>
        public void ShowTempSpeechSequence(string[] lines, System.Action onComplete)
        {
            // 출석 윷점 옥토끼 대사 중엔 요괴 말풍선 없이 결과만 진행
            if (lines == null || lines.Length == 0 || SpeechGate.YokaiSilenced)
            {
                onComplete?.Invoke();
                return;
            }
            if (tempSpeechRoutine != null) StopCoroutine(tempSpeechRoutine);
            tempSpeechRoutine = StartCoroutine(TempSpeechSequenceRoutine(lines, onComplete));
        }

        IEnumerator TempSpeechSequenceRoutine(string[] lines, System.Action onComplete)
        {
            const float secondsPerLine = 2.8f;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                yield return TempSpeechRoutine(line, secondsPerLine, hideAtEnd: true);
            }
            tempSpeechRoutine = null;
            onComplete?.Invoke();
        }

        IEnumerator TempSpeechRoutine(string line, float duration, bool hideAtEnd)
        {
            EnsureBubble();
            bubbleTextMesh.text = WrapBubbleText(line);
            ApplyBubbleScale();
            bubbleTextMesh.gameObject.SetActive(true);
            bubbleBg.gameObject.SetActive(true);
            var renderer = bubbleTextMesh.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 1001;
                Bounds bounds = renderer.bounds;
                bubbleBg.transform.localScale = new Vector3(bounds.size.x + BubblePadX * UiTextScale.Bubble, bounds.size.y + BubblePadY * UiTextScale.Bubble, 1f);
            }
            monologueShowing = true;
            monologueTimer = duration;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (bubbleTextMesh != null)
                {
                    float spriteTop = spriteRenderer != null ? spriteRenderer.bounds.extents.y : 0.3f;
                    // 말풍선 아랫변을 머리 위에 고정 — 여러 줄이 돼도 아래로 늘어나 머리를 가리지 않게
            float halfH = bubbleBg != null ? bubbleBg.transform.localScale.y * 0.5f : 0.25f;
            Vector3 bubblePos = transform.position + Vector3.up * (spriteTop + 0.3f + halfH);
                    bubbleTextMesh.transform.position = bubblePos;
                    if (bubbleBg != null) bubbleBg.transform.position = bubblePos;
                }
                yield return null;
            }
            if (hideAtEnd) HideMonologue();
        }

        /// <summary>말풍선 배경용 1색 스프라이트 (디버그 점 제거 후에도 말풍선이 씀).</summary>
        private static Sprite GetSharedDotSprite()
        {
            if (sharedBubbleSprite != null) return sharedBubbleSprite;
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            sharedBubbleSprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
            return sharedBubbleSprite;
        }

        // 말풍선 크기 — 글자 크기 · 여백 · 한 줄 최대 글자 수 (긴 대사가 한 줄로 늘어나 말풍선이 커지지 않게)
        // 배율·한 줄 글자 수 = 시트 game_settings(bubbleTextScale · bubbleMaxChars) × 설정 화면 글자 크기 (UiTextScale)
        const float BubbleCharacterSize = 0.038f;
        const float BubblePadX = 0.2f;
        const float BubblePadY = 0.12f;
        static int BubbleMaxCharsPerLine => UiTextScale.BubbleMaxChars;
        const int BubbleMaxLines = 3;

        /// <summary>말풍선 글자 배율 적용 — 배경은 이 크기에 맞춰 그리므로 UiTextScaler 가 아니라 여기서 직접.</summary>
        void ApplyBubbleScale()
        {
            if (bubbleTextMesh != null)
                bubbleTextMesh.characterSize = BubbleCharacterSize * UiTextScale.Bubble;
        }

        /// <summary>한 줄이 BubbleMaxCharsPerLine 을 넘으면 띄어쓰기 자리에서 나눈다 (띄어쓰기가 없으면 글자 수로).</summary>
        public static string WrapBubbleText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= BubbleMaxCharsPerLine || text.Contains("\n")) return text;
            int lines = Mathf.Min(BubbleMaxLines, Mathf.CeilToInt(text.Length / (float)BubbleMaxCharsPerLine));
            int target = Mathf.CeilToInt(text.Length / (float)lines);
            var sb = new System.Text.StringBuilder(text.Length + lines);
            int start = 0;
            for (int l = 1; l < lines && start < text.Length; l++)
            {
                int ideal = start + target;
                if (ideal >= text.Length) break;
                // ideal 에서 가장 가까운 띄어쓰기
                int cut = -1;
                for (int d = 0; d <= target / 2 && cut < 0; d++)
                {
                    if (ideal - d > start && text[ideal - d] == ' ') cut = ideal - d;
                    else if (ideal + d < text.Length && text[ideal + d] == ' ') cut = ideal + d;
                }
                if (cut < 0)
                {
                    sb.Append(text, start, ideal - start).Append('\n');
                    start = ideal;
                }
                else
                {
                    sb.Append(text, start, cut - start).Append('\n');
                    start = cut + 1;
                }
            }
            sb.Append(text, start, text.Length - start);
            return sb.ToString();
        }

        private void EnsureBubble()
        {
            if (bubbleTextMesh != null) return;

            var bgGo = new GameObject(gameObject.name + "_BubbleBg");
            bubbleBg = bgGo.AddComponent<SpriteRenderer>();
            bubbleBg.sprite = GetSharedDotSprite();
            bubbleBg.color = new Color(1f, 1f, 0.96f, 0.92f);
            bubbleBg.sortingOrder = 1000;
            bgGo.SetActive(false);

            var textGo = new GameObject(gameObject.name + "_Bubble");
            bubbleTextMesh = textGo.AddComponent<TextMesh>();
            bubbleTextMesh.characterSize = BubbleCharacterSize;
            bubbleTextMesh.fontSize = UiFonts.Size(48);
            bubbleTextMesh.anchor = TextAnchor.MiddleCenter;
            bubbleTextMesh.alignment = TextAlignment.Center;
            bubbleTextMesh.color = new Color(0.15f, 0.1f, 0.08f);
            if (bubbleFont != null)
            {
                bubbleTextMesh.font = bubbleFont;
                textGo.GetComponent<MeshRenderer>().material = bubbleFont.material;
            }
            textGo.GetComponent<MeshRenderer>().sortingOrder = 1001;
            textGo.SetActive(false);
        }
    }
}
