using System;
using System.Collections.Generic;
using System.Linq;
using NoiseSnitch.AudioWatcher;
using NoiseSnitch.Model;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>Ring-buffer semantics of <see cref="EventStore"/>.</summary>
public sealed class EventStoreTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static NoiseEvent Event(int i) =>
        new(T0.AddSeconds(i), (uint)i, $"proc{i}", 0.5f, $"sess{i}");

    [Fact]
    public void Recent_Is_Newest_First()
    {
        var store = new EventStore(capacity: 10);
        store.Add(Event(1));
        store.Add(Event(2));
        store.Add(Event(3));

        var recent = store.Recent();
        Assert.Equal(new[] { "proc3", "proc2", "proc1" },
            recent.Select(e => e.ProcessName).ToArray());
    }

    [Fact]
    public void Evicts_Oldest_When_Over_Capacity()
    {
        var store = new EventStore(capacity: 3);
        for (int i = 1; i <= 5; i++)
        {
            store.Add(Event(i));
        }

        Assert.Equal(3, store.Count);
        // Newest-first: 5, 4, 3 retained; 1 and 2 evicted.
        Assert.Equal(new[] { "proc5", "proc4", "proc3" },
            store.Recent().Select(e => e.ProcessName).ToArray());
    }

    [Fact]
    public void Count_Tracks_Adds_And_Clear()
    {
        var store = new EventStore(capacity: 8);
        Assert.Equal(0, store.Count);

        store.Add(Event(1));
        store.Add(Event(2));
        Assert.Equal(2, store.Count);

        store.Clear();
        Assert.Equal(0, store.Count);
        Assert.Empty(store.Recent());
    }

    [Fact]
    public void Added_Event_Fires_Per_Add()
    {
        var store = new EventStore(capacity: 8);
        var seen = new List<NoiseEvent>();
        store.Added += (_, e) => seen.Add(e);

        store.Add(Event(1));
        store.Add(Event(2));

        Assert.Equal(2, seen.Count);
        Assert.Equal("proc1", seen[0].ProcessName);
        Assert.Equal("proc2", seen[1].ProcessName);
    }

    [Fact]
    public void Capacity_Must_Be_Positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventStore(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventStore(-1));
    }

    [Fact]
    public void Recent_Returns_Independent_Copy()
    {
        var store = new EventStore(capacity: 4);
        store.Add(Event(1));

        var snap1 = store.Recent();
        store.Add(Event(2));
        var snap2 = store.Recent();

        Assert.Single(snap1); // earlier copy unaffected by later Add
        Assert.Equal(2, snap2.Count);
    }
}
