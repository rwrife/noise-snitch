using System;
using System.Collections.Generic;
using NoiseSnitch.Model;

namespace NoiseSnitch.AudioWatcher;

/// <summary>
/// A fixed-capacity, in-memory ring buffer of the most recent
/// <see cref="NoiseEvent"/>s. When full, adding a new event evicts the oldest.
///
/// This is the session-lifetime history the blotter (M4) reads; persistence
/// across restarts is deliberately out of scope until M6. Access is synchronized
/// because events are written from the watcher's timer thread while the UI may
/// read the snapshot concurrently.
/// </summary>
internal sealed class EventStore
{
    /// <summary>Default number of recent events retained.</summary>
    public const int DefaultCapacity = 200;

    private readonly object _gate = new();
    private readonly Queue<NoiseEvent> _events;
    private readonly int _capacity;

    public EventStore(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity), capacity, "Capacity must be positive.");
        }

        _capacity = capacity;
        _events = new Queue<NoiseEvent>(capacity);
    }

    /// <summary>Maximum number of events retained before the oldest is evicted.</summary>
    public int Capacity => _capacity;

    /// <summary>Current number of retained events.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    /// <summary>
    /// Appends an event, evicting the oldest if at capacity. Raised on whatever
    /// thread calls this (typically the watcher timer thread).
    /// </summary>
    public void Add(NoiseEvent e)
    {
        lock (_gate)
        {
            if (_events.Count >= _capacity)
            {
                _events.Dequeue();
            }

            _events.Enqueue(e);
        }

        Added?.Invoke(this, e);
    }

    /// <summary>
    /// Returns the retained events, <b>newest first</b> — the order the blotter
    /// renders. The result is a copy and safe to enumerate without holding a lock.
    /// </summary>
    public IReadOnlyList<NoiseEvent> Recent()
    {
        lock (_gate)
        {
            var arr = new NoiseEvent[_events.Count];
            _events.CopyTo(arr, 0); // oldest -> newest
            Array.Reverse(arr);     // newest -> oldest
            return arr;
        }
    }

    /// <summary>Removes all retained events.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }

    /// <summary>
    /// Raised after each event is added. The blotter/tray (M4/M5) subscribe to
    /// refresh the list and flash the icon.
    /// </summary>
    public event EventHandler<NoiseEvent>? Added;
}
