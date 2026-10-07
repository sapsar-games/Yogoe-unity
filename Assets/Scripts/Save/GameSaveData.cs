using System;
using Yoegoe.Data;

namespace Yoegoe.Save
{
    /// <summary>
    /// 디스크/PlayerPrefs에 넣는 세이브 DTO만 둔다. 씬·Agent를 직접 참조하지 않는다.
    /// </summary>
    [Serializable]
    public class GameSaveData
    {
        /// <summary>버전 필드가 없는 세이브를 로드하면 1로 채워진다 — 마이그레이션 기준값.
        /// 새 세이브의 실제 버전 스탬프는 GameSaveBridge.CaptureFromWorld가 GameSaveMigration.CurrentVersion으로 찍는다.</summary>
        public int version = 1;

        /// <summary>저장 시각 (UTC DateTime.Ticks).</summary>
        public long savedAtUtcTicks;

        public EconomySave economy = new EconomySave();
        public PropSave[] props = Array.Empty<PropSave>();
        public AgentSave[] agents = Array.Empty<AgentSave>();

        /// <summary>진행 중이던 윷놀이 매치(있을 때만). null이면 매치 없음 — 앱을 껐다 켜거나
        /// 나갔다 들어와도 보드 상태(말 위치·완주 여부)를 그대로 이어간다.</summary>
        public YutMatchSave yutMatch;

        /// <summary>나루터 혼령 줄 · 기억 조각 (v2). 없던 세이브는 빈 줄에서 지금부터 시작.</summary>
        public Yoegoe.Economy.SpiritPier.PierSave pier;
    }

    [Serializable]
    public class YutMatchSave
    {
        public YutPieceSave[] playerPieces = Array.Empty<YutPieceSave>();
        public YutPieceSave opponentPiece = new YutPieceSave();

        /// <summary>이번 매치에서 뽑힌 특수 칸(엽전/재료보따리/물/보물상자) 배치 — 나갔다 들어오거나
        /// 앱을 껐다 켜도 말 위치는 그대로인데 특수 칸만 새로 섞이면 안 되므로 저장한다.
        /// nodeId/kind가 병렬 배열(같은 인덱스끼리 짝)인 이유는 JsonUtility가 Dictionary를
        /// 직렬화하지 못해서다.</summary>
        public int[] specialSquareNodeIds = Array.Empty<int>();
        public int[] specialSquareKinds = Array.Empty<int>();

        /// <summary>매 판 도전과제. kind &lt; 0 이면 과제 없음.</summary>
        public int challengeKind = -1;
        public bool challengeCompleted;
        public bool challengeFailed;
        public int challengeStreak;

        /// <summary>이번 매치에서 모았지만 아직 완주 전이라 경제에 안 넣은 재화.
        /// 중도 나가기·앱 재시작 후에도 이어가려면 저장해야 한다.</summary>
        public int pendingYeopjeon;
        public int pendingWater;
        public int pendingHyang;
        public int pendingAdTicket;
        public int pendingYutToken;
        public string[] pendingOfferingIds = Array.Empty<string>();
        public int[] pendingOfferingCounts = Array.Empty<int>();

        /// <summary>미지급 재료(CookingIngredientId)·부적(CookingCharmType) 병렬 배열.</summary>
        public int[] pendingIngredientIds = Array.Empty<int>();
        public int[] pendingIngredientCounts = Array.Empty<int>();
        public int[] pendingCharmTypes = Array.Empty<int>();
        public int[] pendingCharmCounts = Array.Empty<int>();
    }

    [Serializable]
    public class YutPieceSave
    {
        public string id;
        public string displayName;
        /// <summary>-1이면 대기(보드 밖).</summary>
        public int nodeId = -1;
        public bool finished;
        public int[] history = Array.Empty<int>();
    }

    [Serializable]
    public class EconomySave
    {
        public BigNumberSave merit = new BigNumberSave();
        /// <summary>앱 재시작 일괄 수거 미수령분.</summary>
        public BigNumberSave pendingBatchMerit = new BigNumberSave();
        public int yeopjeon;
        public int hyang;
        public int water;
        public int yutToken;
        public int yutTokenMax = 5;
        /// <summary>다음 윷 토큰 충전 예정 UTC ticks (2·10장: 30분마다 1개). 0이면 충전 대기 없음.</summary>
        public long yutTokenRegenNextUtcTicks;
        /// <summary>플레이어가 구매한 기물 수 (prebuilt 제외). 다음 구매 비용 n = 이 값+1.</summary>
        public int propsPurchasedCount;
        /// <summary>선물꾸러미 연속 빈손 / 첫 확정 / 광고보상권.</summary>
        public int giftMissStreak;
        public bool giftFirstGrantDone;
        public int adRewardTickets;

        /// <summary>12장 고가구점: 좌·우 공양 id, 다음 자동 갱신 UTC ticks.</summary>
        public string shopLeftOfferingId = "";
        public string shopRightOfferingId = "";
        public long shopNextRefreshUtcTicks;

        /// <summary>공양물·음식 인벤 (물 제외).</summary>
        public OfferingCountSave[] offerings;

        /// <summary>요리 재료 개수 (CookingIngredientId 순서, 물 칸은 0 — 물은 water).</summary>
        public int[] materials;

        /// <summary>4번째 잠긴 슬롯을 엽전 99로 열었는지 (구미호 소환 자리).</summary>
        public bool lockedSlotUnlocked;

        /// <summary>출석 윷점: 다음에 받을 칸(0=1일차), 마지막으로 처리한 날(yyyyMMdd, 새벽 4시 기준).</summary>
        public int attendanceNextDayIndex;
        public int attendanceLastHandledDayKey;

        /// <summary>특수 수집품 개수 (SpecialItemId 순서 — 황금쌀·황금꿀).</summary>
        public int[] specialItems;

        /// <summary>요리 부적 개수 (CookingCharmType 순서).</summary>
        public int[] charms;

        /// <summary>요리책 발견한 결과물 id.</summary>
        public string[] codexDiscovered;
    }

    [Serializable]
    public class OfferingCountSave
    {
        public string offeringId;
        public int count;
    }

    [Serializable]
    public class PropSave
    {
        public string propId;
        public int level = 1;
        /// <summary>건립 여부.</summary>
        public bool isBuilt = true;
        public BigNumberSave pendingMerit = new BigNumberSave();
        public bool isEndingProp;
        public string ownerCharacterId;

        /// <summary>자원 기물 보관 (물·엽전·재료 개수).</summary>
        public int storedResources;
        /// <summary>진행 중인 산출 주기 경과 초.</summary>
        public float cycleProgressSeconds;
        /// <summary>이번 만창 사이클 오버플로우 판정 완료(=정지) 여부.</summary>
        public bool overflowJudged;
        /// <summary>활터·약초밭 보관 재료 (CookingIngredientId 정수, 1개당 1칸).</summary>
        public int[] pendingIngredients = Array.Empty<int>();
        /// <summary>v1.3 사냥터·채집터에서 고른 목적지 id (비면 예전 표).</summary>
        public string destinationId = "";
    }

    [Serializable]
    public class AgentSave
    {
        public string characterId; // CharacterData.id 또는 displayName
        public float intimacy;
        public float stamina;
        public ActionState state;
        public float stateTimer;
        public float posX;
        public float posY;
        /// <summary>앉아/점유 중인 기물 propId. 없으면 빈 문자열.</summary>
        public string occupiedPropId;
        /// <summary>먹여서 공개된 선호 공양물 offeringId.</summary>
        public string[] revealedPreferredOfferingIds = Array.Empty<string>();
        /// <summary>황금음식 버프 끝나는 UTC ticks (0 = 없음).</summary>
        public long goldenBuffEndsUtcTicks;
    }

    [Serializable]
    public class BigNumberSave
    {
        public double mantissa;
        public int exponent;

        public static BigNumberSave From(Yoegoe.Core.BigNumber n) =>
            new BigNumberSave { mantissa = n.Mantissa, exponent = n.Exponent };

        public Yoegoe.Core.BigNumber ToBigNumber() =>
            new Yoegoe.Core.BigNumber(mantissa, exponent);
    }
}
