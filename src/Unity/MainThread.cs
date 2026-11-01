using System;
using System.Collections.Generic;
using System.Threading;

namespace PartnerLive2D.Unity
{
    // Work handed from the HTTP thread to Unity's main thread (drained in Mod.OnUpdate).
    static class MainThread
    {
        static readonly Queue<Action> Pending = new Queue<Action>();

        // The waiter may time out before queued work or an async callback completes.
        // Serialize signaling with disposal; late completion must not touch a closed handle.
        sealed class Completion : IDisposable
        {
            readonly ManualResetEvent _event = new ManualResetEvent(false);
            readonly object _gate = new object();
            bool _disposed;

            public bool Wait(int timeoutMs) { return _event.WaitOne(timeoutMs); }
            public void Set()
            {
                lock (_gate) { if (!_disposed) _event.Set(); }
            }
            public void Dispose()
            {
                lock (_gate)
                {
                    if (_disposed) return;
                    _disposed = true;
                    _event.Close();
                }
            }
        }

        public static void Post(Action a) { lock (Pending) Pending.Enqueue(a); }

        public static void Drain()
        {
            while (true)
            {
                Action a;
                lock (Pending) { if (Pending.Count == 0) return; a = Pending.Dequeue(); }
                try { a(); } catch (Exception e) { Mod.Error("main-thread task: " + e); }
            }
        }

        // run f on the main thread and wait for its result
        public static T Call<T>(Func<T> f, int timeoutMs = 5000)
        {
            T result = default(T);
            Exception error = null;
            using (var done = new Completion())
            {
                Post(() => { try { result = f(); } catch (Exception e) { error = e; } finally { done.Set(); } });
                if (!done.Wait(timeoutMs)) throw new TimeoutException("game main thread did not respond (paused or loading?)");
            }
            if (error != null) throw error;
            return result;
        }

        // for work that must wait for end of frame (screenshots): f gets a completion callback
        public static T CallAsync<T>(Action<Action<T>> f, int timeoutMs = 5000)
        {
            T result = default(T);
            Exception error = null;
            using (var done = new Completion())
            {
                Post(() => { try { f(r => { result = r; done.Set(); }); } catch (Exception e) { error = e; done.Set(); } });
                if (!done.Wait(timeoutMs)) throw new TimeoutException("game main thread did not respond");
            }
            if (error != null) throw error;
            return result;
        }
    }

}
