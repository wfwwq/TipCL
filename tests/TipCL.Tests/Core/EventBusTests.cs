using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>EventBus 订阅/发布队列单测（M1-A5）。</summary>
public class EventBusTests
{
    [Fact]
    public void Subscribe_IncreasesSubscriberCount()
    {
        var bus = new EventBus();
        Assert.Equal(0, bus.SubscriberCount);
        var q = bus.Subscribe();
        Assert.Equal(1, bus.SubscriberCount);
        bus.Unsubscribe(q);
        Assert.Equal(0, bus.SubscriberCount);
    }

    [Fact]
    public void Emit_DeliversToAllSubscribers()
    {
        var bus = new EventBus();
        var q1 = bus.Subscribe();
        var q2 = bus.Subscribe();
        bus.Emit("progress", "hello");
        Assert.True(q1.TryTake(out var item1, 1000));
        Assert.True(q2.TryTake(out var item2, 1000));
        Assert.Equal("progress", item1["event"]);
        Assert.Equal("hello", item1["data"]);
        Assert.Equal(item2["event"], item1["event"]);
    }

    [Fact]
    public void Emit_WithNoSubscribers_IsSafe()
    {
        var bus = new EventBus();
        bus.Emit("orphan", 123);  // 不应抛异常
    }

    [Fact]
    public void Queue_BoundedAt800_EmitDropsWhenFull()
    {
        var bus = new EventBus();
        var q = bus.Subscribe();
        for (int i = 0; i < EventBus.QueueMax + 10; i++)
        {
            bus.Emit("e", i);
        }
        // 队列上限 800，多发的 10 条被丢弃而非阻塞
        Assert.Equal(EventBus.QueueMax, q.Count);
    }

    [Fact]
    public void Concurrent_Emit_IsThreadSafe()
    {
        var bus = new EventBus();
        var q = bus.Subscribe();
        Parallel.For(0, 100, i => bus.Emit("e", i));
        Assert.Equal(100, q.Count);
    }
}
