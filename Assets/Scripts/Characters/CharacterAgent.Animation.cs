using UnityEngine;
using Yoegoe.Data;

namespace Yoegoe.Characters
{
    /// <summary>
    /// 캐릭터 스프라이트 애니메이션.
    /// CharacterData의 walkDown/Left/Right/Up·stay·idle·fainted 배열을
    /// 이동 방향·ActionState에 맞춰 SpriteRenderer에 넣는다.
    /// </summary>
    public partial class CharacterAgent
    {
        /// <summary>화면 기준 바라보는 방향. Down=정면, Up=뒷모습, Left/Right=옆.</summary>
        private enum FacingDir { Down, Up, Left, Right }

        private const float AnimFrameInterval = 0.15f; // 걷기 1프레임 유지 시간(초)
        private const float FacingMoveThresholdSq = 0.00004f; // 이보다 작게 움직이면 "안 움직임" (노이즈 무시)
        private const float FacingAxisBias = 1.25f; // 좌우↔상하 전환 시, 새 축이 이 배율만큼 우세해야 바꿈 (깜빡임 방지)

        private Vector3 lastPosition; // 직전 프레임 위치 → moved 판정·목표 없을 때 facing 폴백용
        private FacingDir facing = FacingDir.Down;
        private int animFrame;   // 현재 재생 중인 프레임 인덱스
        private float animTimer; // 다음 프레임까지 누적 시간

        /// <summary>상태 전환 직후 등, 타이머 없이 즉시 0번 프레임을 찍을 때.</summary>
        private void ApplyAnimationFrameImmediate()
        {
            if (spriteRenderer == null || Data == null) return;
            bool wantsWalk = Stats.State == ActionState.Walking
                || Stats.State == ActionState.Playing;
            Sprite[] frames = ResolveAnimationFrames(wantsWalk, out bool flipX);
            if (frames != null && frames.Length > 0 && frames[0] != null)
                spriteRenderer.sprite = frames[0];
            spriteRenderer.flipX = flipX;
        }

        /// <summary>
        /// Update마다 호출. 가고자 하는 목표·상태로 방향·프레임을 고른 뒤 스프라이트에 반영.
        /// </summary>
        private void UpdateWalkAnimation(float dt)
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer == null || Data == null) return;

            // 1) 실제 이동량 — 걷기 사이클 on/off·목표 없을 때 facing 폴백만 사용
            //    (분리 밀림·경계 Clamp 때문에 delta로 방향을 잡으면 부정확함)
            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            bool moved = delta.sqrMagnitude > FacingMoveThresholdSq;

            // 2) 걷기 사이클을 돌릴지 (방향별 walk* 프레임 재생)
            //    - Walking: 항상
            //    - Playing: 실제로 움직이거나 방황 목표가 있을 때만 (멈추면 idle로)
            bool wantsWalkCycle =
                Stats.State == ActionState.Walking
                || (Stats.State == ActionState.Playing && (moved || wanderTarget.HasValue));

            // 3) facing = 이미 정해진 목적지 방향 (wanderTarget → destination → delta 폴백)
            if (TryGetWalkAim(out Vector3 aim))
                UpdateFacingFromDirection(aim);
            else if (moved)
                UpdateFacingFromDirection(delta);

            // 4) 프레임을 앞으로 넘길 상태인지 (걷기뿐 아니라 stay/idle/기절도 루프)
            bool advanceFrames = wantsWalkCycle || Stats.State == ActionState.Staying
                || Stats.State == ActionState.Fainted
                || Stats.State == ActionState.Playing;

            // 5) facing·상태에 맞는 스프라이트 배열 선택 (+ Left 폴백 시 flipX)
            Sprite[] frames = ResolveAnimationFrames(wantsWalkCycle, out bool flipX);
            if (frames == null || frames.Length == 0) return;

            // 6) 타이머로 다음 프레임 인덱스 진행 (걷기는 빠르게, 상태 애니는 느리게)
            if (advanceFrames)
            {
                float interval = wantsWalkCycle ? AnimFrameInterval : AnimFrameInterval * 2f;
                animTimer += dt;
                if (animTimer >= interval)
                {
                    animTimer -= interval;
                    animFrame = (animFrame + 1) % frames.Length;
                }
            }
            else
            {
                animFrame = 0;
                animTimer = 0f;
            }

            if (animFrame >= frames.Length)
                animFrame = 0;

            // 7) SpriteRenderer에 적용
            int idx = animFrame;
            if (frames[idx] != null)
                spriteRenderer.sprite = frames[idx];
            spriteRenderer.flipX = flipX;
        }

        /// <summary>
        /// 걷기의 "가고자 하는 지점". Movement가 이미 잡아 둔 목표를 그대로 쓴다.
        /// 방황 중이면 wanderTarget, 아니면 기물 destination.
        /// </summary>
        private bool TryGetWalkAim(out Vector3 aim)
        {
            aim = default;
            Vector3? target = null;
            if (wanderTarget.HasValue)
                target = wanderTarget.Value;
            else if (destination != null)
                target = destination.transform.position;

            if (!target.HasValue) return false;

            aim = target.Value - transform.position;
            aim.z = 0f;
            return aim.sqrMagnitude > FacingMoveThresholdSq;
        }

        /// <summary>
        /// 목표(또는 폴백 delta) 방향으로 facing을 정한다.
        /// +Y(화면 위) → 뒷모습(Up), -Y(화면 아래) → 정면(Down), ±X → Left/Right.
        /// FacingAxisBias로 대각선일 때 방향이 매 프레임 뒤집히지 않게 한다.
        /// </summary>
        private void UpdateFacingFromDirection(Vector3 dir)
        {
            float ax = Mathf.Abs(dir.x);
            float ay = Mathf.Abs(dir.y);
            FacingDir vertical = dir.y > 0f ? FacingDir.Up : FacingDir.Down;

            if (facing == FacingDir.Left || facing == FacingDir.Right)
            {
                // 지금 옆을 보고 있음 → 세로가 확실히 우세할 때만 상하로 전환
                if (ay > ax * FacingAxisBias)
                    facing = vertical;
                else if (ax >= ay)
                    facing = dir.x > 0f ? FacingDir.Right : FacingDir.Left;
            }
            else
            {
                // 지금 정면/뒤를 보고 있음 → 가로가 확실히 우세할 때만 좌우로 전환
                if (ax > ay * FacingAxisBias)
                    facing = dir.x > 0f ? FacingDir.Right : FacingDir.Left;
                else if (ay >= ax)
                    facing = vertical;
            }
        }

        /// <summary>
        /// wantsWalkCycle이면 방향별 walk* , 아니면 상태별 stay/idle/fainted.
        /// 해당 배열이 비면 idle → walk* 순으로 폴백.
        /// </summary>
        private Sprite[] ResolveAnimationFrames(bool wantsWalkCycle, out bool flipX)
        {
            flipX = false;
            if (wantsWalkCycle)
                return WalkFramesForFacing(out flipX);

            switch (Stats.State)
            {
                case ActionState.Staying:
                    if (HasFrames(Data.stay)) return Data.stay;
                    break;
                case ActionState.Playing:
                    if (HasFrames(Data.idle)) return Data.idle;
                    break;
                case ActionState.Fainted:
                    if (HasFrames(Data.fainted)) return Data.fainted;
                    break;
            }

            if (HasFrames(Data.idle)) return Data.idle;
            return WalkFramesForFacing(out flipX);
        }

        /// <summary>
        /// facing → CharacterData 스프라이트 슬롯.
        /// walkLeft가 비어 있으면 walkRight를 flipX로 뒤집어 쓴다.
        /// 선택한 방향 배열이 비면 walkDown으로 폴백.
        /// </summary>
        private Sprite[] WalkFramesForFacing(out bool flipX)
        {
            flipX = false;
            Sprite[] frames;
            switch (facing)
            {
                case FacingDir.Up:
                    frames = Data.walkUp;   // 뒷모습
                    break;
                case FacingDir.Right:
                    frames = Data.walkRight;
                    break;
                case FacingDir.Left:
                    if (HasFrames(Data.walkLeft))
                    {
                        frames = Data.walkLeft;
                    }
                    else if (HasFrames(Data.walkRight))
                    {
                        flipX = true;
                        frames = Data.walkRight;
                    }
                    else
                    {
                        frames = Data.walkLeft;
                    }
                    break;
                default:
                    frames = Data.walkDown; // 정면
                    break;
            }

            if (HasFrames(frames)) return frames;
            if (HasFrames(Data.walkDown))
            {
                flipX = false;
                return Data.walkDown;
            }
            return frames;
        }

        private static bool HasFrames(Sprite[] frames)
        {
            if (frames == null || frames.Length == 0) return false;
            for (int i = 0; i < frames.Length; i++)
                if (frames[i] != null) return true;
            return false;
        }
    }
}
