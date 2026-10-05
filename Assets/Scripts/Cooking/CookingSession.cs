using System;
using System.Collections.Generic;
using Yoegoe.Core;
using Yoegoe.Data;
using UnityEngine;

namespace Yoegoe.Cooking
{
    /// <summary>한 판 공양간 세션. UI는 GongyangganScreen.</summary>
    public class CookingSession
    {
        public const int GridSize = 5;
        public const int EmptyCellCount = 5;
        public const float BaseSeconds = 15f;
        public const float AdExtendSeconds = 15f;
        /// <summary>판에 올라가는 재료 최대 개수(25칸 − 빈칸 5). 이보다 적으면 시작 전 확인 팝업.</summary>
        public const int FullBoardMaterials = GridSize * GridSize - EmptyCellCount;
        /// <summary>이보다 적으면 어떤 레시피도 못 만들어 시작 불가.</summary>
        public const int MinMaterialsToCook = 2;

        public CookingIngredientId?[,] Grid { get; private set; }
        /// <summary>판 칸이 바뀔 때마다 +1 (새로 깔기·요리 완성·정산) — 무거운 판 계산을 캐시할 때 쓴다.</summary>
        public int BoardVersion { get; private set; }
        /// <summary>황금쌀·황금꿀 칸 (Grid에는 쌀·꿀로 들어간다). 이 칸이 들어간 음식은 황금음식으로 완성.</summary>
        public bool[,] Golden { get; private set; }
        public bool[,] Locked { get; private set; }
        public float TimeLeft { get; private set; }
        public bool Running { get; private set; }
        public bool Finished { get; private set; }
        public CookingCharmType PreCharm { get; private set; }
        public bool AllowDiagonal => PreCharm == CookingCharmType.Diagonal;
        public bool AllowAdExtend =>
            PreCharm != CookingCharmType.Recycle && PreCharm != CookingCharmType.Double;
        /// <summary>나가리 버튼: 사전 부적 없이 시작한 판 + 나가리 부적을 가지고 있을 때만 (쓰면 1개 소모).</summary>
        public bool ShowNagari => Running && PreCharm == CookingCharmType.None && HasNagariCharm;
        /// <summary>손님에게 요리를 건넨 뒤엔 그 판에서 부적(나가리)을 쓸 수 없다 — 버튼에 X.</summary>
        public bool CharmsLocked => GuestOrder != null && GuestOrder.Fulfilled;
        public bool CanUseNagari => ShowNagari && !CharmsLocked;
        static bool HasNagariCharm =>
            Yoegoe.Economy.GameEconomy.Instance != null
            && Yoegoe.Economy.GameEconomy.Instance.GetCharmCount(CookingCharmType.Cancel) > 0;
        /// <summary>시간이 다 됐고 광고 연장을 물어보는 중 (19장: 종료 후 광고 보고 +15초, 무제한). 판은 아직 정산 전.</summary>
        public bool AwaitingExtend { get; private set; }
        public bool ClairvoyanceActive { get; private set; }
        public float ClairvoyanceLeft { get; private set; }

        /// <summary>이번 판 완성품. golden = 황금쌀·황금꿀이 들어간 음식(황금음식).</summary>
        public readonly List<(CookingRecipe recipe, int count, bool golden)> Results = new List<(CookingRecipe, int, bool)>();
        /// <summary>판에 올라간 재료(황금 칸은 쌀·꿀로). 시작(<see cref="StartRound"/>) 때 인벤에서 차감된다 — 미리보기 중엔 차감 전.</summary>
        public readonly List<CookingIngredientId> SpentOnBoard = new List<CookingIngredientId>();
        /// <summary>이번 판에서 처음 발견한 레시피(결과물 id) — 결과창 금색, 나가리면 다시 잠금.</summary>
        public readonly List<string> NewlyDiscovered = new List<string>();
        /// <summary>판이 끝날 때 판에 남은 재료 — 결과창 '스러진 재료'(회수 부적이면 '회수한 재료').</summary>
        public readonly List<BoardItem> Leftover = new List<BoardItem>();

        /// <summary>선호 공양물 조합이 가능할 때 나타나는 주문 요괴. 없으면 null.</summary>
        public CookingGuestOrder GuestOrder { get; private set; }
        /// <summary>테스트용 — 손님 주문을 직접 꽂는다.</summary>
        public void SetGuestOrderForTest(CookingGuestOrder order) => GuestOrder = order;
        /// <summary>제자리 조리 중(익는 중·김·식음).</summary>
        public IReadOnlyList<CookingCookJob> ActiveCooks => cooks;
        public int PerfectCollectCount { get; private set; }

        /// <summary>판에 올라간 황금쌀·황금꿀 개수 (SpentOnBoard의 쌀·꿀 중 이만큼은 특수 수집품에서 뺀다).</summary>
        readonly Dictionary<SpecialItemId, int> goldenOnBoard = new Dictionary<SpecialItemId, int>();
        readonly List<CookingCookJob> cooks = new List<CookingCookJob>();
        readonly Dictionary<(int x, int y), int> cookAt = new Dictionary<(int, int), int>();
        int cookSeq;
        bool pendingDead;

        /// <summary>판 칸 하나 — 황금쌀이면 (Rice, golden).</summary>
        public readonly struct BoardItem
        {
            public readonly CookingIngredientId Id;
            public readonly bool Golden;
            public BoardItem(CookingIngredientId id, bool golden) { Id = id; Golden = golden; }
        }

        /// <summary>판에서 쌀·꿀 칸으로 쓰이는 황금 수집품.</summary>
        public static bool TryGoldenOf(CookingIngredientId id, out SpecialItemId golden)
        {
            switch (id)
            {
                case CookingIngredientId.Rice: golden = SpecialItemId.GoldenRice; return true;
                case CookingIngredientId.Honey: golden = SpecialItemId.GoldenHoney; return true;
                default: golden = default; return false;
            }
        }
        public int MaterialsOnBoard => SpentOnBoard.Count;
        public bool IsShortBoard => MaterialsOnBoard < FullBoardMaterials;
        public bool CanStart => !Running && !Finished && MaterialsOnBoard >= MinMaterialsToCook;

        readonly List<(int x, int y)> path = new List<(int, int)>();
        public IReadOnlyList<(int x, int y)> Path => path;

        public event Action Changed;
        public event Action RoundEnded;
        /// <summary>시간 종료 — 연장 가능한 판이면 정산 대신 이걸 알린다. UI가 <see cref="ExtendByAd"/> 또는 <see cref="FinishAfterTimeUp"/>.</summary>
        public event Action TimeUp;

        public void Prepare(CookingCharmType charm)
        {
            PreCharm = charm;
            Finished = false;
            Running = false;
            Results.Clear();
            SpentOnBoard.Clear();
            goldenOnBoard.Clear();
            NewlyDiscovered.Clear();
            Leftover.Clear();
            path.Clear();
            ClairvoyanceActive = false;
            GuestOrder = null;
            PerfectCollectCount = 0;
            ClearCooks();
            pendingDead = false;
            TimeLeft = ResolveLimit(charm);
            DealBoard();
            Changed?.Invoke();
        }

        static float ResolveLimit(CookingCharmType charm) => charm switch
        {
            CookingCharmType.PlusFive => BaseSeconds + 5f,
            CookingCharmType.Recycle => BaseSeconds - 4f,
            CookingCharmType.Double => 7f,
            _ => BaseSeconds
        };

        /// <summary>한 번도 완성 못 하는 판이 나오지 않도록, 풀리는 배치가 나올 때까지 재시도한다.
        /// 여기서는 배치(미리보기)만 — 인벤 차감은 <see cref="StartRound"/>에서 1회.</summary>
        const int DealBoardMaxAttempts = 30;

        void DealBoard()
        {
            CookingIngredientId?[,] grid = null;
            bool[,] golden = null;

            for (int attempt = 0; attempt < DealBoardMaxAttempts; attempt++)
            {
                var pool = BuildMaterialPool();
                var idx = LayoutIndices(pool.Count, UnityEngine.Random.Range);
                grid = new CookingIngredientId?[GridSize, GridSize];
                golden = new bool[GridSize, GridSize];
                for (int y = 0; y < GridSize; y++)
                for (int x = 0; x < GridSize; x++)
                {
                    if (idx[x, y] < 0) continue;
                    grid[x, y] = pool[idx[x, y]].Id;
                    golden[x, y] = pool[idx[x, y]].Golden;
                }
                if (CookingRecipeCatalog.AnyCompletable(grid, AllowDiagonal)) break;
                // 재료가 워낙 부족/편중돼 있으면 끝까지 안 풀릴 수 있음 — 그때는 마지막 시도 그대로 사용.
            }

            Grid = grid;
            Golden = golden;
            BoardVersion++;
            Locked = new bool[GridSize, GridSize];
            SpentOnBoard.Clear();
            goldenOnBoard.Clear();
            for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
            {
                if (!Grid[x, y].HasValue) continue;
                SpentOnBoard.Add(Grid[x, y].Value);
                if (Golden[x, y] && TryGoldenOf(Grid[x, y].Value, out var g))
                    goldenOnBoard[g] = goldenOnBoard.TryGetValue(g, out int n) ? n + 1 : 1;
            }
        }

        /// <summary>
        /// 판 배치 (19장): 재료(최대 20) + 빈칸 5를 <b>맨 아래 줄부터</b> 채운다. 재료가 모자라면 위쪽은 비고,
        /// 빈칸 5개는 채운 영역 안에서 랜덤. y=0이 맨 위, y=GridSize−1이 맨 아래.
        /// pool은 이미 섞인 순서대로 쓴다. randRange(min, maxExclusive).
        /// </summary>
        public static CookingIngredientId?[,] LayoutBoard(IReadOnlyList<CookingIngredientId> pool,
            Func<int, int, int> randRange)
        {
            var grid = new CookingIngredientId?[GridSize, GridSize];
            var idx = LayoutIndices(pool != null ? pool.Count : 0, randRange);
            for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
                if (idx[x, y] >= 0) grid[x, y] = pool[idx[x, y]];
            return grid;
        }

        /// <summary><see cref="LayoutBoard"/>의 배치 — 칸마다 pool 인덱스(빈칸·안 쓰는 칸 = −1).</summary>
        public static int[,] LayoutIndices(int poolCount, Func<int, int, int> randRange)
        {
            var grid = new int[GridSize, GridSize];
            for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
                grid[x, y] = -1;
            int materials = Math.Min(poolCount, FullBoardMaterials);
            if (materials == 0) return grid;
            int used = materials + EmptyCellCount;

            // 아래 줄부터 채울 칸 순서
            var cells = new List<(int x, int y)>(used);
            for (int y = GridSize - 1; y >= 0 && cells.Count < used; y--)
            for (int x = 0; x < GridSize && cells.Count < used; x++)
                cells.Add((x, y));

            var empties = new HashSet<int>();
            while (empties.Count < EmptyCellCount)
                empties.Add(randRange(0, used));

            int pi = 0;
            for (int i = 0; i < used; i++)
            {
                if (empties.Contains(i)) continue;
                var (x, y) = cells[i];
                grid[x, y] = pi++;
            }
            return grid;
        }

        /// <summary>보유 재료 + 황금쌀·황금꿀(쌀·꿀 칸, 황금 표시)을 섞은 풀.</summary>
        List<BoardItem> BuildMaterialPool()
        {
            var list = new List<BoardItem>();
            var eco = Yoegoe.Economy.GameEconomy.Instance;
            if (eco != null)
            {
                for (int i = 0; i < (int)CookingIngredientId.Count; i++)
                {
                    var id = (CookingIngredientId)i;
                    int n = eco.GetMaterialCount(id);
                    for (int k = 0; k < n; k++) list.Add(new BoardItem(id, false));
                    if (TryGoldenOf(id, out var golden))
                    {
                        int g = eco.GetSpecialItemCount(golden);
                        for (int k = 0; k < g; k++) list.Add(new BoardItem(id, true));
                    }
                }
            }
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        /// <summary>판 시작 — 판에 올라간 재료를 이때 인벤에서 뺀다. 인벤이 그사이 줄어 모자라면 false(아무것도 안 뺌).</summary>
        /// <summary>지금 판에서 만들 수 있는 요리 (상태 줄 — 발견한 건 이름, 아직 못 본 건 '?').</summary>
        public List<CookingRecipe> MakeableNow() =>
            Grid == null ? new List<CookingRecipe>() : CookingRecipeCatalog.CompletableRecipes(Grid, AllowDiagonal);

        public bool StartRound()
        {
            if (!CanStart) return false;
            var eco = Yoegoe.Economy.GameEconomy.Instance;
            if (eco != null)
            {
                var need = new Dictionary<CookingIngredientId, int>();
                foreach (var id in SpentOnBoard)
                    need[id] = need.TryGetValue(id, out int n) ? n + 1 : 1;
                foreach (var kv in goldenOnBoard)
                {
                    if (eco.GetSpecialItemCount(kv.Key) < kv.Value) return false;
                    var baseId = kv.Key == SpecialItemId.GoldenRice ? CookingIngredientId.Rice : CookingIngredientId.Honey;
                    need[baseId] -= kv.Value;
                }
                foreach (var kv in need)
                    if (eco.GetMaterialCount(kv.Key) < kv.Value) return false;
                foreach (var kv in need)
                    if (kv.Value > 0) eco.TrySpendMaterial(kv.Key, kv.Value);
                foreach (var kv in goldenOnBoard)
                    eco.AddSpecialItem(kv.Key, -kv.Value);
            }
            Running = true;
            pendingDead = false;
            PerfectCollectCount = 0;
            ClearCooks();
            GuestOrder = CookingGuestOrder.TryCreate(MakeableNow(), UnityEngine.Random.Range);
            if (PreCharm == CookingCharmType.Clairvoyance)
            {
                ClairvoyanceActive = true;
                ClairvoyanceLeft = 1f;
            }
            Changed?.Invoke();
            return true;
        }

        public void Tick(float dt)
        {
            if (Finished) return;
            // 연장 대기 중엔 화로(조리)도 멈춤
            if (AwaitingExtend) return;
            if (!Running) return;
            if (ClairvoyanceActive)
            {
                ClairvoyanceLeft -= dt;
                if (ClairvoyanceLeft <= 0f) ClairvoyanceActive = false;
            }
            TickCooks(dt);
            TimeLeft -= dt;
            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                // 회수·몰빵 판, 더 만들 게·꺼낼 요리 없는 판은 바로 정산
                bool canExtend = AllowAdExtend
                    && (cooks.Count > 0 || CookingRecipeCatalog.AnyCompletable(Grid, AllowDiagonal));
                if (canExtend)
                {
                    Running = false;
                    AwaitingExtend = true;
                    path.Clear();
                    Changed?.Invoke();
                    TimeUp?.Invoke();
                    return;
                }
                EndRound(timeUp: true);
                return;
            }
            Changed?.Invoke();
        }

        void TickCooks(float dt)
        {
            if (cooks.Count == 0) return;
            for (int i = 0; i < cooks.Count; i++)
                cooks[i].Tick(dt);
        }

        /// <summary>칸에 익는/식은 요리가 있으면 그 job.</summary>
        public CookingCookJob CookAt(int x, int y) =>
            cookAt.TryGetValue((x, y), out int id) ? FindCook(id) : null;

        CookingCookJob FindCook(int id)
        {
            for (int i = 0; i < cooks.Count; i++)
                if (cooks[i].Id == id) return cooks[i];
            return null;
        }

        /// <summary>김(퍼펙트)·식음 요리를 꺼낸다. 익는 중·연장 대기·종료 후면 false.</summary>
        public bool TryCollectCook(int x, int y)
        {
            if (!Running || Finished) return false;
            var job = CookAt(x, y);
            if (job == null || !job.CanCollect) return false;
            ResolveCook(job, job.IsPerfectWindow);
            return true;
        }

        /// <summary>시간 종료 후 광고 보고 +15초 — 횟수 제한 없음. 연장 중에도 나가리 가능.</summary>
        public bool ExtendByAd()
        {
            if (!AwaitingExtend || Finished) return false;
            AwaitingExtend = false;
            TimeLeft = AdExtendSeconds;
            Running = true;
            pendingDead = !CookingRecipeCatalog.AnyCompletable(Grid, AllowDiagonal);
            Changed?.Invoke();
            return true;
        }

        /// <summary>시간 종료 후 연장 안 함 → 정산.</summary>
        public void FinishAfterTimeUp()
        {
            if (!AwaitingExtend) return;
            AwaitingExtend = false;
            EndRound(timeUp: true);
        }

        /// <summary>화면을 닫을 때 — 진행 중이거나 연장을 묻는 중이면 지금까지 만든 걸로 정산(만든 요리를 잃지 않게).</summary>
        public void FinishNow()
        {
            if (Finished || (!Running && !AwaitingExtend)) return;
            AwaitingExtend = false;
            EndRound(timeUp: false);
        }

        public bool TryBeginPath(int x, int y)
        {
            if (!Running || Finished) return false;
            if (!InBounds(x, y) || Locked[x, y] || !Grid[x, y].HasValue) return false;
            if (cookAt.ContainsKey((x, y))) return false;
            path.Clear();
            path.Add((x, y));
            Changed?.Invoke();
            return true;
        }

        public bool TryExtendPath(int x, int y)
        {
            if (!Running || Finished || path.Count == 0) return false;
            if (!InBounds(x, y) || Locked[x, y] || !Grid[x, y].HasValue) return false;
            if (cookAt.ContainsKey((x, y))) return false;
            for (int i = 0; i < path.Count; i++)
                if (path[i].x == x && path[i].y == y) return false;
            var last = path[path.Count - 1];
            if (!CookingRecipeCatalog.Adjacent(last.x, last.y, x, y, AllowDiagonal)) return false;
            if (path.Count >= 3) return false;

            // 도달 가능성: 확장 후 완성 가능해야 함
            path.Add((x, y));
            if (!PathCanStillComplete())
            {
                path.RemoveAt(path.Count - 1);
                return false;
            }
            Changed?.Invoke();
            return true;
        }

        public void EndPath()
        {
            if (!Running || path.Count == 0) return;
            var ings = new List<CookingIngredientId>(path.Count);
            bool anyGolden = false;
            for (int i = 0; i < path.Count; i++)
            {
                var (px, py) = path[i];
                if (Grid[px, py].HasValue) ings.Add(Grid[px, py].Value);
                if (Golden[px, py]) anyGolden = true;
            }

            if (CookingRecipeCatalog.TryMatch(ings, out var recipe))
            {
                var cells = path.ToArray();
                // 황금쌀·황금꿀이 들어간 음식 = 황금음식 (공양물은 황금 버전 없음 — 일반으로)
                bool golden = anyGolden && recipe.Kind == CookingResultKind.Food;
                StartCook(recipe, cells, golden);
                path.Clear();
                if (!CookingRecipeCatalog.AnyCompletable(Grid, AllowDiagonal))
                    pendingDead = true;
                Changed?.Invoke();
                return;
            }

            path.Clear();
            Changed?.Invoke();
        }

        void StartCook(CookingRecipe recipe, (int x, int y)[] cells, bool golden)
        {
            var job = new CookingCookJob(++cookSeq, recipe, cells, golden);
            cooks.Add(job);
            for (int i = 0; i < cells.Length; i++)
            {
                var (px, py) = cells[i];
                Grid[px, py] = null;
                Golden[px, py] = false;
                Locked[px, py] = true;
                cookAt[(px, py)] = job.Id;
            }
            BoardVersion++;
        }

        void ResolveCook(CookingCookJob job, bool perfect)
        {
            if (job == null || job.Phase == CookingCookPhase.Done) return;
            job.MarkDone();
            cooks.Remove(job);
            for (int i = 0; i < job.Cells.Length; i++)
                cookAt.Remove(job.Cells[i]);

            var recipe = job.Recipe;
            if (perfect) PerfectCollectCount++;

            bool deliveredToGuest = GuestOrder != null
                && !GuestOrder.Fulfilled && !GuestOrder.Failed
                && GuestOrder.Matches(recipe);

            if (deliveredToGuest)
            {
                // 주문 배달: 인벤 미지급(퍼펙트여도 음식 ×2 없음). 친밀도·기력만 배율 적용.
                GuestOrder.Deliver(perfect);
            }
            else
            {
                int baseQty = PreCharm == CookingCharmType.Double ? 2 : 1;
                int qty = baseQty * (perfect ? 2 : 1);
                AddResult(recipe, qty, job.Golden);
            }

            if (CookingCodex.Discover(recipe.Id) && !NewlyDiscovered.Contains(recipe.Id))
                NewlyDiscovered.Add(recipe.Id);

            Changed?.Invoke();
            AfterCookResolved();
        }

        void AfterCookResolved()
        {
            if (cooks.Count > 0) return;
            if (!Running || Finished) return;
            if (pendingDead || !CookingRecipeCatalog.AnyCompletable(Grid, AllowDiagonal))
                EndRound(timeUp: false);
        }

        void ClearCooks()
        {
            cooks.Clear();
            cookAt.Clear();
            cookSeq = 0;
        }

        bool PathCanStillComplete()
        {
            if (path.Count == 2 || path.Count == 3)
            {
                var ings = new List<CookingIngredientId>();
                for (int i = 0; i < path.Count; i++)
                    ings.Add(Grid[path[i].x, path[i].y].Value);
                if (CookingRecipeCatalog.TryMatch(ings, out _)) return true;
            }
            if (path.Count >= 3) return false;

            // 1칸: 이웃으로 레시피 확장 가능 여부
            var last = path[path.Count - 1];
            for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
            {
                if (Locked[x, y] || !Grid[x, y].HasValue) continue;
                bool onPath = false;
                for (int i = 0; i < path.Count; i++)
                    if (path[i].x == x && path[i].y == y) { onPath = true; break; }
                if (onPath) continue;
                if (!CookingRecipeCatalog.Adjacent(last.x, last.y, x, y, AllowDiagonal)) continue;

                var trial = new List<CookingIngredientId>();
                for (int i = 0; i < path.Count; i++)
                    trial.Add(Grid[path[i].x, path[i].y].Value);
                trial.Add(Grid[x, y].Value);
                if (CookingRecipeCatalog.TryMatch(trial, out _)) return true;

                // 2칸 경로면 한 칸 더 필요한 3재료 레시피도 허용
                if (path.Count == 1)
                {
                    // BFS one more step
                    for (int y2 = 0; y2 < GridSize; y2++)
                    for (int x2 = 0; x2 < GridSize; x2++)
                    {
                        if ((x2 == x && y2 == y) || Locked[x2, y2] || !Grid[x2, y2].HasValue) continue;
                        bool on = false;
                        for (int i = 0; i < path.Count; i++)
                            if (path[i].x == x2 && path[i].y == y2) { on = true; break; }
                        if (on) continue;
                        if (!CookingRecipeCatalog.Adjacent(x, y, x2, y2, AllowDiagonal)) continue;
                        var t3 = new List<CookingIngredientId>(trial) { Grid[x2, y2].Value };
                        if (CookingRecipeCatalog.TryMatch(t3, out _)) return true;
                    }
                }
            }
            return false;
        }

        void AddResult(CookingRecipe recipe, int count, bool golden)
        {
            for (int i = 0; i < Results.Count; i++)
            {
                if (Results[i].recipe.Id == recipe.Id && Results[i].golden == golden)
                {
                    Results[i] = (recipe, Results[i].count + count, golden);
                    return;
                }
            }
            Results.Add((recipe, count, golden));
        }

        /// <summary>판의 재료 하나를 인벤으로 되돌린다 (황금 칸이면 황금쌀·황금꿀로).</summary>
        static void ReturnToInventory(Yoegoe.Economy.GameEconomy eco, CookingIngredientId id, bool golden)
        {
            if (golden && TryGoldenOf(id, out var g)) eco.AddSpecialItem(g, 1);
            else eco.AddMaterial(id, 1);
        }

        public void CancelNagari()
        {
            if (!CanUseNagari || Finished) return;
            if (!Yoegoe.Economy.GameEconomy.Instance.TrySpendCharm(CookingCharmType.Cancel)) return;
            // 결과 취소 + 재료 전량 반환
            Results.Clear();
            ClearCooks();
            GuestOrder = null;
            var eco = Yoegoe.Economy.GameEconomy.Instance;
            if (eco != null)
            {
                var golden = new Dictionary<SpecialItemId, int>(goldenOnBoard);
                foreach (var id in SpentOnBoard)
                {
                    int left = 0;
                    bool g = TryGoldenOf(id, out var gid) && golden.TryGetValue(gid, out left) && left > 0;
                    if (g) golden[gid] = left - 1;
                    ReturnToInventory(eco, id, g);
                }
            }
            SpentOnBoard.Clear();
            goldenOnBoard.Clear();
            // 그 판에서 새로 발견한 레시피는 도감에서 다시 잠긴다 (이미 예전에 발견한 건 유지)
            foreach (var id in NewlyDiscovered) CookingCodex.Forget(id);
            NewlyDiscovered.Clear();
            EndRound(timeUp: false, nagari: true);
        }

        void EndRound(bool timeUp, bool nagari = false)
        {
            if (Finished) return;
            Running = false;
            Finished = true;
            AwaitingExtend = false;
            path.Clear();
            BoardVersion++;
            pendingDead = false;

            // 못 꺼낸 요리는 받지 못함. 주문 미제공이면 실망.
            if (!nagari && GuestOrder != null && !GuestOrder.Fulfilled)
                GuestOrder.MarkFailed();
            ClearCooks();

            var eco = Yoegoe.Economy.GameEconomy.Instance;
            Leftover.Clear();
            if (!nagari)
            {
                for (int y = 0; y < GridSize; y++)
                for (int x = 0; x < GridSize; x++)
                    if (Grid[x, y].HasValue) Leftover.Add(new BoardItem(Grid[x, y].Value, Golden[x, y]));
                if (PreCharm == CookingCharmType.Recycle && eco != null)
                {
                    // 남은 재료 반환
                    for (int y = 0; y < GridSize; y++)
                    for (int x = 0; x < GridSize; x++)
                    {
                        if (Grid[x, y].HasValue)
                            ReturnToInventory(eco, Grid[x, y].Value, Golden[x, y]);
                    }
                }
                // 완성품 → 인벤 (음식/공양물 id)
                if (eco != null)
                {
                    foreach (var (recipe, count, golden) in Results)
                    {
                        if (golden)
                            eco.AddCookingProduct(Yoegoe.Data.OfferingCatalog.GoldenIdOf(recipe.Id),
                                "황금 " + recipe.DisplayName, recipe.Kind, count);
                        else
                            eco.AddCookingProduct(recipe.Id, recipe.DisplayName, recipe.Kind, count);
                    }
                }
            }

            Changed?.Invoke();
            RoundEnded?.Invoke();
        }

        static bool InBounds(int x, int y) =>
            x >= 0 && y >= 0 && x < GridSize && y < GridSize;
    }
}
