namespace ServerCore;

public static class ThreadManager
{
    // ─── Thread-Local Storage ───
    [ThreadStatic]
    private static int t_threadId;
    // 0 = 미초기화, 유효 ID는 1부터

    // ─── Shared State ───
    private static int _nextId;
    private static readonly ConcurrentBag<Thread> _threads = new();

    // ─── Public Accessor ───
    public static int CurrentThreadId => t_threadId;

    // ─── Core Methods ───
    private static void InitTLS()
    {
        t_threadId = Interlocked.Increment(ref _nextId);
    }

    private static void DestroyTLS()
    {
        t_threadId = 0;
    }

    public static void Launch(Action callback, string? threadName = null)
    {
        var thread = new Thread(() =>
        {
            InitTLS();
            try
            {
                callback();
            }
            finally
            {
                DestroyTLS();
            }
        });

        if (threadName is not null)
            thread.Name = threadName;

        thread.IsBackground = false;
        thread.Start();
        _threads.Add(thread);
    }

    public static void JoinAll()
    {
        foreach (var thread in _threads)
        {
            if (thread.IsAlive)
                thread.Join();
        }
    }
}