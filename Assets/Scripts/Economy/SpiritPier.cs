using System;
using System.Collections.Generic;
using Yoegoe.Cooking;
using Yoegoe.Data;

namespace Yoegoe.Economy
{
    /// <summary>혼령 외형 4종 (v1.3 나루터). 말투는 대사 시트 spirit_lines.</summary>
    public enum SpiritKind { Woman = 0, Man = 1, Elder = 2, Child = 3 }

    /// <summary>
    /// 나루터 혼령 줄 (v1.3) — 규칙과 저장만. 화면은 따로.
    /// - spiritIntervalMinutes(20)마다 한 명, 줄이 spiritQueueMax(5)보다 적을 때만 시간이 흐른다.
    /// - spiritStayMinutes(180) 안에 대접받지 못하면 떠난다. 벌 없음.
    /// - 주문은 도착하는 순간 "가진 재료로 지을 수 있거나 창고에 있는 요리" 중에서 (줄에 이미 있는 요리는 되도록 피함).
    ///   지을 수 있는 요리가 하나도 없으면 오지 않는다(타이머는 다 찬 채로 기다림).
    /// - 오프라인 동안에도 온다: 다시 켤 때 <see cref="Advance"/>가 지난 시간을 1분 단위로 따라잡는다
    ///   (그동안의 주문은 지금 창고·재료로 판단).
    /// - 내주면 기억 조각: 음식 spiritPiecesFood(1) · 공양물 spiritPiecesOffering(2).
    /// </summary>
    public static class SpiritPier
    {
        public sealed class Spirit
        {
            public int Id;
            public SpiritKind Kind;
            public string DishId;
            public long ArrivedUtcTicks;
            /// <summary>선착장 자리 0~(max-1).</summary>
            public int Slot;

            public float MinutesLeft(DateTime utcNow) =>
                Math.Max(0f, GameSettings.SpiritStayMinutes - (float)(utcNow - new DateTime(ArrivedUtcTicks, DateTimeKind.Utc)).TotalMinutes);
        }

        /// <summary>오프라인 따라잡기 상한 — 이만큼이면 줄 상태가 충분히 정해진다.</summary>
        public const float MaxCatchUpMinutes = 24f * 60f;

        static readonly List<Spirit> spirits = new List<Spirit>();
        public static IReadOnlyList<Spirit> Spirits => spirits;

        /// <summary>다음 혼령까지 쌓인 분.</summary>
        public static float TimerMinutes { get; private set; }
        public static int NextId { get; private set; } = 1;
        /// <summary>마지막으로 따라잡은 시각 (UTC ticks). 0이면 아직 시작 안 함.</summary>
        public static long LastUtcTicks { get; private set; }
        /// <summary>모은 기억 조각 — 요괴를 가리지 않고 한데 쌓인다.</summary>
        public static int MemoryPieces { get; private set; }

        public static event Action<Spirit> Arrived;
        public static event Action<Spirit> Left;
        /// <summary>(혼령, 받은 조각 수)</summary>
        public static event Action<Spirit, int> Served;
        public static event Action Changed;

        /// <summary>테스트·처음 시작: 빈 줄, 지금부터 시간이 흐른다.</summary>
        public static void Reset(DateTime utcNow)
        {
            spirits.Clear();
            TimerMinutes = 0f;
            NextId = 1;
            LastUtcTicks = utcNow.Ticks;
            MemoryPieces = 0;
            Changed?.Invoke();
        }

        /// <summary>
        /// utcNow 까지 시간을 흘린다. orderPool = 지금 주문할 수 있는 요리 id 목록, randRange(min, maxExclusive).
        /// </summary>
        public static void Advance(DateTime utcNow, Func<IReadOnlyList<string>> orderPool, Func<int, int, int> randRange)
        {
            if (LastUtcTicks <= 0) { LastUtcTicks = utcNow.Ticks; return; }
            var last = new DateTime(LastUtcTicks, DateTimeKind.Utc);
            double elapsed = (utcNow - last).TotalMinutes;
            if (elapsed <= 0) return;
            if (elapsed > MaxCatchUpMinutes)
            {
                last = utcNow.AddMinutes(-MaxCatchUpMinutes);
                elapsed = MaxCatchUpMinutes;
            }

            float interval = Math.Max(1f, GameSettings.SpiritIntervalMinutes);
            int max = Math.Max(1, GameSettings.SpiritQueueMax);
            float stay = GameSettings.SpiritStayMinutes;
            bool changed = false;
            double t = 0;
            while (t < elapsed - 1e-9)
            {
                double step = Math.Min(1.0, elapsed - t);
                t += step;
                var now = last.AddMinutes(t);

                // 떠나기
                for (int i = spirits.Count - 1; i >= 0; i--)
                {
                    var s = spirits[i];
                    if ((now - new DateTime(s.ArrivedUtcTicks, DateTimeKind.Utc)).TotalMinutes >= stay)
                    {
                        spirits.RemoveAt(i);
                        Left?.Invoke(s);
                        changed = true;
                    }
                }

                // 오기 — 줄이 다 차 있으면 시간이 흐르지 않는다
                if (spirits.Count < max)
                {
                    TimerMinutes += (float)step;
                    while (TimerMinutes >= interval && spirits.Count < max)
                    {
                        if (!TrySpawn(now, orderPool, randRange)) { TimerMinutes = interval; break; }
                        TimerMinutes -= interval;
                        changed = true;
                    }
                }
                else TimerMinutes = 0f;
            }
            LastUtcTicks = utcNow.Ticks;
            if (changed) Changed?.Invoke();
        }

        static bool TrySpawn(DateTime at, Func<IReadOnlyList<string>> orderPool, Func<int, int, int> randRange)
        {
            var pool = orderPool?.Invoke();
            if (pool == null || pool.Count == 0 || randRange == null) return false;

            var fresh = new List<string>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
                if (!string.IsNullOrEmpty(pool[i]) && !InQueue(pool[i])) fresh.Add(pool[i]);
            var from = fresh.Count > 0 ? (IReadOnlyList<string>)fresh : pool;
            string dish = from[randRange(0, from.Count)];
            if (string.IsNullOrEmpty(dish)) return false;

            int max = Math.Max(1, GameSettings.SpiritQueueMax);
            int slot = 0;
            while (slot < max && SlotTaken(slot)) slot++;

            var s = new Spirit
            {
                Id = NextId++,
                Kind = (SpiritKind)randRange(0, 4),
                DishId = dish,
                ArrivedUtcTicks = at.Ticks,
                Slot = slot,
            };
            spirits.Add(s);
            Arrived?.Invoke(s);
            return true;
        }

        static bool InQueue(string dishId)
        {
            for (int i = 0; i < spirits.Count; i++)
                if (string.Equals(spirits[i].DishId, dishId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool SlotTaken(int slot)
        {
            for (int i = 0; i < spirits.Count; i++) if (spirits[i].Slot == slot) return true;
            return false;
        }

        public static Spirit Find(int id)
        {
            for (int i = 0; i < spirits.Count; i++) if (spirits[i].Id == id) return spirits[i];
            return null;
        }

        /// <summary>그 요리가 공양물이면 조각 2, 음식이면 1 (시트 game_settings).</summary>
        public static int PiecesFor(string dishId)
        {
            bool offering = CookingCodex.TryGetRecipe(dishId, out var r) && r.Kind == CookingResultKind.Offering;
            return offering ? GameSettings.SpiritPiecesOffering : GameSettings.SpiritPiecesFood;
        }

        /// <summary>창고에 청한 요리가 있는지 (보통 + 황금).</summary>
        public static int StockOf(GameEconomy eco, string dishId)
        {
            if (eco == null || string.IsNullOrEmpty(dishId)) return 0;
            return eco.GetOfferingCount(dishId) + eco.GetOfferingCount(OfferingCatalog.GoldenIdOf(dishId));
        }

        /// <summary>
        /// 혼령에게 청한 요리를 내준다. 보통 요리를 먼저 쓰고, 없으면 황금 요리.
        /// 성공하면 조각을 쌓고 그 혼령은 떠난다.
        /// </summary>
        public static bool TryServe(int spiritId, GameEconomy eco, out int pieces)
        {
            pieces = 0;
            var s = Find(spiritId);
            if (s == null || eco == null) return false;

            var plain = OfferingCatalog.Find(s.DishId);
            var golden = OfferingCatalog.FindGolden(s.DishId);
            bool spent = (plain != null && eco.TrySpendOffering(plain, 1))
                         || (golden != null && eco.TrySpendOffering(golden, 1));
            if (!spent) return false;

            pieces = PiecesFor(s.DishId);
            MemoryPieces += pieces;
            spirits.Remove(s);
            Served?.Invoke(s, pieces);
            Changed?.Invoke();
            return true;
        }

        /// <summary>지금 주문으로 나올 수 있는 요리: 창고에 있거나(보통·황금) 가진 재료로 지을 수 있는 것.</summary>
        public static List<string> BuildOrderPool(GameEconomy eco)
        {
            var list = new List<string>();
            if (eco == null) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recipes = CookingRecipeCatalog.Recipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                if (string.IsNullOrEmpty(r.Id) || seen.Contains(r.Id)) continue;
                if (StockOf(eco, r.Id) > 0 || CookingRecipeCatalog.CanMakeWith(r, eco.GetBoardMaterialCount))
                {
                    seen.Add(r.Id);
                    list.Add(r.Id);
                }
            }
            return list;
        }

        // ---------------- 저장 ----------------

        [Serializable]
        public class SpiritSave
        {
            public int id;
            public int kind;
            public string dishId;
            public long arrivedUtcTicks;
            public int slot;
        }

        [Serializable]
        public class PierSave
        {
            public SpiritSave[] spirits = Array.Empty<SpiritSave>();
            public float timerMinutes;
            public int nextId = 1;
            public long lastUtcTicks;
            public int memoryPieces;
        }

        public static PierSave CaptureToSave()
        {
            var arr = new SpiritSave[spirits.Count];
            for (int i = 0; i < spirits.Count; i++)
            {
                var s = spirits[i];
                arr[i] = new SpiritSave { id = s.Id, kind = (int)s.Kind, dishId = s.DishId, arrivedUtcTicks = s.ArrivedUtcTicks, slot = s.Slot };
            }
            return new PierSave
            {
                spirits = arr, timerMinutes = TimerMinutes, nextId = NextId,
                lastUtcTicks = LastUtcTicks, memoryPieces = MemoryPieces,
            };
        }

        /// <summary>세이브 복원. save 가 없으면(나루터 이전 세이브) 지금부터 시작.</summary>
        public static void ResetFromSave(PierSave save, DateTime utcNow)
        {
            spirits.Clear();
            if (save == null)
            {
                TimerMinutes = 0f; NextId = 1; LastUtcTicks = utcNow.Ticks; MemoryPieces = 0;
                Changed?.Invoke();
                return;
            }
            if (save.spirits != null)
                foreach (var s in save.spirits)
                    if (s != null && !string.IsNullOrEmpty(s.dishId))
                        spirits.Add(new Spirit
                        {
                            Id = s.id, Kind = (SpiritKind)Math.Max(0, Math.Min(3, s.kind)), DishId = s.dishId,
                            ArrivedUtcTicks = s.arrivedUtcTicks, Slot = s.slot,
                        });
            TimerMinutes = Math.Max(0f, save.timerMinutes);
            NextId = Math.Max(1, save.nextId);
            LastUtcTicks = save.lastUtcTicks > 0 ? save.lastUtcTicks : utcNow.Ticks;
            MemoryPieces = Math.Max(0, save.memoryPieces);
            Changed?.Invoke();
        }
    }
}
