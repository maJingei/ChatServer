namespace ServerCore;

public sealed class ReadWriteLock
{
    // ─── Constants ───
    private const int EMPTY_FLAG     = 0;
    private const int WRITE_MASK     = unchecked((int)0xFFFF_0000);
    private const int READ_MASK      = 0x0000_FFFF;
    private const int MAX_SPIN_COUNT = 5000;
    private const long TIMEOUT_MS    = 10_000;

    // ─── Lock Identity (DEBUG) ───
#if DEBUG
    /// <summary> Lock ID 자동 증가 카운터 </summary>
    private static int _nextLockId;

    /// <summary> 이 인스턴스의 고유 Lock ID </summary>
    private readonly int _lockId = Interlocked.Increment(ref _nextLockId);
#endif

    // ─── State ───
    private volatile int _lockFlag;

    // ─── Write Lock ───
    public void WriteLock()
    {
        int threadId = ThreadManager.CurrentThreadId;
        Debug.Assert(threadId > 0 && threadId <= 0xFFFF);

#if DEBUG
        DeadLockProfiler.PushLock(_lockId); // 블로킹 전에 데드락 패턴 검사
#endif

        int desired = threadId << 16;
        long startTick = Environment.TickCount64;
        int spinCount = 0;

        while (true)
        {
            if (Interlocked.CompareExchange(ref _lockFlag, desired, EMPTY_FLAG) == EMPTY_FLAG)
                return;

            spinCount++;
            if (spinCount > MAX_SPIN_COUNT)
                Thread.Yield();

            if (Environment.TickCount64 - startTick > TIMEOUT_MS)
            {
#if DEBUG
                DeadLockProfiler.PopLock(_lockId); // Timeout 시 Push/Pop 균형 유지
#endif
                throw new TimeoutException($"WriteLock timeout. ThreadId={threadId}, Flag=0x{_lockFlag:X8}");
            }
        }
    }

    public void WriteUnlock()
    {
        Debug.Assert((_lockFlag & WRITE_MASK) == (ThreadManager.CurrentThreadId << 16));
        Interlocked.Exchange(ref _lockFlag, EMPTY_FLAG);

#if DEBUG
        DeadLockProfiler.PopLock(_lockId); // 해제 후 스택에서 제거
#endif
    }

    // ─── Read Lock ───
    public void ReadLock()
    {
        int threadId = ThreadManager.CurrentThreadId;
        Debug.Assert(threadId > 0);

#if DEBUG
        DeadLockProfiler.PushLock(_lockId); // 블로킹 전에 데드락 패턴 검사
#endif

        long startTick = Environment.TickCount64;
        int spinCount = 0;

        while (true)
        {
            int current = _lockFlag;
            if ((current & WRITE_MASK) == 0)
            {
                int desired = current + 1;
                if (Interlocked.CompareExchange(ref _lockFlag, desired, current) == current)
                    return;
            }

            spinCount++;
            if (spinCount > MAX_SPIN_COUNT)
                Thread.Yield();

            if (Environment.TickCount64 - startTick > TIMEOUT_MS)
            {
#if DEBUG
                DeadLockProfiler.PopLock(_lockId); // Timeout 시 Push/Pop 균형 유지
#endif
                throw new TimeoutException($"ReadLock timeout. ThreadId={threadId}, Flag=0x{_lockFlag:X8}");
            }
        }
    }

    public void ReadUnlock()
    {
        while (true)
        {
            int current = _lockFlag;
            Debug.Assert((current & READ_MASK) > 0);

            int desired = current - 1;
            if (Interlocked.CompareExchange(ref _lockFlag, desired, current) == current)
            {
#if DEBUG
                DeadLockProfiler.PopLock(_lockId); // CAS 성공 후 스택에서 제거
#endif
                return;
            }
        }
    }

    // ─── RAII Scope Guards ───
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WriteLockGuard WriteLockScope() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadLockGuard ReadLockScope() => new(this);

    // ─── Guard Structs ───
    public ref struct WriteLockGuard
    {
        private readonly ReadWriteLock _lock;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal WriteLockGuard(ReadWriteLock rwLock)
        {
            _lock = rwLock;
            _lock.WriteLock();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => _lock.WriteUnlock();
    }

    public ref struct ReadLockGuard
    {
        private readonly ReadWriteLock _lock;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadLockGuard(ReadWriteLock rwLock)
        {
            _lock = rwLock;
            _lock.ReadLock();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => _lock.ReadUnlock();
    }
}
