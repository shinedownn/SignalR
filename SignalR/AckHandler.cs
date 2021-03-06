﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SignalR
{
    internal class AckHandler : IAckHandler, IDisposable
    {
        private readonly ConcurrentDictionary<string, AckInfo> _acks = new ConcurrentDictionary<string, AckInfo>();

        // REVIEW: Consider making this pluggable
        private readonly TimeSpan _ackThreshold;

        // REVIEW: Consider moving this logic to the transport heartbeat
        private Timer _timer;

        public AckHandler()
            : this(cancelAcksOnTimeout: true, 
                   ackThreshold: TimeSpan.FromMinutes(1),
                   ackInterval: TimeSpan.FromSeconds(10))
        {
        }

        public AckHandler(bool cancelAcksOnTimeout, TimeSpan ackThreshold, TimeSpan ackInterval)
        {
            if (cancelAcksOnTimeout)
            {
                _timer = new Timer(_ => CheckAcks(), state: null, dueTime: ackInterval, period: ackInterval);
            }

            _ackThreshold = ackThreshold;
        }

        public Task CreateAck(string id)
        {
            return _acks.GetOrAdd(id, _ => new AckInfo()).Tcs.Task;
        }

        public bool TriggerAck(string id)
        {
            AckInfo info;
            if (_acks.TryRemove(id, out info))
            {
                info.Tcs.TrySetResult(null);
                return true;
            }

            return false;
        }

        private void CheckAcks()
        {
            foreach (var pair in _acks)
            {
                TimeSpan elapsed = DateTime.UtcNow - pair.Value.Created;
                if (elapsed > _ackThreshold)
                {
                    AckInfo info;
                    if (_acks.TryRemove(pair.Key, out info))
                    {
                        // If we have a pending ack for longer than the threshold
                        // cancel it.
                        info.Tcs.TrySetCanceled();
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Dispose();
            }
        }

        private class AckInfo
        {
            public TaskCompletionSource<object> Tcs { get; private set; }
            public DateTime Created { get; private set; }

            public AckInfo()
            {
                Tcs = new TaskCompletionSource<object>();
                Created = DateTime.UtcNow;
            }
        }
    }
}
