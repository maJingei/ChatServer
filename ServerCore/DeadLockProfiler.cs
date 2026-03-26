namespace ServerCore;

#if DEBUG
public static class DeadLockProfiler
{
    // ─── Thread-Local Storage ───

    /// <summary> 현재 스레드가 보유 중인 Lock ID 목록 </summary>
    [ThreadStatic]
    private static List<int>? t_LockStack;

    // ─── Global Lock-Order Graph ───

    /// <summary> Lock 순서 방향 그래프 (인접 리스트) </summary>
    private static readonly Dictionary<int, HashSet<int>> _Graph = new();

    /// <summary> 그래프 접근 동기화용 모니터 객체 </summary>
    private static readonly object _GraphLock = new();

    // ─── Public API ───

    /// <summary> Lock 획득 전 호출 — 간선 추가 및 사이클 검사 </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PushLock(int LockId)
    {
        t_LockStack ??= new List<int>(); // 스레드 최초 호출 시 초기화

        lock (_GraphLock)
        {
            bool NewEdgeAdded = false; // 새 간선 추가 여부 (DFS 스킵 최적화)

            foreach (int HeldLockId in t_LockStack) // 보유 Lock → 새 Lock 간선 추가
            {
                if (!_Graph.TryGetValue(HeldLockId, out HashSet<int>? Neighbors))
                {
                    Neighbors = new HashSet<int>();
                    _Graph[HeldLockId] = Neighbors;
                }

                if (Neighbors.Add(LockId)) NewEdgeAdded = true; // 중복이면 false
            }

            if (NewEdgeAdded) CheckCycle(LockId); // 새 간선일 때만 사이클 검사
        }

        t_LockStack.Add(LockId); // 검사 통과 후 스택에 추가
    }

    /// <summary> Lock 해제 후 호출 — 스택에서 제거 </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PopLock(int LockId)
    {
        Debug.Assert(t_LockStack != null && t_LockStack.Contains(LockId), $"PopLock: lock {LockId} not in stack.");

        for (int I = t_LockStack!.Count - 1; I >= 0; I--) // 뒤에서부터 탐색 (비-LIFO 지원)
        {
            if (t_LockStack[I] == LockId) { t_LockStack.RemoveAt(I); break; }
        }
    }

    // ─── Cycle Detection (caller holds _GraphLock) ───

    /// <summary> DFS로 그래프 사이클 검사 — 역방향 간선 발견 시 Assert </summary>
    private static void CheckCycle(int StartNode)
    {
        Dictionary<int, int> Color = new(); // 노드별 DFS 색상 (WHITE=0, GRAY=1, BLACK=2)
        List<int> Path = new(); // DFS 탐색 경로 (사이클 출력용)

        if (!DFS(StartNode, Color, Path)) return; // 사이클 없으면 종료

        // 사이클 경로 추출: Path 마지막이 사이클 시작점
        int CycleStart = Path[^1];
        int CycleStartIdx = 0;
        for (int I = 0; I < Path.Count - 1; I++) { if (Path[I] == CycleStart) { CycleStartIdx = I; break; } }

        string CyclePath = string.Join(" → ", Path.Skip(CycleStartIdx).Select(Id => $"Lock({Id})"));
        Debug.Assert(false, $"[DeadLockProfiler] Deadlock detected! Cycle: {CyclePath}");
        throw new InvalidOperationException($"[DeadLockProfiler] Deadlock detected! Cycle: {CyclePath}");
    }

    // ─── DFS (WHITE=0 미방문, GRAY=1 탐색중, BLACK=2 탐색 완료) ───

    /// <summary> 재귀 DFS — GRAY 노드 재방문 시 역방향 간선(사이클) </summary>
    private static bool DFS(int Node, Dictionary<int, int> Color, List<int> Path)
    {
        const int GRAY = 1;
        const int BLACK = 2;

        Color.TryGetValue(Node, out int NodeColor); // 기본값 WHITE=0

        if (NodeColor == GRAY) { Path.Add(Node); return true; }   // 역방향 간선 → 사이클!
        if (NodeColor == BLACK) return false;                      // 순방향/교차 간선 → OK

        Color[Node] = GRAY;  // 탐색 중으로 마킹
        Path.Add(Node);

        if (_Graph.TryGetValue(Node, out HashSet<int>? Neighbors)) // 인접 노드 재귀 탐색
        {
            foreach (int Neighbor in Neighbors)
            {
                if (DFS(Neighbor, Color, Path)) return true;
            }
        }

        Color[Node] = BLACK; // 탐색 완료로 마킹
        Path.RemoveAt(Path.Count - 1);
        return false;
    }
}
#endif