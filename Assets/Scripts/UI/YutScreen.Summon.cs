using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 윷 화면 안에서 벌어지는 소환 연출 전담 — YutScreen.cs에서 분리(리팩토링, 동작 변화 없음).
    /// 맵의 <see cref="SummonCeremony"/>는 월드 스페이스 연출이라 윷 화면의 불투명 패널에 가려져
    /// 안 보이기 때문에, 여기서 화면(스크린) 스페이스로 같은 느낌만 재현한다.
    /// </summary>
    public partial class YutScreen
    {
        /// <summary>로스터 [소환하기] — 맵과 같은 확인 창(SummonPopup)·같은 절차(CharacterSummon)를 쓴다.
        /// 확인하면 SummonPopup이 윷 화면이 열려 있는 걸 보고 <see cref="PlaySummon"/>으로 연출을 넘긴다.</summary>
        void OnSummonSlotTapped()
        {
            if (!CharacterSummon.NextSummonTarget(out var target)) return;
            if (SummonPopup.Instance != null) SummonPopup.Instance.Open(target);
        }

        bool summonPresenting;

        /// <summary>윷 화면용 소환 연출 (맵 SummonCeremony는 월드라 윷 패널에 가려진다). 시작하면 true.</summary>
        public bool PlaySummon(CharacterId target)
        {
            if (!CharacterSummon.CanSummon(target) || !isActiveAndEnabled) return false;
            StartCoroutine(SummonCeremonyRoutine(target));
            return true;
        }

        /// <summary>어디서 소환됐든(맵·윷) 진행 중인 매치에 바로 대기 말로 합류 — 소환 즉시 말로 쓸 수 있다.</summary>
        void OnCharacterSummoned(CharacterAgent agent)
        {
            if (agent == null) return;
            if (match != null && !match.IsEnded)
            {
                string id = agent.Data != null ? agent.Data.id.ToString() : agent.name;
                string name = agent.Data != null && !string.IsNullOrEmpty(agent.Data.displayName)
                    ? agent.Data.displayName
                    : agent.name;
                if (match.TryAddPlayerPiece(id, name))
                    teamById[id] = agent;
            }
            // 윷 화면 연출 중이면 아이콘이 슬롯에 떨어진 뒤 갱신(슬롯이 먼저 사라지지 않게)
            if (IsOpen && !summonPresenting) HandlePiecesChanged();
        }

        /// <summary>메인 화면 소환 연출(암전 → 요괴 등장)과 같은 느낌을, 윷 화면 안에서 직접
        /// 재현한다 — SummonCeremony는 월드 스페이스 연출이라 윷 화면의 불투명 패널에
        /// 가려져 안 보인다(SummonPopup과 같은 문제). 대신 화면을 어둡게 했다 밝히면서 그
        /// 사이에 요괴를 소환해 "슬롯에 요괴가 들어오는" 느낌만 살린다.</summary>
        IEnumerator SummonCeremonyRoutine(CharacterId target)
        {
            summonPresenting = true;
            CeremonyGate.Begin();
            var dimGo = new GameObject("SummonDim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dimGo.transform.SetParent(root.transform, false);
            Stretch((RectTransform)dimGo.transform);
            dimGo.transform.SetAsLastSibling();
            var dimImg = dimGo.GetComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0.05f, 0f);

            float t = 0f;
            const float dimIn = 0.45f;
            while (t < dimIn)
            {
                t += Time.unscaledDeltaTime;
                // 완전히 새까맣게는 안 하고(0.72) — 소환하기 슬롯이 은은하게 비쳐서 "저기로
                // 떨어진다"는 느낌이 나게. 메인 화면 SummonCeremony와 같은 어둡기.
                dimImg.color = new Color(0f, 0f, 0.05f, Mathf.Lerp(0f, 0.72f, t / dimIn));
                yield return null;
            }

            // 절차(향·스폰·초기화·저장·매치 합류 이벤트)는 CharacterSummon.TrySummon
            var agent = CharacterSummon.TrySummon(target, font);

            // 소환된 요괴 아이콘이 화면 위에서 로스터의 "소환하기" 슬롯 자리로 떨어져 안착하는 연출 —
            // dimGo의 자식으로 붙여서 암전 위에 확실히 보이게 한다(YutMiniGame 쪽에 붙이면
            // 암전 오버레이보다 그리기 순서가 앞서서 안 보였다).
            if (agent != null)
                yield return PlaySummonDrop(dimGo.transform, agent);
            else
                yield return new WaitForSecondsRealtime(0.4f);

            t = 0f;
            const float dimOut = 0.5f;
            while (t < dimOut)
            {
                t += Time.unscaledDeltaTime;
                dimImg.color = new Color(0f, 0f, 0.05f, Mathf.Lerp(0.72f, 0f, t / dimOut));
                yield return null;
            }
            Destroy(dimGo);
            CeremonyGate.End();
            summonPresenting = false;

            if (agent == null)
            {
                ShowNotice("소환에 실패했습니다.", null);
                yield break;
            }

            HandlePiecesChanged();
            GameSaveBridge.SaveFromWorld();
        }

        /// <summary>소환된 요괴 아이콘을 화면 위쪽에서 로스터의 "소환하기" 슬롯 위치까지 떨어뜨린다.
        /// 슬롯 위치를 못 구하면(레이아웃 준비 전 등) 화면 중앙으로 대신 떨어뜨린다.</summary>
        IEnumerator PlaySummonDrop(Transform parent, CharacterAgent summoned)
        {
            var go = new GameObject("SummonDrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(64f, 64f);
            var img = go.GetComponent<Image>();
            var sprite = summoned != null ? CharacterSpawner.FirstSprite(summoned.Data) : null;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
                img.preserveAspect = true;
            }
            else
            {
                img.color = CharacterSummon.PlaceholderColor; // 아트 없을 때 폴백
            }

            Vector3? slotPos = miniGame != null ? miniGame.GetSummonSlotWorldPosition() : null;
            Vector3 targetPos = slotPos ?? rt.position;
            Vector3 startPos = targetPos + new Vector3(0f, 520f, 0f);
            rt.position = startPos;

            const float fall = 0.55f;
            float t = 0f;
            while (t < fall)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / fall);
                float eased = 1f - (1f - u) * (1f - u);
                rt.position = Vector3.Lerp(startPos, targetPos, eased);
                yield return null;
            }

            const float bounce = 0.2f;
            t = 0f;
            while (t < bounce)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / bounce);
                float bob = Mathf.Sin(u * Mathf.PI) * 10f * (1f - u);
                rt.position = targetPos + new Vector3(0f, bob, 0f);
                yield return null;
            }

            rt.position = targetPos;
            yield return new WaitForSecondsRealtime(0.2f);
            Destroy(go);
        }
    }
}
