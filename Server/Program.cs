using ServerCore;

namespace Server;

class Program
{
    static readonly ReadWriteLock _lock = new();
    static int _sharedValue = 0;
    static volatile bool _running = true;

    static void Main(string[] args)
    {
#if DEBUG
        TestDeadLockDetection();
        Console.WriteLine();
#endif

        Console.WriteLine("[ReadWriteLock Test] Starting...");
        Console.WriteLine();

        // Writer 스레드 2개: 값을 증가시킴
        for (int i = 0; i < 2; i++)
        {
            int writerId = i;
            ThreadManager.Launch(() =>
            {
                int writeCount = 0;
                while (_running)
                {
                    using var wl = _lock.WriteLockScope();
                    _sharedValue++;
                    writeCount++;
                    Thread.Sleep(1); // 쓰기 간격
                }
                Console.WriteLine($"  [Writer-{writerId}] ThreadId={ThreadManager.CurrentThreadId}, Writes={writeCount}");
            }, threadName: $"Writer-{writerId}");
        }

        // Reader 스레드 4개: 값을 읽음
        for (int i = 0; i < 4; i++)
        {
            int readerId = i;
            ThreadManager.Launch(() =>
            {
                int readCount = 0;
                while (_running)
                {
                    using var rl = _lock.ReadLockScope();
                    _ = _sharedValue; // 읽기
                    readCount++;
                }
                Console.WriteLine($"  [Reader-{readerId}] ThreadId={ThreadManager.CurrentThreadId}, Reads={readCount}");
            }, threadName: $"Reader-{readerId}");
        }

        // 3초간 실행 후 종료
        Console.WriteLine("[Test] 6개 스레드 실행 중 (Writer 2 + Reader 4)...");
        Console.WriteLine("[Test] 3초 후 자동 종료...");
        Console.WriteLine();
        Thread.Sleep(3000);
        _running = false;

        ThreadManager.JoinAll();

        Console.WriteLine();
        Console.WriteLine($"[Result] Final sharedValue = {_sharedValue}");
        Console.WriteLine("[ReadWriteLock Test] Complete!");
    }

    // ─── DeadLock Detection Test ───
#if DEBUG
    static void TestDeadLockDetection()
    {
        Console.WriteLine("[DeadLock Profiler Test] Starting...");

        // 데드락 테스트용 Lock 2개
        ReadWriteLock LockA = new();
        ReadWriteLock LockB = new();

        // Thread 1: A → B 순서로 획득 (간선 A→B 기록)
        ThreadManager.Launch(() =>
        {
            using var A = LockA.WriteLockScope();
            Thread.Sleep(100); // Thread 2가 LockB를 잡을 시간 확보
            using var B = LockB.WriteLockScope();
        }, threadName: "DeadLock-Thread1");

        Thread.Sleep(50); // Thread 1이 먼저 LockA를 잡도록

        // Thread 2: B → A 순서로 획득 (간선 B→A 추가 → 사이클 A→B→A 탐지)
        ThreadManager.Launch(() =>
        {
            using var B = LockB.WriteLockScope();
            Thread.Sleep(100);
            using var A = LockA.WriteLockScope(); // 여기서 DeadLock Assert 발생!
        }, threadName: "DeadLock-Thread2");

        ThreadManager.JoinAll();
        Console.WriteLine("[DeadLock Profiler Test] Complete!");
    }
#endif
}