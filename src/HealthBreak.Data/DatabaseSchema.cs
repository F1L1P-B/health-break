using Microsoft.Data.Sqlite;

namespace HealthBreak.Data;

internal static class DatabaseSchema
{
    internal const int CurrentVersion = 1;

    public static void Migrate(SqliteConnection connection)
    {
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());
        if (version > CurrentVersion)
        {
            throw new InvalidOperationException("This database was created by a newer version of HealthBreak. Update the application to open it.");
        }

        if (version == CurrentVersion)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS settings (
                id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT NOT NULL PRIMARY KEY,
                start_time TEXT NOT NULL,
                end_time TEXT NULL,
                last_observed_at TEXT NOT NULL,
                local_date TEXT NOT NULL,
                active_seconds REAL NOT NULL CHECK (active_seconds >= 0),
                idle_seconds REAL NOT NULL CHECK (idle_seconds >= 0),
                is_demo INTEGER NOT NULL CHECK (is_demo IN (0,1))
            );
            CREATE INDEX IF NOT EXISTS ix_sessions_date ON sessions(local_date, is_demo);
            CREATE TABLE IF NOT EXISTS breaks (
                id TEXT NOT NULL PRIMARY KEY,
                start_time TEXT NOT NULL,
                local_date TEXT NOT NULL,
                duration_seconds REAL NOT NULL CHECK (duration_seconds >= 0),
                type TEXT NOT NULL,
                completed INTEGER NOT NULL CHECK (completed IN (0,1)),
                skipped INTEGER NOT NULL CHECK (skipped IN (0,1)),
                emergency INTEGER NOT NULL CHECK (emergency IN (0,1)),
                is_demo INTEGER NOT NULL CHECK (is_demo IN (0,1))
            );
            CREATE INDEX IF NOT EXISTS ix_breaks_date ON breaks(local_date, is_demo);
            CREATE TABLE IF NOT EXISTS exercises (
                id INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                category TEXT NOT NULL,
                description TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL CHECK (duration_seconds > 0),
                repetitions INTEGER NULL
            );
            CREATE TABLE IF NOT EXISTS exercise_history (
                break_id TEXT NOT NULL REFERENCES breaks(id) ON DELETE CASCADE,
                exercise_id INTEGER NOT NULL REFERENCES exercises(id),
                timestamp TEXT NOT NULL,
                local_date TEXT NOT NULL,
                completed INTEGER NOT NULL CHECK (completed IN (0,1)),
                is_demo INTEGER NOT NULL CHECK (is_demo IN (0,1)),
                PRIMARY KEY (break_id, exercise_id, is_demo)
            );
            CREATE INDEX IF NOT EXISTS ix_exercise_history_date ON exercise_history(local_date, is_demo);
            CREATE TABLE IF NOT EXISTS reminder_actions (
                id TEXT NOT NULL PRIMARY KEY,
                timestamp TEXT NOT NULL,
                local_date TEXT NOT NULL,
                action TEXT NOT NULL CHECK (action IN ('snooze','skip','emergency')),
                is_demo INTEGER NOT NULL CHECK (is_demo IN (0,1))
            );
            CREATE INDEX IF NOT EXISTS ix_reminder_actions_date ON reminder_actions(local_date, is_demo);
            CREATE TABLE IF NOT EXISTS application_usage (
                local_date TEXT NOT NULL,
                category TEXT NOT NULL,
                active_seconds REAL NOT NULL CHECK (active_seconds >= 0),
                is_demo INTEGER NOT NULL CHECK (is_demo IN (0,1)),
                PRIMARY KEY (local_date, category, is_demo)
            );
            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
