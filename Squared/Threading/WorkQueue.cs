﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Threading {
    public interface IWorkQueue {
        /// <param name="exhausted">Is set to true if the Step operation caused the queue to become empty.</param>
        /// <returns>The number of work items handled.</returns>
        int Step (out bool exhausted, int? maximumCount = null);
    }

    public interface IWorkItem {
        void Execute ();
    }

    public delegate void OnWorkItemComplete<T> (ref T item)
        where T : IWorkItem;

    internal struct InternalWorkItem<T>
        where T : IWorkItem
    {
        public readonly WorkQueue<T>          Queue;
        public readonly OnWorkItemComplete<T> OnComplete;
        public          T                     Data;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal InternalWorkItem (WorkQueue<T> queue, ref T data, OnWorkItemComplete<T> onComplete) {
            Queue = queue;
            Data = data;
            OnComplete = onComplete;
        }
        
        // TODO: Add cheap blocking wait primitive
    }

    public class WorkQueue<T> : IWorkQueue
        where T : IWorkItem 
    {
        public struct Marker {
            private readonly WorkQueue<T> Queue;
            public readonly long Executed, Enqueued;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Marker (WorkQueue<T> queue) {
                Queue = queue;
                Executed = Interlocked.Read(ref Queue.ItemsExecuted);
                Enqueued = Interlocked.Read(ref Queue.ItemsEnqueued);
            }

            /// <summary>
            /// Waits until all items enqueued at the marking point have been executed
            /// </summary>
            public void Wait () {
                while (true) {
                    lock (Queue.Token) {
                        var executed = Interlocked.Read(ref Queue.ItemsExecuted);
                        if (executed >= Enqueued)
                            return;

                        Monitor.Wait(Queue.Token);
                    }
                }
            }
        }

        /// <summary>
        /// Configures the number of steps taken each time this queue is visited by a worker thread.
        /// Low values increase the overhead of individual work items.
        /// High values reduce the overhead of work items but increase the odds that all worker threads can get bogged down
        ///  by a single queue.
        /// </summary>
        public int DefaultStepCount = 128;

        private readonly object Token = new object();

        private readonly Queue<InternalWorkItem<T>> Queue = new Queue<InternalWorkItem<T>>();

        private long ItemsEnqueued;
        private long ItemsExecuted;

        public WorkQueue () {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (T data, OnWorkItemComplete<T> onComplete = null) {
            Interlocked.Increment(ref ItemsEnqueued);
            lock (Queue)
                Queue.Enqueue(new InternalWorkItem<T>(this, ref data, onComplete));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (ref T data, OnWorkItemComplete<T> onComplete = null) {
            Interlocked.Increment(ref ItemsEnqueued);
            lock (Queue)
                Queue.Enqueue(new InternalWorkItem<T>(this, ref data, onComplete));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueMany (ArraySegment<T> data) {
            Interlocked.Add(ref ItemsEnqueued, data.Count);
            lock (Queue) {
                for (var i = 0; i < data.Count; i++)
                    Queue.Enqueue(new InternalWorkItem<T>(this, ref data.Array[data.Offset + i], null));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Marker Mark () {
            return new Marker(this);
        }

        public int Step (out bool exhausted, int? maximumCount = null) {
            int result = 0, count = 0;
            int actualMaximumCount = maximumCount.GetValueOrDefault(DefaultStepCount);

            lock (Queue)
            while (
                ((count = Queue.Count) > 0) &&
                (result < actualMaximumCount)
            ) {
                var item = Queue.Dequeue();

                Monitor.Exit(Queue);
                try {
                    item.Data.Execute();
                    if (item.OnComplete != null)
                        item.OnComplete(ref item.Data);
                } finally {
                    Monitor.Enter(Queue);
                }

                result++;
            }

            if (result > 0)
                Interlocked.Add(ref ItemsExecuted, result);

            lock (Token)
                Monitor.PulseAll(Token);

            exhausted = (result > 0) && (count <= 0);

            return result;
        }

        public void WaitUntilDrained () {
            var done = false;

            while (!done) {
                int count;
                lock (Token) {
                    lock (Queue)
                        count = Queue.Count;

                    done = 
                        (Interlocked.Read(ref ItemsExecuted) >= Interlocked.Read(ref ItemsEnqueued)) &&
                        (count == 0);

                    if (!done)                        
                        Monitor.Wait(Token);
                }
            }
        }
    }
}
