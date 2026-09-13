using System.Globalization;
using System.Text.Json;
using HealthBreak.Core.Models;
using Microsoft.Data.Sqlite;

namespace HealthBreak.Data;

/// <summary>Local persistence. Use on the application's background coordinator; calls are synchronous.</summary>
public sealed class HealthRepository : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> ApplicationCategories =
        ["Work", "Browser", "Gaming", "Communication", "Development", "Entertainment", "Other"];
    private readonly SqliteConnection _connection;
    private readonly string? _databasePath;
    private readonly object _gate = new();
    private DateTimeOffset _lastBackupUtc = DateTimeOffset.MinValue;
    private bool _disposed;

    public string? RecoveryNotice { get; }
    public string? MaintenanceWarning { get; private set; }

    public HealthRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (databasePath == ":memory:")
        {
            _connection = OpenDatabase(databasePath);
            return;
        }

        _databasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        try
        {
            _connection = OpenDatabase(_databasePath);
        }
        catch (Exception error) when (IsCorruption(error))
        {
            (_connection, RecoveryNotice) = RecoverDatabase(_databasePath);
        }
    }

    /// <summary>Seeds the bundled catalog and closes interrupted sessions at their last saved checkpoint.</summary>
    public void Initialize(IEnumerable<Exercise> exercises)
    {
        ArgumentNullException.ThrowIfNull(exercises);
        lock (_gate)
        {
            EnsureOpen();
            using var transaction = _connection.BeginTransaction();
            foreach (var exercise in exercises)
            {
                using var command = CreateCommand("""
                    INSERT INTO exercises(id,name,category,description,duration_seconds,repetitions)
                    VALUES($id,$name,$category,$description,$duration,$repetitions)
                    ON CONFLICT(id) DO UPDATE SET name=excluded.name, category=excluded.category,
                        description=excluded.description, duration_seconds=excluded.duration_seconds,
                        repetitions=excluded.repetitions;
                    """, transaction,
                    ("$id", exercise.Id), ("$name", exercise.Name), ("$category", exercise.Category.ToString()),
                    ("$description", exercise.Description), ("$duration", exercise.DurationSeconds),
                    ("$repetitions", exercise.Repetitions));
                command.ExecuteNonQuery();
            }

            using var recover = CreateCommand(
                "UPDATE sessions SET end_time=last_observed_at WHERE end_time IS NULL;", transaction);
            recover.ExecuteNonQuery();
            transaction.Commit();
            TryRefreshBackup(force: true);
        }
    }

    public AppSettings LoadSettings()
    {
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand("SELECT json FROM settings WHERE id=1;");
            if (command.ExecuteScalar() is not string json)
            {
                return new AppSettings();
            }

            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                return settings is not null && settings.Validate().Count == 0 ? settings : new AppSettings();
            }
            catch (JsonException)
            {
                return new AppSettings();
            }
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = settings.Validate();
        if (errors.Count != 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(settings));
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand("""
                INSERT INTO settings(id,json) VALUES(1,$json)
                ON CONFLICT(id) DO UPDATE SET json=excluded.json;
                """, null, ("$json", json));
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Atomically checkpoints changed sessions and breaks; IDs make repeated saves idempotent.</summary>
    public void SaveChanges(IEnumerable<SessionRecord> sessions, IEnumerable<BreakRecord> breaks)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(breaks);
        lock (_gate)
        {
            EnsureOpen();
            using var transaction = _connection.BeginTransaction();
            foreach (var session in sessions)
            {
                RequireSeconds(session.ActiveSeconds);
                RequireSeconds(session.IdleSeconds);
                using var command = CreateCommand("""
                    INSERT INTO sessions(id,start_time,end_time,last_observed_at,local_date,active_seconds,idle_seconds,is_demo)
                    VALUES($id,$start,$end,$observed,$date,$active,$idle,$demo)
                    ON CONFLICT(id) DO UPDATE SET end_time=excluded.end_time,
                        last_observed_at=excluded.last_observed_at, active_seconds=excluded.active_seconds,
                        idle_seconds=excluded.idle_seconds;
                    """, transaction,
                    ("$id", session.Id), ("$start", Timestamp(session.StartTime)),
                    ("$end", session.EndTime is { } end ? Timestamp(end) : null),
                    ("$observed", Timestamp(session.EndTime ?? session.LastObservedAt ?? session.StartTime)),
                    ("$date", LocalDate(session.StartTime)), ("$active", session.ActiveSeconds),
                    ("$idle", session.IdleSeconds), ("$demo", session.IsDemo));
                command.ExecuteNonQuery();
            }

            foreach (var item in breaks)
            {
                RequireSeconds(item.DurationSeconds);
                using var command = CreateCommand("""
                    INSERT INTO breaks(id,start_time,local_date,duration_seconds,type,completed,skipped,emergency,is_demo)
                    VALUES($id,$start,$date,$duration,$type,$completed,$skipped,$emergency,$demo)
                    ON CONFLICT(id) DO UPDATE SET duration_seconds=excluded.duration_seconds,
                        type=excluded.type, completed=excluded.completed, skipped=excluded.skipped,
                        emergency=excluded.emergency;
                    """, transaction,
                    ("$id", item.Id), ("$start", Timestamp(item.StartTime)), ("$date", LocalDate(item.StartTime)),
                    ("$duration", item.DurationSeconds), ("$type", item.Type.ToString()),
                    ("$completed", item.Completed), ("$skipped", item.Skipped), ("$emergency", item.Emergency),
                    ("$demo", item.IsDemo));
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    /// <summary>Records shown/skipped exercises, allowing a later completion once per exercise and break.</summary>
    public void SaveExerciseHistory(ExerciseHistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand("""
                INSERT INTO exercise_history(break_id,exercise_id,timestamp,local_date,completed,is_demo)
                VALUES($break,$exercise,$timestamp,$date,$completed,$demo)
                ON CONFLICT(break_id,exercise_id,is_demo) DO UPDATE SET
                    timestamp=CASE WHEN exercise_history.completed=0 AND excluded.completed=1
                        THEN excluded.timestamp ELSE exercise_history.timestamp END,
                    local_date=CASE WHEN exercise_history.completed=0 AND excluded.completed=1
                        THEN excluded.local_date ELSE exercise_history.local_date END,
                    completed=MAX(exercise_history.completed,excluded.completed);
                """, null, ("$break", record.BreakId), ("$exercise", record.ExerciseId),
                ("$timestamp", Timestamp(record.Timestamp)), ("$date", LocalDate(record.Timestamp)),
                ("$completed", record.Completed), ("$demo", record.IsDemo));
            command.ExecuteNonQuery();
        }
    }

    public void SaveReminderAction(ReminderActionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var action = record.Action.ToLowerInvariant();
        if (action is not ("snooze" or "skip" or "emergency"))
        {
            throw new ArgumentException("Unknown reminder action. Expected snooze, skip, or emergency.", nameof(record));
        }

        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand("""
                INSERT INTO reminder_actions(id,timestamp,local_date,action,is_demo)
                VALUES($id,$timestamp,$date,$action,$demo) ON CONFLICT(id) DO NOTHING;
                """, null, ("$id", record.Id), ("$timestamp", Timestamp(record.Timestamp)),
                ("$date", LocalDate(record.Timestamp)), ("$action", action), ("$demo", record.IsDemo));
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Stores category totals only. Process names, window titles and input content are never persisted.</summary>
    public void AddApplicationTime(DateOnly date, string category, double seconds, bool isDemo)
    {
        RequireSeconds(seconds);
        if (seconds == 0)
        {
            return;
        }

        category = ApplicationCategories.Contains(category) ? category : "Other";
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand("""
                INSERT INTO application_usage(local_date,category,active_seconds,is_demo)
                VALUES($date,$category,$seconds,$demo)
                ON CONFLICT(local_date,category,is_demo) DO UPDATE SET
                    active_seconds=application_usage.active_seconds+excluded.active_seconds;
                """, null, ("$date", Date(date)), ("$category", category), ("$seconds", seconds), ("$demo", isDemo));
            command.ExecuteNonQuery();
        }
    }

    public DailyStatistics GetStatistics(DateOnly date, bool isDemo)
    {
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand("""
                SELECT
                    (SELECT COALESCE(SUM(active_seconds),0) FROM sessions WHERE local_date=$date AND is_demo=$demo),
                    (SELECT COALESCE(SUM(idle_seconds),0) FROM sessions WHERE local_date=$date AND is_demo=$demo),
                    (SELECT COUNT(*) FROM sessions WHERE local_date=$date AND is_demo=$demo AND active_seconds>0),
                    (SELECT COUNT(*) FROM breaks WHERE local_date=$date AND is_demo=$demo AND completed=1),
                    (SELECT COALESCE(AVG(active_seconds),0) FROM sessions WHERE local_date=$date AND is_demo=$demo AND active_seconds>0),
                    (SELECT COALESCE(MAX(active_seconds),0) FROM sessions WHERE local_date=$date AND is_demo=$demo),
                    (SELECT COUNT(*) FROM exercise_history WHERE local_date=$date AND is_demo=$demo AND completed=1),
                    (SELECT COUNT(*) FROM reminder_actions WHERE local_date=$date AND is_demo=$demo AND action IN ('skip','emergency')),
                    (SELECT COUNT(*) FROM reminder_actions WHERE local_date=$date AND is_demo=$demo AND action='snooze'),
                    (SELECT COUNT(*) FROM reminder_actions WHERE local_date=$date AND is_demo=$demo AND action='emergency');
                """, null, ("$date", Date(date)), ("$demo", isDemo));
            using var reader = command.ExecuteReader();
            reader.Read();
            return new DailyStatistics(date, reader.GetDouble(0), reader.GetDouble(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetInt32(6),
                reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9));
        }
    }

    /// <summary>Seven local calendar days, oldest first, including days without recorded activity.</summary>
    public IReadOnlyList<DailyStatistics> GetWeek(DateOnly endingDate, bool isDemo)
    {
        lock (_gate)
        {
            EnsureOpen();
            return Enumerable.Range(-6, 7).Select(offset => GetStatistics(endingDate.AddDays(offset), isDemo)).ToArray();
        }
    }

    public IReadOnlyList<ExerciseHistoryRecord> GetExerciseHistory(bool isDemo)
    {
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand("""
                SELECT break_id,exercise_id,timestamp,completed,is_demo FROM exercise_history
                WHERE is_demo=$demo ORDER BY timestamp DESC,break_id,exercise_id;
                """, null, ("$demo", isDemo));
            using var reader = command.ExecuteReader();
            var results = new List<ExerciseHistoryRecord>();
            while (reader.Read())
            {
                results.Add(new ExerciseHistoryRecord(reader.GetString(0), reader.GetInt32(1), ParseTime(reader.GetString(2)),
                    reader.GetBoolean(3), reader.GetBoolean(4)));
            }

            return results;
        }
    }

    public IReadOnlyList<BreakRecord> GetRecentBreaks(bool isDemo, int count = 3)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand("""
                SELECT id,start_time,duration_seconds,type,completed,skipped,emergency,is_demo FROM breaks
                WHERE is_demo=$demo AND completed=1 ORDER BY start_time DESC,id LIMIT $count;
                """, null, ("$demo", isDemo), ("$count", count));
            using var reader = command.ExecuteReader();
            var results = new List<BreakRecord>();
            while (reader.Read())
            {
                results.Add(new BreakRecord(reader.GetString(0), ParseTime(reader.GetString(1)), reader.GetDouble(2),
                    Enum.Parse<BreakType>(reader.GetString(3)), reader.GetBoolean(4), reader.GetBoolean(5),
                    reader.GetBoolean(6), reader.GetBoolean(7)));
            }

            return results;
        }
    }

    public IReadOnlyDictionary<string, double> GetApplicationUsage(DateOnly date, bool isDemo)
    {
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand("""
                SELECT category,active_seconds FROM application_usage WHERE local_date=$date AND is_demo=$demo
                ORDER BY active_seconds DESC,category;
                """, null, ("$date", Date(date)), ("$demo", isDemo));
            using var reader = command.ExecuteReader();
            var results = new Dictionary<string, double>(StringComparer.Ordinal);
            while (reader.Read())
            {
                results.Add(reader.GetString(0), reader.GetDouble(1));
            }

            return results;
        }
    }

    public IReadOnlyList<SessionRecord> GetSessions(DateOnly date, bool isDemo)
    {
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand("""
                SELECT id,start_time,end_time,active_seconds,idle_seconds,is_demo,last_observed_at FROM sessions
                WHERE local_date=$date AND is_demo=$demo ORDER BY start_time,id;
                """, null, ("$date", Date(date)), ("$demo", isDemo));
            using var reader = command.ExecuteReader();
            var results = new List<SessionRecord>();
            while (reader.Read())
            {
                results.Add(new SessionRecord(reader.GetString(0), ParseTime(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : ParseTime(reader.GetString(2)), reader.GetDouble(3), reader.GetDouble(4),
                    reader.GetBoolean(5), ParseTime(reader.GetString(6))));
            }

            return results;
        }
    }

    public void MaintainBackup()
    {
        lock (_gate)
        {
            EnsureOpen();
            TryRefreshBackup(force: false);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            TryRefreshBackup(force: true);
            _disposed = true;
            _connection.Dispose();
        }
    }

    private SqliteCommand CreateCommand(string sql, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void TryRefreshBackup(bool force)
    {
        if (_databasePath is null) return;
        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastBackupUtc < TimeSpan.FromMinutes(2)) return;
        _lastBackupUtc = now;
        try
        {
            WriteBackup(_connection, _databasePath);
            MaintenanceWarning = null;
        }
        catch (Exception error)
        {
            MaintenanceWarning = "HealthBreak could not refresh its safety backup. The main database remains usable. "
                + error.Message;
        }
    }

    private static SqliteConnection OpenDatabase(string dataSource)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; "
                    + "PRAGMA busy_timeout=5000; PRAGMA wal_autocheckpoint=1000;";
                command.ExecuteNonQuery();
            }
            VerifyIntegrity(connection);
            DatabaseSchema.Migrate(connection);
            VerifyIntegrity(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static (SqliteConnection Connection, string Notice) RecoverDatabase(string databasePath)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backupPath = databasePath + ".backup";
        var validBackup = File.Exists(backupPath) && IsHealthyBackup(backupPath);
        var preservedPath = QuarantineDatabase(databasePath, timestamp);

        if (validBackup)
        {
            File.Copy(backupPath, databasePath, overwrite: true);
            try
            {
                return (OpenDatabase(databasePath),
                    $"The local database was damaged and was restored from its safety backup. "
                    + $"The damaged copy was preserved as {Path.GetFileName(preservedPath)}.");
            }
            catch (Exception error) when (IsCorruption(error))
            {
                QuarantineDatabase(databasePath, timestamp + "-failed-restore");
            }
        }
        else if (File.Exists(backupPath))
        {
            TryMove(backupPath, backupPath + ".invalid-" + timestamp);
        }

        return (OpenDatabase(databasePath),
            $"The local database was damaged and no valid backup was available. "
            + $"A new database was created; the damaged copy was preserved as {Path.GetFileName(preservedPath)}.");
    }

    private static string QuarantineDatabase(string databasePath, string timestamp)
    {
        var preservedPath = databasePath + ".corrupt-" + timestamp;
        TryMove(databasePath, preservedPath);
        TryMove(databasePath + "-wal", preservedPath + "-wal");
        TryMove(databasePath + "-shm", preservedPath + "-shm");
        return preservedPath;
    }

    private static void TryMove(string source, string destination)
    {
        if (!File.Exists(source)) return;
        File.Move(source, destination, overwrite: true);
    }

    private static bool IsHealthyBackup(string path)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                ForeignKeys = true,
                Pooling = false
            }.ToString());
            connection.Open();
            VerifyIntegrity(connection);
            using var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(version.ExecuteScalar()) <= DatabaseSchema.CurrentVersion;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteBackup(SqliteConnection source, string databasePath)
    {
        var backupPath = databasePath + ".backup";
        var temporaryPath = backupPath + ".tmp";
        DeleteIfExists(temporaryPath);
        DeleteIfExists(temporaryPath + "-wal");
        DeleteIfExists(temporaryPath + "-shm");
        try
        {
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
                Pooling = false
            }.ToString()))
            {
                destination.Open();
                source.BackupDatabase(destination);
            }
            if (!IsHealthyBackup(temporaryPath))
                throw new InvalidDataException("The newly created backup did not pass its integrity check.");
            File.Move(temporaryPath, backupPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
            DeleteIfExists(temporaryPath + "-wal");
            DeleteIfExists(temporaryPath + "-shm");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void VerifyIntegrity(SqliteConnection connection)
    {
        using (var quickCheck = connection.CreateCommand())
        {
            quickCheck.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(Convert.ToString(quickCheck.ExecuteScalar(), CultureInfo.InvariantCulture),
                    "ok", StringComparison.OrdinalIgnoreCase))
                throw new DatabaseCorruptionException("SQLite integrity check failed.");
        }
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";
        using var reader = foreignKeys.ExecuteReader();
        if (reader.Read()) throw new DatabaseCorruptionException("SQLite foreign-key integrity check failed.");
    }

    private static bool IsCorruption(Exception error) =>
        error is DatabaseCorruptionException
        || error is SqliteException { SqliteErrorCode: 11 or 26 };

    private sealed class DatabaseCorruptionException(string message) : Exception(message);

    private static string Timestamp(DateTimeOffset timestamp) => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTime(string timestamp) => DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string LocalDate(DateTimeOffset timestamp) => Date(DateOnly.FromDateTime(timestamp.Date));
    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void RequireSeconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Duration must be finite and nonnegative.");
        }
    }
}
