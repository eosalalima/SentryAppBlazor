using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SentryAppBlazor.Models;

namespace SentryAppBlazor.Services;

public sealed class MonitoringState(IOptions<MonitoringOptions> options, ILogger<MonitoringState> logger) : BackgroundService
{
    private readonly ConcurrentQueue<AccessEvent> queue = new();
    private readonly List<(AccessEvent Event, DateTimeOffset Expires)> recent = [];
    private readonly HashSet<long> processed = [];
    private readonly object gate = new();
    private long nextId;
    private volatile bool requested;

    public event Action? Changed;
    public AccessEvent? Spotlight { get; private set; }
    public string Status { get; private set; } = "Halted";
    public bool IsRunning => requested;
    public IReadOnlyList<AccessEvent> Recent { get { lock (gate) return recent.Select(x => x.Event).Reverse().ToArray(); } }

    public void Start()
    {
        if (requested) return;
        requested = true;
        Status = options.Value.OperatingMode.Equals("Demo", StringComparison.OrdinalIgnoreCase)
            ? "Demo Mode Active" : "Live Mode Unavailable — schema mapping required";
        logger.LogInformation("Monitoring requested in {OperatingMode}", options.Value.OperatingMode);
        Notify();
    }

    public void Stop()
    {
        requested = false;
        Status = "Halted";
        logger.LogInformation("Monitoring halted");
        Notify();
    }

    public bool Enqueue(AccessEvent item)
    {
        lock (gate)
        {
            if (!processed.Add(item.UniqueLogId)) return false;
            if (processed.Count > 2_000) processed.Remove(processed.Min());
            queue.Enqueue(item);
        }
        Notify();
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastGenerated = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (requested && options.Value.OperatingMode.Equals("Demo", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.UtcNow - lastGenerated > TimeSpan.FromSeconds(7))
            {
                lastGenerated = DateTimeOffset.UtcNow;
                Enqueue(CreateDemoEvent());
            }

            if (Spotlight is null && queue.TryDequeue(out var next))
            {
                Spotlight = next;
                Notify();
                await Task.Delay(options.Value.HighlightDisplayDuration, stoppingToken);
                lock (gate)
                    recent.Add((next, DateTimeOffset.UtcNow.AddMilliseconds(options.Value.FeedRetentionDuration)));
                Spotlight = null;
                Notify();
            }

            lock (gate) recent.RemoveAll(x => x.Expires <= DateTimeOffset.UtcNow);
            await Task.Delay(100, stoppingToken);
        }
    }

    private AccessEvent CreateDemoEvent()
    {
        var id = Interlocked.Increment(ref nextId);
        string[] names = ["Maria Santos", "Daniel Reyes", "Angela Cruz", "Noel Garcia"];
        var direction = id % 3 == 0 ? AccessDirection.Exit : AccessDirection.Entry;
        return new(id, DateTimeOffset.Now, $"demo-{id}", $"2026-{id:0000}", names[(int)((id - 1) % names.Length)],
            direction == AccessDirection.Entry ? "gate-in" : "gate-out",
            direction == AccessDirection.Entry ? "Main Gate — Entry" : "Main Gate — Exit", direction);
    }

    private void Notify() => Changed?.Invoke();
}
