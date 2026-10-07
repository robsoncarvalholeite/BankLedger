using BankLedger.Application.Ports;
using BankLedger.Application.UseCases;
using BankLedger.Infrastructure.Persistence;
using BankLedger.Infrastructure.Jobs;
using BankLedger.Api.Middleware;
using BankLedger.Api.Filters;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ValidationFilter>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=bank.db";

builder.Services.AddSingleton<SqliteConnectionFactory>(_ => new SqliteConnectionFactory(connectionString));
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<ISnapshotRepository, SnapshotRepository>();
builder.Services.AddScoped<CreateTransactionUseCase>();
builder.Services.AddScoped<GetBalanceUseCase>();
builder.Services.AddHostedService<SnapshotBackgroundService>();

using (var connection = new SqliteConnection(connectionString))
{
    connection.Open();
    DatabaseInitializer.Initialize(connection);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAccountAuthorization();
app.MapControllers();

app.Run();