using Microsoft.Data.Sqlite;

namespace ClipForge.Core;

/// <summary>SQLite-backed <see cref="IClipStore"/>. DB lives at %LOCALAPPDATA%/ClipForge/clips.db.</summary>
public sealed class SqliteClipStore : IClipStore
{
    private readonly string _connectionString;

    public SqliteClipStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS clips (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type TEXT NOT NULL,
                text_content TEXT,
                image_data BLOB,
                preview_text TEXT NOT NULL,
                source_app TEXT,
                created_at INTEGER NOT NULL,
                pinned INTEGER NOT NULL DEFAULT 0,
                content_hash TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS idx_clips_created ON clips(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_clips_pinned ON clips(pinned, created_at DESC);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> SaveAsync(Clip clip, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO clips (type, text_content, image_data, preview_text, source_app, created_at, pinned, content_hash)
            VALUES ($type, $text, $image, $preview, $app, $created, $pinned, $hash);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$type", clip.Type.ToString());
        cmd.Parameters.AddWithValue("$text", (object?)clip.Text ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$image", (object?)clip.ImageData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$preview", clip.PreviewText);
        cmd.Parameters.AddWithValue("$app", (object?)clip.SourceApp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", clip.CreatedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$pinned", clip.Pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$hash", clip.ContentHash);
        var id = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        clip.Id = id;
        return id;
    }

    public async Task<IReadOnlyList<Clip>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"{Select} ORDER BY pinned DESC, created_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<Clip?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"{Select} WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var list = await ReadAllAsync(cmd, ct);
        return list.Count == 0 ? null : list[0];
    }

    public async Task<IReadOnlyList<Clip>> SearchAsync(string query, int limit = 100, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"{Select} WHERE preview_text LIKE $q ESCAPE '\\' OR text_content LIKE $q ESCAPE '\\' ORDER BY created_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$q", $"%{EscapeLike(query)}%");
        cmd.Parameters.AddWithValue("$limit", limit);
        return await ReadAllAsync(cmd, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM clips WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetPinnedAsync(long id, bool pinned, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE clips SET pinned = $pinned WHERE id = $id";
        cmd.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> PruneAsync(int maxUnpinned, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM clips WHERE pinned = 0 AND id NOT IN (
                SELECT id FROM clips WHERE pinned = 0 ORDER BY created_at DESC LIMIT $keep
            )
            """;
        cmd.Parameters.AddWithValue("$keep", maxUnpinned);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> PruneByAgeAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(maxAge).ToUnixTimeMilliseconds();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM clips WHERE pinned = 0 AND created_at < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM clips";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private const string Select =
        "SELECT id, type, text_content, image_data, preview_text, source_app, created_at, pinned, content_hash FROM clips";

    private static async Task<IReadOnlyList<Clip>> ReadAllAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<Clip>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Clip
            {
                Id = reader.GetInt64(0),
                Type = Enum.Parse<ClipType>(reader.GetString(1)),
                Text = reader.IsDBNull(2) ? null : reader.GetString(2),
                ImageData = reader.IsDBNull(3) ? null : (byte[])reader.GetValue(3),
                PreviewText = reader.GetString(4),
                SourceApp = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)),
                Pinned = reader.GetInt64(7) != 0,
                ContentHash = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
            });
        }
        return list;
    }

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
