using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Pakon.LegacyBridge
{
    /// <summary>Keeps every TLX COM call on one STA while the pipe remains responsive.</summary>
    internal sealed class StaComWorker
    {
        private readonly BlockingCollection<Action> actions = new BlockingCollection<Action>();

        public StaComWorker()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "Pakon TLX STA worker" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public void Enqueue(Action action) { actions.Add(action); }

        public T Invoke<T>(Func<T> function)
        {
            T result = default(T);
            Exception failure = null;
            using (var completed = new ManualResetEvent(false))
            {
                Enqueue(() =>
                {
                    try { result = function(); }
                    catch (Exception exception) { failure = exception; }
                    finally { completed.Set(); }
                });
                completed.WaitOne();
            }
            if (failure != null) throw failure;
            return result;
        }

        private void Run()
        {
            foreach (var action in actions.GetConsumingEnumerable())
            {
                try { action(); }
                catch (Exception exception) { Console.Error.WriteLine("TLX STA worker command failed: " + exception); }
            }
        }
    }
}
