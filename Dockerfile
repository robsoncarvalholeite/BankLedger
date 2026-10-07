FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BankLedger.slnx .
COPY Directory.Build.props .
COPY src/BankLedger.Domain/BankLedger.Domain.csproj src/BankLedger.Domain/
COPY src/BankLedger.Application/BankLedger.Application.csproj src/BankLedger.Application/
COPY src/BankLedger.Infrastructure/BankLedger.Infrastructure.csproj src/BankLedger.Infrastructure/
COPY src/BankLedger.Api/BankLedger.Api.csproj src/BankLedger.Api/
COPY tests/BankLedger.Domain.Tests/BankLedger.Domain.Tests.csproj tests/BankLedger.Domain.Tests/
COPY tests/BankLedger.Application.Tests/BankLedger.Application.Tests.csproj tests/BankLedger.Application.Tests/
COPY tests/BankLedger.Infrastructure.Tests/BankLedger.Infrastructure.Tests.csproj tests/BankLedger.Infrastructure.Tests/

RUN dotnet restore BankLedger.slnx

COPY . .
RUN dotnet publish src/BankLedger.Api/BankLedger.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "BankLedger.Api.dll"]