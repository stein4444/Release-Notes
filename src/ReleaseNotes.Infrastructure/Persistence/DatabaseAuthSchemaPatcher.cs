using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Infrastructure.Persistence.Entities;

namespace ReleaseNotes.Infrastructure.Persistence;

/// <summary>
/// Додає таблицю Users і колонки OwnerUserId для існуючих БД (EnsureCreated не оновлює схему).
/// </summary>
public static class DatabaseAuthSchemaPatcher
{
    public static async Task ApplyAsync(ReleaseNotesDbContext db, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsSqlServer())
        {
            await ApplySqlServerAsync(db, cancellationToken);
        }
        else if (db.Database.IsSqlite())
        {
            await ApplySqliteAsync(db, cancellationToken);
        }
    }

    private static async Task ApplySqlServerAsync(ReleaseNotesDbContext db, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            db,
            """
            IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Users (
                    Id uniqueidentifier NOT NULL PRIMARY KEY,
                    Email nvarchar(256) NOT NULL,
                    PasswordHash nvarchar(max) NOT NULL,
                    DisplayName nvarchar(200) NOT NULL,
                    CreatedAt datetimeoffset NOT NULL
                );
                CREATE UNIQUE INDEX IX_Users_Email ON dbo.Users(Email);
            END
            """,
            cancellationToken);

        var repoTable = await ResolveTableByColumnsAsync(
            db, ["RepositoryPath", "Provider", "DisplayName"], cancellationToken)
            ?? GetModelTableName(db, typeof(RepositoryConnectionEntity));

        var documentsTable = await ResolveTableByColumnAsync(db, "EntriesJson", cancellationToken)
            ?? GetModelTableName(db, typeof(ReleaseNoteDocumentEntity));

        var jobsTable = await ResolveTableByColumnsAsync(
            db, ["BaseTag", "TargetTag", "Status"], cancellationToken)
            ?? GetModelTableName(db, typeof(ReleaseNoteJobEntity));

        await AddOwnerUserIdColumnSqlServerAsync(db, repoTable, cancellationToken);
        await AddOwnerUserIdColumnSqlServerAsync(db, documentsTable, cancellationToken);
        await AddOwnerUserIdColumnSqlServerAsync(db, jobsTable, cancellationToken);

        if (repoTable is not null)
        {
            await TryRecreateRepositoryUniqueIndexSqlServerAsync(db, repoTable, cancellationToken);
        }
    }

    private static async Task ApplySqliteAsync(ReleaseNotesDbContext db, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            db,
            """
            CREATE TABLE IF NOT EXISTS Users (
                Id TEXT NOT NULL PRIMARY KEY,
                Email TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Email ON Users(Email);
            """,
            cancellationToken);

        foreach (var table in new[]
                 {
                     GetModelTableName(db, typeof(RepositoryConnectionEntity)) ?? "RepositoryConnections",
                     GetModelTableName(db, typeof(ReleaseNoteDocumentEntity)) ?? "Documents",
                     GetModelTableName(db, typeof(ReleaseNoteJobEntity)) ?? "Jobs",
                 })
        {
            await TryAddColumnSqliteAsync(db, table, "OwnerUserId", "TEXT", cancellationToken);
        }
    }

    private static async Task AddOwnerUserIdColumnSqlServerAsync(
        ReleaseNotesDbContext db,
        string? tableName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return;
        }

        var escapedTable = tableName.Replace("]", "]]", StringComparison.Ordinal);
        await ExecuteAsync(
            db,
            $"""
            IF OBJECT_ID(N'dbo.[{escapedTable}]', N'U') IS NOT NULL
               AND COL_LENGTH('dbo.[{escapedTable}]', 'OwnerUserId') IS NULL
            BEGIN
                ALTER TABLE dbo.[{escapedTable}] ADD OwnerUserId uniqueidentifier NULL;
            END
            """,
            cancellationToken);
    }

    private static async Task TryRecreateRepositoryUniqueIndexSqlServerAsync(
        ReleaseNotesDbContext db,
        string tableName,
        CancellationToken cancellationToken)
    {
        var escapedTable = tableName.Replace("]", "]]", StringComparison.Ordinal);
        var newIndex = $"IX_{escapedTable}_OwnerUserId_Provider_RepositoryPath";

        await ExecuteAsync(
            db,
            $"""
            IF OBJECT_ID(N'dbo.[{escapedTable}]', N'U') IS NOT NULL
               AND COL_LENGTH('dbo.[{escapedTable}]', 'OwnerUserId') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RepositoryConnections_Provider_RepositoryPath' AND object_id = OBJECT_ID(N'dbo.[{escapedTable}]'))
                    DROP INDEX IX_RepositoryConnections_Provider_RepositoryPath ON dbo.[{escapedTable}];

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{newIndex}' AND object_id = OBJECT_ID(N'dbo.[{escapedTable}]'))
                    DROP INDEX [{newIndex}] ON dbo.[{escapedTable}];

                CREATE UNIQUE INDEX [{newIndex}]
                    ON dbo.[{escapedTable}](OwnerUserId, Provider, RepositoryPath)
                    WHERE OwnerUserId IS NOT NULL;
            END
            """,
            cancellationToken);
    }

    private static async Task<string?> ResolveTableByColumnAsync(
        ReleaseNotesDbContext db,
        string columnName,
        CancellationToken cancellationToken)
    {
        return await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT TOP (1) t.TABLE_NAME AS [Value]
                FROM INFORMATION_SCHEMA.COLUMNS c
                INNER JOIN INFORMATION_SCHEMA.TABLES t
                    ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
                WHERE c.COLUMN_NAME = {0}
                  AND t.TABLE_TYPE = 'BASE TABLE'
                  AND t.TABLE_SCHEMA = 'dbo'
                  AND t.TABLE_NAME <> 'Users'
                ORDER BY t.TABLE_NAME
                """,
                columnName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<string?> ResolveTableByColumnsAsync(
        ReleaseNotesDbContext db,
        string[] columnNames,
        CancellationToken cancellationToken)
    {
        if (columnNames.Length == 0)
        {
            return null;
        }

        var placeholders = string.Join(", ", columnNames.Select((_, i) => "{" + i + "}"));
        var sql = $"""
            SELECT TOP (1) t.TABLE_NAME AS [Value]
            FROM INFORMATION_SCHEMA.TABLES t
            WHERE t.TABLE_TYPE = 'BASE TABLE'
              AND t.TABLE_SCHEMA = 'dbo'
              AND t.TABLE_NAME <> 'Users'
              AND (
                SELECT COUNT(DISTINCT c.COLUMN_NAME)
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = t.TABLE_SCHEMA
                  AND c.TABLE_NAME = t.TABLE_NAME
                  AND c.COLUMN_NAME IN ({placeholders})
              ) = {columnNames.Length}
            ORDER BY t.TABLE_NAME
            """;

        return await db.Database.SqlQueryRaw<string>(sql, columnNames.Cast<object>().ToArray())
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? GetModelTableName(ReleaseNotesDbContext db, Type entityType) =>
        db.Model.FindEntityType(entityType)?.GetTableName();

    private static async Task ExecuteAsync(
        ReleaseNotesDbContext db,
        string sql,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sql))
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

    private static async Task TryAddColumnSqliteAsync(
        ReleaseNotesDbContext db,
        string table,
        string column,
        string type,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE {table} ADD COLUMN {column} {type} NULL",
                cancellationToken);
        }
        catch
        {
            // column already exists
        }
    }
}
