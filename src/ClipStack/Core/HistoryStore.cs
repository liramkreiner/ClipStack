using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ClipStack.Core;

/// <summary>
/// SQLite persistence for clipboard history. Content (text/html/rtf/files, image, thumbnail)
/// is stored as blobs, optionally DPAPI-encrypted per row. Not thread-safe: callers serialize access.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private const int SchemaVersion = 1;
    private SqliteConnection _db;

    public string FilePath { get; private set; }

    public HistoryStore(string filePath)
    {
        FilePath = filePath;
        _db = Open(filePath);
    }

    private static SqliteConnection Open(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        db.Open();
        Exec(db, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;");

        long version = (long)Scalar(db, "PRAGMA user_version")!;
        if (version < 1)
        {
            Exec(db, """
                CREATE TABLE IF NOT EXISTS clipboard_items (
                    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                    type               INTEGER NOT NULL,
                    created_at         INTEGER NOT NULL,
                    updated_at         INTEGER NOT NULL,
                    last_used_at       INTEGER,
                    use_count          INTEGER NOT NULL DEFAULT 0,
                    copy_count         INTEGER NOT NULL DEFAULT 1,
                    is_pinned          INTEGER NOT NULL DEFAULT 0,
                    pinned_at          INTEGER,
                    source_application TEXT,
                    source_path        TEXT,
                    content_hash       TEXT NOT NULL,
                    encrypted          INTEGER NOT NULL DEFAULT 0,
                    content            BLOB,
                    image              BLOB,
                    thumbnail          BLOB,
                    image_width        INTEGER NOT NULL DEFAULT 0,
                    image_height       INTEGER NOT NULL DEFAULT 0,
                    metadata           TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_items_hash ON clipboard_items(content_hash);
                CREATE INDEX IF NOT EXISTS ix_items_updated ON clipboard_items(updated_at);
                """);
            Exec(db, $"PRAGMA user_version = {SchemaVersion}");
        }
        return db;
    }

    /// <summary>Move the database file to a new folder and reopen it there.</summary>
    public void MoveTo(string newFilePath)
    {
        if (string.Equals(Path.GetFullPath(newFilePath), Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase))
            return;
        Exec(_db, "PRAGMA wal_checkpoint(TRUNCATE)");
        _db.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(newFilePath)!);
        File.Copy(FilePath, newFilePath, overwrite: true);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            TryDelete(FilePath + suffix);
        FilePath = newFilePath;
        _db = Open(newFilePath);
    }

    public List<ClipItem> LoadAll()
    {
        var items = new List<ClipItem>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT id, type, created_at, updated_at, last_used_at, use_count, copy_count, is_pinned, pinned_at,
                   source_application, source_path, content_hash, encrypted, content, image_width, image_height
            FROM clipboard_items ORDER BY updated_at DESC
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            try
            {
                bool encrypted = r.GetInt64(12) != 0;
                var blob = r.IsDBNull(13) ? null : (byte[])r[13];
                var content = DecodeContent(Decrypt(blob, encrypted));
                items.Add(new ClipItem
                {
                    Id = r.GetInt64(0),
                    Kind = (ClipKind)r.GetInt32(1),
                    CreatedAt = FromUnix(r.GetInt64(2)),
                    UpdatedAt = FromUnix(r.GetInt64(3)),
                    LastUsedAt = r.IsDBNull(4) ? null : FromUnix(r.GetInt64(4)),
                    UseCount = r.GetInt32(5),
                    CopyCount = r.GetInt32(6),
                    IsPinned = r.GetInt64(7) != 0,
                    PinnedAt = r.IsDBNull(8) ? null : FromUnix(r.GetInt64(8)),
                    SourceApp = r.IsDBNull(9) ? null : r.GetString(9),
                    SourcePath = r.IsDBNull(10) ? null : r.GetString(10),
                    Hash = r.GetString(11),
                    Text = content.Text,
                    Html = content.Html,
                    Rtf = content.Rtf,
                    Files = content.Files,
                    ImageWidth = r.GetInt32(14),
                    ImageHeight = r.GetInt32(15),
                });
            }
            catch (Exception ex)
            {
                // e.g. DB copied from another Windows account: DPAPI can't decrypt it. Skip the row.
                Log.Error($"Skipping unreadable history row {r.GetInt64(0)}", ex);
            }
        }
        return items;
    }

    public long Insert(ClipItem item, byte[]? imagePng, byte[]? thumbPng, bool encrypt)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO clipboard_items (type, created_at, updated_at, last_used_at, use_count, copy_count, is_pinned, pinned_at,
                source_application, source_path, content_hash, encrypted, content, image, thumbnail, image_width, image_height)
            VALUES ($type, $created, $updated, $lastUsed, $useCount, $copyCount, $pinned, $pinnedAt,
                $app, $path, $hash, $enc, $content, $image, $thumb, $w, $h);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$type", (int)item.Kind);
        cmd.Parameters.AddWithValue("$created", ToUnix(item.CreatedAt));
        cmd.Parameters.AddWithValue("$updated", ToUnix(item.UpdatedAt));
        cmd.Parameters.AddWithValue("$lastUsed", item.LastUsedAt is { } lu ? ToUnix(lu) : DBNull.Value);
        cmd.Parameters.AddWithValue("$useCount", item.UseCount);
        cmd.Parameters.AddWithValue("$copyCount", item.CopyCount);
        cmd.Parameters.AddWithValue("$pinned", item.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$pinnedAt", item.PinnedAt is { } pa ? ToUnix(pa) : DBNull.Value);
        cmd.Parameters.AddWithValue("$app", (object?)item.SourceApp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$path", (object?)item.SourcePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", item.Hash);
        cmd.Parameters.AddWithValue("$enc", encrypt ? 1 : 0);
        cmd.Parameters.AddWithValue("$content", Blob(Encrypt(EncodeContent(item), encrypt)));
        cmd.Parameters.AddWithValue("$image", Blob(Encrypt(imagePng, encrypt)));
        cmd.Parameters.AddWithValue("$thumb", Blob(Encrypt(thumbPng, encrypt)));
        cmd.Parameters.AddWithValue("$w", item.ImageWidth);
        cmd.Parameters.AddWithValue("$h", item.ImageHeight);
        return (long)cmd.ExecuteScalar()!;
    }

    public void UpdateMeta(ClipItem item)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            UPDATE clipboard_items SET updated_at=$updated, last_used_at=$lastUsed, use_count=$useCount,
                copy_count=$copyCount, is_pinned=$pinned, pinned_at=$pinnedAt WHERE id=$id
            """;
        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$updated", ToUnix(item.UpdatedAt));
        cmd.Parameters.AddWithValue("$lastUsed", item.LastUsedAt is { } lu ? ToUnix(lu) : DBNull.Value);
        cmd.Parameters.AddWithValue("$useCount", item.UseCount);
        cmd.Parameters.AddWithValue("$copyCount", item.CopyCount);
        cmd.Parameters.AddWithValue("$pinned", item.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$pinnedAt", item.PinnedAt is { } pa ? ToUnix(pa) : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Delete(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return;
        using var tx = _db.BeginTransaction();
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM clipboard_items WHERE id=$id";
        var p = cmd.Parameters.Add("$id", SqliteType.Integer);
        foreach (var id in ids)
        {
            p.Value = id;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void DeleteAll(bool includePinned)
    {
        Exec(_db, includePinned ? "DELETE FROM clipboard_items" : "DELETE FROM clipboard_items WHERE is_pinned=0");
        // Reclaim space so deleted content doesn't linger in free pages.
        Exec(_db, "PRAGMA wal_checkpoint(TRUNCATE); VACUUM;");
    }

    public byte[]? LoadImage(long id) => LoadBlob(id, "image");
    public byte[]? LoadThumbnail(long id) => LoadBlob(id, "thumbnail");

    private byte[]? LoadBlob(long id, string column)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT {column}, encrypted FROM clipboard_items WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(0)) return null;
        try { return Decrypt((byte[])r[0], r.GetInt64(1) != 0); }
        catch (Exception ex) { Log.Error($"Could not read {column} of row {id}", ex); return null; }
    }

    /// <summary>Rewrite every row with the requested encryption state.</summary>
    public int SetEncryption(bool encrypt)
    {
        var rows = new List<(long id, byte[]? c, byte[]? i, byte[]? t)>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, encrypted, content, image, thumbnail FROM clipboard_items WHERE encrypted<>$enc";
            cmd.Parameters.AddWithValue("$enc", encrypt ? 1 : 0);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                bool enc = r.GetInt64(1) != 0;
                try
                {
                    rows.Add((r.GetInt64(0),
                        Decrypt(r.IsDBNull(2) ? null : (byte[])r[2], enc),
                        Decrypt(r.IsDBNull(3) ? null : (byte[])r[3], enc),
                        Decrypt(r.IsDBNull(4) ? null : (byte[])r[4], enc)));
                }
                catch (Exception ex) { Log.Error("Re-encryption skipped a row", ex); }
            }
        }
        using var tx = _db.BeginTransaction();
        foreach (var (id, c, i, t) in rows)
        {
            using var u = _db.CreateCommand();
            u.Transaction = tx;
            u.CommandText = "UPDATE clipboard_items SET encrypted=$enc, content=$c, image=$i, thumbnail=$t WHERE id=$id";
            u.Parameters.AddWithValue("$enc", encrypt ? 1 : 0);
            u.Parameters.AddWithValue("$c", Blob(Encrypt(c, encrypt)));
            u.Parameters.AddWithValue("$i", Blob(Encrypt(i, encrypt)));
            u.Parameters.AddWithValue("$t", Blob(Encrypt(t, encrypt)));
            u.Parameters.AddWithValue("$id", id);
            u.ExecuteNonQuery();
        }
        tx.Commit();
        Exec(_db, "PRAGMA wal_checkpoint(TRUNCATE); VACUUM;");
        return rows.Count;
    }

    public void Dispose() => _db.Dispose();

    // ---- content encoding ----

    private const byte ContentFormat = 1;

    internal static byte[] EncodeContent(ClipItem item)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(ContentFormat);
            WriteString(w, item.Text);
            WriteString(w, item.Html);
            WriteString(w, item.Rtf);
            w.Write(item.Files?.Length ?? -1);
            foreach (var f in item.Files ?? []) w.Write(f);
        }
        return ms.ToArray();
    }

    internal static (string? Text, string? Html, string? Rtf, string[]? Files) DecodeContent(byte[]? data)
    {
        if (data is null || data.Length == 0) return default;
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        if (r.ReadByte() != ContentFormat) throw new InvalidDataException("Unknown content format");
        var text = ReadString(r);
        var html = ReadString(r);
        var rtf = ReadString(r);
        int n = r.ReadInt32();
        string[]? files = null;
        if (n >= 0)
        {
            files = new string[n];
            for (int i = 0; i < n; i++) files[i] = r.ReadString();
        }
        return (text, html, rtf, files);
    }

    private static void WriteString(BinaryWriter w, string? s)
    {
        w.Write(s is not null);
        if (s is not null) w.Write(s);
    }

    private static string? ReadString(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

    private static byte[]? Encrypt(byte[]? data, bool encrypt) =>
        data is null ? null : encrypt ? Protector.Protect(data) : data;

    private static byte[]? Decrypt(byte[]? data, bool encrypted) =>
        data is null ? null : encrypted ? Protector.Unprotect(data) : data;

    private static object Blob(byte[]? b) => (object?)b ?? DBNull.Value;

    private static long ToUnix(DateTime t) => new DateTimeOffset(t.ToUniversalTime()).ToUnixTimeMilliseconds();
    private static DateTime FromUnix(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    private static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
