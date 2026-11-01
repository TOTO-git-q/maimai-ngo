using System;
using System.Collections.Generic;
using System.Threading;
using PartnerLive2D.Unity;

namespace PartnerLive2D.Unity
{
    // Logging double only; the production dispatcher is compiled directly into Headless.
    static class Mod
    {
        internal static readonly List<string> Errors = new List<string>();
        public static void Error(string message) { Errors.Add(message); }
    }
}

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        static void MainThreadChecks()
        {
            int checks = 0;
            Action<bool, string> check = (ok, why) => { if (!ok) throw new Exception(why); checks++; };
            Action<Action> timeout = action =>
            {
                bool threw = false;
                try { action(); } catch (TimeoutException) { threw = true; }
                check(threw, "dispatcher must retain timeout behavior");
            };
            Action<Action> pump = action =>
            {
                Exception error = null;
                var worker = new Thread(() => { try { action(); } catch (Exception e) { error = e; } }) { IsBackground = true };
                worker.Start();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (worker.IsAlive && DateTime.UtcNow < deadline) { MainThread.Drain(); Thread.Sleep(1); }
                if (!worker.Join(1000)) throw new Exception("dispatcher test worker did not finish");
                MainThread.Drain();
                if (error != null) throw error;
            };

            Mod.Errors.Clear();
            int runs = 0;
            timeout(() => MainThread.Call(() => ++runs, 0));
            MainThread.Drain();
            check(runs == 1 && Mod.Errors.Count == 0, "late sync work still runs without signaling a disposed event");
            timeout(() => MainThread.Call<int>(() => { throw new InvalidOperationException("late"); }, 0));
            MainThread.Drain();
            check(Mod.Errors.Count == 0, "late sync failure must not escape Drain");

            Action<int> complete = null;
            timeout(() => MainThread.CallAsync<int>(done => complete = done, 0));
            MainThread.Drain();
            complete(42);
            complete(43);
            check(Mod.Errors.Count == 0, "late async callbacks must not signal a disposed event");
            timeout(() => MainThread.CallAsync<int>(done => { throw new InvalidOperationException("late async"); }, 0));
            MainThread.Drain();
            check(Mod.Errors.Count == 0, "late async startup failure must not escape Drain");

            pump(() => check(MainThread.Call(() => 42) == 42, "sync result"));
            var expected = new InvalidOperationException("expected");
            pump(() =>
            {
                Exception actual = null;
                try { MainThread.Call<int>(() => { throw expected; }); } catch (Exception e) { actual = e; }
                check(ReferenceEquals(expected, actual), "sync exception propagation");
            });
            pump(() => check(MainThread.CallAsync<int>(done => MainThread.Post(() => done(42))) == 42, "async result"));
            pump(() =>
            {
                Exception actual = null;
                try { MainThread.CallAsync<int>(done => { throw expected; }); } catch (Exception e) { actual = e; }
                check(ReferenceEquals(expected, actual), "async exception propagation");
            });
            // Exercise completion vs timeout/disposal on different threads, with bounded waits.
            for (int i = 0; i < 100; i++)
                pump(() => { try { MainThread.CallAsync<int>(done => done(1), 1); } catch (TimeoutException) { } });
            check(Mod.Errors.Count == 0, "completion/timeout race must not log dispatcher failures");
            Console.WriteLine("Main-thread lifetime checks passed: " + checks + "; 100 completion/timeout race iterations");
        }
    }
}
