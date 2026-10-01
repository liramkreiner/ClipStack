using System.Text;

namespace ClipStack.Core;

/// <summary>
/// Owns the clipboard history: the in-memory list (mutated on the UI thread only) and its
/// persistence. Database writes run on a serial background queue so the UI never waits on disk.
/// </summary>
public sealed class HistoryService : IDisposable
{
    private readonly HistoryStore _store;
    private readonly Protector _protector;
    private readonly Func<AppSettings> _settings;
    private readonly List<ClipItem> _items;
    private readonly object _storeLock = new();
    private readonly object _queueLock = new();
    private Task _tail = Task.CompletedTask;

    public event Action? Changed;

    public HistoryService(HistoryStore store, Protector protector, Func<AppSettings> settings)
    {
        _store = store;
        _protector = protector;
        _settings = settings;
        lock (_storeLock) _items = store.LoadAll();
    }

    public IReadOnlyList<ClipItem> Items => _items;
    public string DatabasePath => _store.FilePath;

    /// <summary>Pinned items (in pin order) followed by the rest, most recent first.</summary>
    public List<ClipItem> Ordered()
    {
        var pinned = _items.Where(i => i.IsPinned).OrderBy(i => i.PinnedAt ?? i.CreatedAt);
        var recent = _items.Where(i => !i.IsPinned).OrderByDescending(i => i.UpdatedAt);
        return pinned.Concat(recent).ToList();
    }

    public string ComputeHash(CapturedClip c) => c.Kind switch
    {
        ClipKind.Text => _protector.Hash(c.Kind, Utf8(c.Text)),
        ClipKind.Files => _protector.Hash(c.Kind, Utf8(string.Join("\n", c.Files ?? []).ToLowerInvariant())),
        ClipKind.Image => _protector.Hash(c.Kind, c.ImageIdentity ?? c.ImagePng),
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    /// <summary>Add a captured clip (or bump its duplicate to the top). Returns the affected item.</summary>
    public ClipItem Add(CapturedClip c, string hash)
    {
        var s = _settings();
        var now = DateTime.Now;

        if (!s.AllowDuplicates)
        {
            var existing = _items.FirstOrDefault(i => i.Hash == hash);
            if (existing is not null)
            {
                existing.UpdatedAt = now;
                existing.CopyCount++;
                Persist(st => st.UpdateMeta(existing));
                Changed?.Invoke();
                return existing;
            }
        }

        var item = new ClipItem
        {
            Kind = c.Kind,
            Text = c.Text,
            Html = c.Html,
            Rtf = c.Rtf,
            Files = c.Files,
            ImageWidth = c.ImageWidth,
            ImageHeight = c.ImageHeight,
            ThumbPng = c.ThumbPng,
            CreatedAt = now,
            UpdatedAt = now,
            SourceApp = c.SourceApp,
            SourcePath = c.SourcePath,
            Hash = hash,
        };
        _items.Add(item);
        bool encrypt = s.EncryptHistory;
        Persist(st => item.Id = st.Insert(item, c.ImagePng, c.ThumbPng, encrypt));
        EnforceLimits(raiseChanged: false);
        Changed?.Invoke();
        return item;
    }

    /// <summary>Record that an item was pasted/copied from the history: it moves to the top.</summary>
    public void MarkUsed(ClipItem item)
    {
        var now = DateTime.Now;
        item.UpdatedAt = now;
        item.LastUsedAt = now;
        if (_settings().TrackUsage) item.UseCount++;
        Persist(st => st.UpdateMeta(item));
        Changed?.Invoke();
    }

    public void TogglePin(ClipItem item)
    {
        item.IsPinned = !item.IsPinned;
        item.PinnedAt = item.IsPinned ? DateTime.Now : null;
        Persist(st => st.UpdateMeta(item));
        Changed?.Invoke();
    }

    public void Delete(ClipItem item) => Remove([item]);

    /// <summary>
    /// "Similar" = links to the same website, or the same content ignoring case and whitespace.
    /// Pinned items are kept unless it's the item itself.
    /// </summary>
    public int DeleteSimilar(ClipItem item)
    {
        var victims = _items.Where(i => i == item || (!i.IsPinned && AreSimilar(item, i))).ToList();
        Remove(victims);
        return victims.Count;
    }

    internal static bool AreSimilar(ClipItem a, ClipItem b)
    {
        if (a.Kind != b.Kind) return false;
        if (a.TryGetUrl(out var ua) && b.TryGetUrl(out var ub))
            return string.Equals(ua!.Host, ub!.Host, StringComparison.OrdinalIgnoreCase);
        return a.Kind switch
        {
            ClipKind.Text => Normalize(a.Text) == Normalize(b.Text),
            ClipKind.Files => Normalize(string.Join("|", a.Files ?? [])) == Normalize(string.Join("|", b.Files ?? [])),
            ClipKind.Image => a.Hash == b.Hash,
            _ => false,
        };
    }

    private static string Normalize(string? s) =>
        new string((s ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToLowerInvariant();

    public void Clear(bool includePinned)
    {
        _items.RemoveAll(i => includePinned || !i.IsPinned);
        Persist(st => st.DeleteAll(includePinned));
        Changed?.Invoke();
    }

    /// <summary>Apply the size limit and age-based cleanup.</summary>
    public void EnforceLimits(bool raiseChanged = true)
    {
        var s = _settings();
        var victims = new List<ClipItem>();

        if (AppSettings.ToTimeSpan(s.AutoCleanup) is { } maxAge)
        {
            var cutoff = DateTime.Now - maxAge;
            victims.AddRange(_items.Where(i => i.UpdatedAt < cutoff && (!i.IsPinned || s.CleanupIncludesPinned)));
        }

        if (s.MaxItems > 0)
        {
            int excess = _items.Count - victims.Count - s.MaxItems;
            if (excess > 0)
            {
                victims.AddRange(_items
                    .Where(i => !i.IsPinned && !victims.Contains(i))
                    .OrderBy(i => i.UpdatedAt)
                    .Take(excess));
            }
        }

        if (victims.Count > 0)
        {
            Remove(victims, raiseChanged);
            Log.Info($"Cleanup removed {victims.Count} item(s)");
        }
    }

    private void Remove(IReadOnlyCollection<ClipItem> victims, bool raiseChanged = true)
    {
        if (victims.Count == 0) return;
        var set = victims.ToHashSet();
        _items.RemoveAll(set.Contains);
        Persist(st => st.Delete(set.Select(i => i.Id).Where(id => id > 0).ToList()));
        if (raiseChanged) Changed?.Invoke();
    }

    public byte[]? LoadImage(ClipItem item)
    {
        Flush();
        lock (_storeLock) return _store.LoadImage(item.Id);
    }

    public byte[]? LoadThumbnail(ClipItem item)
    {
        if (item.ThumbPng is not null || item.Id == 0) return item.ThumbPng;
        lock (_storeLock) return item.ThumbPng = _store.LoadThumbnail(item.Id);
    }

    public Task<int> SetEncryptionAsync(bool encrypt) => Persist(st => st.SetEncryption(encrypt));

    public Task MoveDatabaseAsync(string newFilePath) => Persist(st => { st.MoveTo(newFilePath); return 0; });

    /// <summary>Block until all queued writes have reached the database.</summary>
    public void Flush()
    {
        Task t;
        lock (_queueLock) t = _tail;
        try { t.Wait(TimeSpan.FromSeconds(10)); } catch { /* already logged */ }
    }

    private void Persist(Action<HistoryStore> op) => Persist(st => { op(st); return 0; });

    private Task<T> Persist<T>(Func<HistoryStore, T> op)
    {
        lock (_queueLock)
        {
            var task = _tail.ContinueWith(_ =>
            {
                lock (_storeLock) return op(_store);
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            _tail = task.ContinueWith(t =>
            {
                if (t.IsFaulted) Log.Error("Database operation failed", t.Exception?.GetBaseException());
            }, TaskScheduler.Default);
            return task;
        }
    }

    public void Dispose()
    {
        Flush();
        lock (_storeLock) _store.Dispose();
    }

    private static byte[]? Utf8(string? s) => s is null ? null : Encoding.UTF8.GetBytes(s);
}
