using System;
using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.UI;

namespace Yoegoe.Characters
{
    /// <summary>점유 아트·자물쇠·더미/자원 라벨·황금 반짝임. (PropSlot 분할 — 본체는 PropSlot.cs)</summary>
    public partial class PropSlot
    {
        void RefreshGoldenSparkle()
        {
            bool want = IsBuilt && HasGoldenPending && spriteRenderer != null;
            if (want)
            {
                float u = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                spriteRenderer.color = Color.Lerp(Color.white, new Color(1f, 0.86f, 0.35f, 1f), u);
                sparkling = true;
            }
            else if (sparkling)
            {
                sparkling = false;
                if (spriteRenderer != null && IsBuilt) spriteRenderer.color = Color.white;
            }
        }

        /// <summary>기물 스프라이트 윗변 중앙 + pad (연출·드래그 마커 위치).</summary>
        public Vector3 TopAnchorWorld(float pad)
        {
            if (spriteRenderer != null && spriteRenderer.sprite != null)
                return new Vector3(transform.position.x, spriteRenderer.bounds.max.y + pad, transform.position.z);
            return transform.position + Vector3.up * (0.25f + pad);
        }

        /// <summary>더미 UI를 즉시 갱신 (LateUpdate 대기 없이).</summary>
        public void ForceRefreshPileLabel() => RefreshPileLabel();

        /// <summary>
        /// 보관 라벨(***·숫자) 히트 — 라벨이 기물 bounds 밖으로 나와 있어도 그 기물 탭으로 (본체 탭은 MapPointerRouter).
        /// </summary>
        public bool TryGetPileLabelHitScore(Vector3 world, float padding, out float score)
        {
            score = float.MaxValue;
            if (!HasPendingCollectible) return false;
            if (pileLabel == null || !pileLabel.gameObject.activeInHierarchy) return false;

            var mr = pileLabel.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Bounds b = mr.bounds;
                b.Expand(Mathf.Max(0f, padding));
                Vector3 p = world;
                p.z = b.center.z;
                if (!b.Contains(p)) return false;
                score = Vector2.Distance(b.center, p);
                return true;
            }

            // MeshRenderer 없을 때: LowerCenter 기준 대략 박스
            Vector3 c = pileLabel.transform.position;
            float halfW = 0.4f + Mathf.Max(0f, padding);
            float h = 0.85f + Mathf.Max(0f, padding);
            if (Mathf.Abs(world.x - c.x) > halfW) return false;
            if (world.y < c.y - padding || world.y > c.y + h) return false;
            score = Vector2.Distance(new Vector2(c.x, c.y + h * 0.5f), world);
            return true;
        }

        /// <summary>Main이 건립 시 쓸 스프라이트·틴트를 기억.</summary>
        public void SetBuiltAppearance(Sprite sprite, Color tint, Sprite occupiedByOwner = null)
        {
            builtSprite = sprite;
            builtTint = tint;
            occupiedByOwnerSprite = occupiedByOwner != null
                ? occupiedByOwner
                : data != null ? data.occupiedByOwnerSprite : null;
        }

        /// <summary>주인 전용 점유 아트가 있으면 기물 스프라이트를 바꾸고, 캐릭터 본체를 숨긴다.</summary>
        public void RefreshOccupancyVisual()
        {
            bool useOccupied = ShouldShowOwnerOccupationArt();

            if (spriteRenderer != null && IsBuilt)
            {
                Sprite next = useOccupied
                    ? (occupiedByOwnerSprite != null ? occupiedByOwnerSprite : builtSprite)
                    : builtSprite;
                if (next != null) spriteRenderer.sprite = next;
                spriteRenderer.color = Color.white;
                spriteRenderer.enabled = true;
            }

            // 이전 숨김 복구
            if (hiddenOccupantVisual != null && (!useOccupied || hiddenOccupantVisual != Occupant))
            {
                hiddenOccupantVisual.SetSpriteVisible(true);
                hiddenOccupantVisual = null;
            }

            if (useOccupied && Occupant != null)
            {
                Occupant.SetSpriteVisible(false);
                hiddenOccupantVisual = Occupant;
            }
        }

        private bool ShouldShowOwnerOccupationArt()
        {
            if (!IsOccupied || Occupant == null || Occupant.Data == null) return false;
            Sprite art = occupiedByOwnerSprite != null
                ? occupiedByOwnerSprite
                : (data != null ? data.occupiedByOwnerSprite : null);
            if (art == null) return false;
            if (data == null) return false;
            if (!data.hasUniqueEndingAnimation) return false;
            return data.isEndingProp && data.owner == Occupant.Data.id;
        }

        private void ApplyBuiltVisual()
        {
            if (lockLabel != null) lockLabel.gameObject.SetActive(false);

            if (meshRenderer != null && !(meshRenderer is SpriteRenderer))
            {
                meshRenderer.enabled = true;
                if (meshRenderer.material != null)
                    meshRenderer.material.color = builtTint;
            }

            RefreshOccupancyVisual();
        }

        private void ApplyLockVisual()
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(0.35f, 0.35f, 0.4f, 0.55f);
                if (builtSprite != null) spriteRenderer.sprite = builtSprite;
            }
            if (meshRenderer != null && !(meshRenderer is SpriteRenderer))
            {
                meshRenderer.enabled = true;
                if (meshRenderer.material != null)
                    meshRenderer.material.color = new Color(0.25f, 0.25f, 0.3f, 0.8f);
            }
            EnsureLockLabel();
            lockLabel.gameObject.SetActive(true);
        }

        private void RefreshLockVisual()
        {
            if (IsBuilt)
            {
                if (lockLabel != null && lockLabel.gameObject.activeSelf)
                    lockLabel.gameObject.SetActive(false);
                return;
            }
            EnsureLockLabel();
            if (!lockLabel.gameObject.activeSelf)
                lockLabel.gameObject.SetActive(true);
            lockLabel.transform.position = transform.position + Vector3.up * 0.55f;
        }

        private void EnsureLockLabel()
        {
            if (lockLabel != null) return;
            var go = new GameObject(name + "_Lock");
            lockLabel = go.AddComponent<TextMesh>();
            lockLabel.anchor = TextAnchor.MiddleCenter;
            lockLabel.alignment = TextAlignment.Center;
            lockLabel.characterSize = 0.07f;
            lockLabel.fontSize = UiFonts.Size(42);
            lockLabel.color = new Color(0.9f, 0.85f, 0.7f, 1f);
            if (sharedPileFont == null)
                sharedPileFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (sharedPileFont != null) lockLabel.font = sharedPileFont;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 480;
        }

        private void RefreshPileLabel()
        {
            // 공덕은 버드나무에 모인 것으로 보여 준다 — 기물 위 더미 라벨 없음 (7-4)
            if (ResourceType == PropResourceType.Merit && MeritWillow.Instance != null)
            {
                if (pileLabel != null && pileLabel.gameObject.activeSelf)
                    pileLabel.gameObject.SetActive(false);
                return;
            }

            if (IsResourceProp)
            {
                RefreshResourceLabel();
                return;
            }

            int stage = GetPileStage();
            if (stage <= 0)
            {
                if (pileLabel != null && pileLabel.gameObject.activeSelf)
                    pileLabel.gameObject.SetActive(false);
                lastPileStage = 0;
                lastPileAmount = null;
                lastPileRounded = int.MinValue;
                lastPileDisplayKey = double.NaN;
                return;
            }

            EnsurePileLabel();
            if (!pileLabel.gameObject.activeSelf)
                pileLabel.gameObject.SetActive(true);

            // 표시 문자열이 실제로 바뀔 때만 TextMesh 갱신 (매 프레임 할당 → GC 스파이크 방지)
            double v = Math.Abs(PendingMerit.ToDouble());
            bool textChanged;
            if (v < 1000d)
            {
                int rounded = Mathf.RoundToInt((float)v);
                textChanged = stage != lastPileStage || rounded != lastPileRounded;
                if (textChanged)
                {
                    lastPileStage = stage;
                    lastPileRounded = rounded;
                    lastPileAmount = StarPrefix(stage) + rounded;
                    pileLabel.text = lastPileAmount;
                }
            }
            else
            {
                // 큰 수는 표시 단위가 바뀔 때만
                double key = Math.Round(PendingMerit.Mantissa, 2) * 1000 + PendingMerit.Exponent;
                textChanged = stage != lastPileStage || key != lastPileDisplayKey;
                if (textChanged)
                {
                    lastPileStage = stage;
                    lastPileDisplayKey = key;
                    lastPileAmount = StarPrefix(stage) + PendingMerit.ToDisplayString();
                    pileLabel.text = lastPileAmount;
                }
            }

            pileLabel.transform.position = GetPileLabelWorldPos();
            EnsurePileLabelSorting();
        }

        /// <summary>자원 기물 라벨: "물 3/6" · 만창이면 붉게.</summary>
        void RefreshResourceLabel()
        {
            if (!HasPendingResources)
            {
                if (pileLabel != null && pileLabel.gameObject.activeSelf)
                    pileLabel.gameObject.SetActive(false);
                lastResourceStored = -1;
                return;
            }

            EnsurePileLabel();
            if (!pileLabel.gameObject.activeSelf)
                pileLabel.gameObject.SetActive(true);

            int stored = storage.Stored, cap = ResourceCapacity;
            bool halted = IsStorageHalted;
            if (stored != lastResourceStored || cap != lastResourceCapacity || halted != lastResourceHalted)
            {
                lastResourceStored = stored;
                lastResourceCapacity = cap;
                lastResourceHalted = halted;
                pileLabel.text = ResourceLabel(ResourceType) + " " + stored + "/" + cap;
                pileLabel.color = halted ? new Color(1f, 0.55f, 0.45f, 1f) : new Color(1f, 0.92f, 0.55f, 1f);
            }
            pileLabel.transform.position = GetPileLabelWorldPos();
            EnsurePileLabelSorting();
        }

        static string ResourceLabel(PropResourceType type)
        {
            switch (type)
            {
                case PropResourceType.Water: return "물";
                default: return "재료";
            }
        }

        Vector3 GetPileLabelWorldPos()
        {
            float topY = transform.position.y + 0.85f;
            if (spriteRenderer != null && spriteRenderer.enabled && spriteRenderer.sprite != null)
                topY = spriteRenderer.bounds.max.y;
            // 엔딩 점유 아트처럼 키가 큰 기물도 숫자게 스프라이트 위로 뜨게
            return new Vector3(transform.position.x, topY + 0.28f, transform.position.z);
        }

        void EnsurePileLabelSorting()
        {
            if (pileLabel == null) return;
            var mr = pileLabel.GetComponent<MeshRenderer>();
            if (mr == null) return;
            // 기물·캐릭터(수백대)보다 항상 앞에. TextMesh는 sortingOrder가 먹히도록 명시.
            mr.sortingOrder = 1200;
        }

        private static string StarPrefix(int stage)
        {
            switch (stage)
            {
                case 1: return "*\n";
                case 2: return "**\n";
                case 3: return "***\n";
                case 4: return "****\n";
                default: return "*****\n";
            }
        }

        private void EnsurePileLabel()
        {
            if (pileLabel != null) return;

            var go = new GameObject("MeritPile");
            // 부모 스케일(기물 propScale)에 숫자가 찌그러지지 않게 월드에 독립
            go.transform.SetParent(null, false);
            pileLabel = go.AddComponent<TextMesh>();
            pileLabel.anchor = TextAnchor.LowerCenter;
            pileLabel.alignment = TextAlignment.Center;
            pileLabel.characterSize = 0.08f;
            pileLabel.fontSize = UiFonts.Size(48);
            pileLabel.color = new Color(1f, 0.92f, 0.55f, 1f);
            if (sharedPileFont == null)
                sharedPileFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (sharedPileFont != null) pileLabel.font = sharedPileFont;
            EnsurePileLabelSorting();
        }
    }
}
