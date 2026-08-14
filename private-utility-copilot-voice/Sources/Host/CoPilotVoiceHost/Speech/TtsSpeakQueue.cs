namespace CoPilotVoiceHost.Speech;

/// <summary>
/// Dedicated speaker thread so TTS delay/play never sleeps on the SAPI callback or 50 ms poll timer.
/// CommandProcessor still calls Speak before actions; only the audio wait is offloaded.
/// </summary>
internal static class TtsSpeakQueue
{
    private static readonly object Gate = new();
    private static readonly Queue<Action> Queue = new();
    private static readonly AutoResetEvent Signal = new(false);
    private static Thread? _thread;
    private static volatile bool _run = true;

    public static void Enqueue(Action work)
    {
        if (work is null) return;
        lock (Gate)
        {
            EnsureThreadUnlocked();
            Queue.Enqueue(work);
        }

        Signal.Set();
    }

    public static void EnqueueDelayed(int delayMs, Action work)
    {
        var ms = Math.Clamp(delayMs, 0, 5_000);
        if (ms <= 0)
        {
            Enqueue(work);
            return;
        }

        Enqueue(() =>
        {
            try
            {
                if (ms > 0)
                    Thread.Sleep(ms);
                work();
            }
            catch
            {
                // speaker thread must not die
            }
        });
    }

    private static void EnsureThreadUnlocked()
    {
        if (_thread is { IsAlive: true })
            return;
        _run = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "PrivateCoPilot-TTS"
        };
        _thread.Start();
    }

    private static void Loop()
    {
        while (_run)
        {
            Action? work = null;
            lock (Gate)
            {
                if (Queue.Count > 0)
                    work = Queue.Dequeue();
            }

            if (work is null)
            {
                Signal.WaitOne(250);
                continue;
            }

            try { work(); }
            catch { /* keep speaker alive */ }
        }
    }
}
