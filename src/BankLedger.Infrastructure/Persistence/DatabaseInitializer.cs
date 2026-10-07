using Microsoft.Data.Sqlite;

namespace BankLedger.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static void Initialize(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS accounts (
                id TEXT PRIMARY KEY,
                number TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS transactions (
                id TEXT PRIMARY KEY,
                account_id TEXT NOT NULL,
                amount_in_cents INTEGER NOT NULL,
                type TEXT NOT NULL CHECK (type IN ('C', 'D')),
                created_at TEXT NOT NULL,
                occ_version INTEGER NOT NULL,
                FOREIGN KEY (account_id) REFERENCES accounts(id)
            );

            CREATE TABLE IF NOT EXISTS balance_snapshots (
                account_id TEXT PRIMARY KEY,
                balance_in_cents INTEGER NOT NULL,
                last_transaction_id TEXT NOT NULL,
                occ_version INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (account_id) REFERENCES accounts(id),
                FOREIGN KEY (last_transaction_id) REFERENCES transactions(id)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS uk_transactions_account_occ_version
            ON transactions(account_id, occ_version);

            CREATE INDEX IF NOT EXISTS ix_transactions_account_id
            ON transactions(account_id);

            CREATE INDEX IF NOT EXISTS ix_transactions_account_created_at
            ON transactions(account_id, created_at);
            """;
        command.ExecuteNonQuery();
    }
}