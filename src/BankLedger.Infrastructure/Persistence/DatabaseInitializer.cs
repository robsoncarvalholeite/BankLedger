using Microsoft.Data.Sqlite;

namespace BankLedger.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static void Initialize(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Accounts (
                Number TEXT PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS Transactions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountNumber TEXT NOT NULL,
                AmountCents INTEGER NOT NULL,
                Type TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                IdempotencyKey TEXT NOT NULL,
                FOREIGN KEY (AccountNumber) REFERENCES Accounts(Number),
                UNIQUE(AccountNumber, IdempotencyKey)
            );

            CREATE TABLE IF NOT EXISTS BalanceSnapshots (
                AccountNumber TEXT PRIMARY KEY,
                BalanceCents INTEGER NOT NULL,
                LastTransactionId INTEGER NOT NULL,
                Sequence INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (AccountNumber) REFERENCES Accounts(Number)
            );

            CREATE INDEX IF NOT EXISTS IX_Transactions_Account_Id
            ON Transactions(AccountNumber, Id);

            CREATE INDEX IF NOT EXISTS IX_Transactions_Account_CreatedAt
            ON Transactions(AccountNumber, CreatedAt);

            CREATE INDEX IF NOT EXISTS IX_Transactions_Idempotency
            ON Transactions(AccountNumber, IdempotencyKey);
            """;
        command.ExecuteNonQuery();
    }
}