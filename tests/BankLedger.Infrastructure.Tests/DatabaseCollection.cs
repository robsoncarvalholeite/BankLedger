using Xunit;

namespace BankLedger.Infrastructure.Tests;

[CollectionDefinition("DatabaseCollection", DisableParallelization = true)]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
}

public class DatabaseFixture : IDisposable
{
    public void Dispose() { }
}