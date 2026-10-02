namespace SwiftX.Services;

/// <summary>
/// Remembers the hosts a favicon could not be fetched from, and for how long not to ask again.
/// <para>
/// <b>What it replaces is "ask again on every summon, forever".</b> A failed resolution is
/// deliberately not cached on the item — the next render retries, so a favicon that missed while
/// the machine was offline appears on a later summon (see <c>OrbitWindow.ResolveIconAsync</c>) —
/// and that retry used to cost two HTTP requests per web link per summon: the site's own
/// <c>/favicon.ico</c>, then the DuckDuckGo icon service (which is also told the host name each
/// time). For a host with no favicon, a dev server that is not running, or a laptop on a plane,
/// that is hundreds of failed requests a day that can never succeed.
/// </para>
/// <para>
/// A back-off keeps the retry and drops the waste: the first miss is retried after 30 s, the next
/// after a minute, then two, five and — the ceiling — ten, so a connection that comes back is
/// noticed within ten minutes at worst and usually within seconds. A success forgets the host
/// entirely. Held in memory only: a restart is a fresh start, which is the right time to look again.
/// </para>
/// </summary>
internal sealed class FaviconMissCache
{
    /// <summary>The waits after the 1st, 2nd, 3rd... consecutive miss; the last one repeats.</summary>
    internal static readonly TimeSpan[] DefaultSchedule =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
    ];

    /// <summary>More distinct hosts than a person has web links; past it the oldest entry goes, so
    /// the cache cannot grow with every host an item has ever pointed at.</summary>
    internal const int MaxHosts = 256;

    private readonly record struct Miss(int Count, long RetryAfterMs);

    private readonly Dictionary<string, Miss> _misses = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Func<long> _nowMs;
    private readonly TimeSpan[] _schedule;

    public FaviconMissCache(Func<long>? nowMs = null, TimeSpan[]? schedule = null)
    {
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _schedule = schedule is { Length: > 0 } ? schedule : DefaultSchedule;
    }

    /// <summary>True while <paramref name="host"/> is inside its back-off window.</summary>
    public bool ShouldSkip(string host)
    {
        lock (_gate)
            return _misses.TryGetValue(host, out var miss) && _nowMs() < miss.RetryAfterMs;
    }

    /// <summary>Records a failed fetch and starts (or lengthens) the back-off for the host.</summary>
    public void RecordMiss(string host)
    {
        lock (_gate)
        {
            int count = _misses.TryGetValue(host, out var previous) ? previous.Count + 1 : 1;
            var wait = _schedule[Math.Min(count, _schedule.Length) - 1];
            _misses[host] = new Miss(count, _nowMs() + (long)wait.TotalMilliseconds);

            if (_misses.Count > MaxHosts)
            {
                var oldest = _misses.OrderBy(kv => kv.Value.RetryAfterMs).First().Key;
                _misses.Remove(oldest);
            }
        }
    }

    /// <summary>Forgets the host: it answered, so the next failure starts the schedule over.</summary>
    public void RecordSuccess(string host)
    {
        lock (_gate)
            _misses.Remove(host);
    }

    internal int Count
    {
        get { lock (_gate) return _misses.Count; }
    }
}
