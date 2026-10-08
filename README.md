# BankLedger

Sistema de ledger financeiro para registro de movimentações financeiras, construído com Ports & Adapters (arquitetura hexagonal) em C# / .NET 10. **Leia os [Requisitos](docs/REQUIREMENTS.md)**
> Prova de Conceito (PoC) para fundamentação de conceitos e decisões técnicas visando uma possível evolução futura.

## Visão Geral

API REST para registro de transações financeiras (créditos/débitos) e consulta de saldo consolidado com suporte a:
- **Idempotência** via header `Idempotency-Key` evitando lançamentos duplos (Res. **BCB 2/2020** e Res. **CMN 4.966/2021**)
- **Concorrência segura** com controle otimista (OCC) e transações atômicas
- **Snapshots periódicos** para performance de consulta de saldo
- **Saldo histórico**
- **Arquitetura hexagonal** (Domain/Application/Infrastructure/API)
- **100% em cobertura de testes** Test Coverage de 100% sobre todas as regras de negócio da aplicação.

## Tecnologias

| Camada | Tecnologia |
|--------|------------|
| Runtime | .NET 10 |
| Web Framework | ASP.NET Core 10 |
| ORM/Data Access | Dapper |
| Database | SQLite (dev)|
| Testes | xUnit, Moq |
| Documentação | Swagger/OpenAPI |
| Containerização | Docker, Docker Compose |

## Arquitetura

```
src/
├── BankLedger.Api/           # Adaptador HTTP (Controllers, Middleware)
├── BankLedger.Application/   # Casos de uso, DTOs, Ports
├── BankLedger.Domain/        # Regras de negócio (Money, Transaction, BalanceSnapshot)
└── BankLedger.Infrastructure/ # Adapters (Dsatabase/Dapper, BackgroundService)
```

### Princípios Tecnologias
- **Domain** sem dependências externas (zero infra, apenas negócios desacomplados)
- **Ports & Adapters**: Domain define interfaces, Infrastructure implementa
- **Money** como Value Object imutável (decimal + Banker's Rounding)
- **Idempotência** no nível de banco (constraint UNIQUE) + aplicação
- **OCC** para snapshots com retry automático

## Pré-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/) e [Docker Compose](https://docs.docker.com/compose/)

## Setup

### Com Docker Compose (Recomendado)
```bash
docker compose up --build
```
API disponível em `http://localhost:8080`

### Local (sem Docker)
```bash
dotnet restore
dotnet build --configuration Release
dotnet run --project src/BankLedger.Api
```
API em `http://localhost:5091` (porta aleatória) ou conforme `launchSettings.json`

---

## Variáveis de Ambiente

| Variável | Padrão | Descrição |
|----------|--------|-----------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Ambiente ASP.NET Core |
| `SNAPSHOT_INTERVAL_MINUTES` | `5` | Intervalo para geração dos snapshots das contas |
| `SQLITE_CONNECTION_STRING` | `Data Source=bank.db` | Connection string SQLite |
| `ASPNETCORE_URLS` | `http://+:8080` | URLs de binding |

---

## Executando Testes

```bash
# Todos os testes
dotnet test --configuration Release

```
---

## API Reference

### Autenticação
Header `Authorization` com número da conta (simbólico, substituir por JWT/OAuth2 em produção):
```
Authorization: 123456
```

### Endpoints

#### Cria transação (crédito ou débito). Requer idempotência.
```
POST /transactions
```

**Headers**
```
Authorization: 123456
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
Content-Type: application/json
```

**Body**
```json
{
  "amount": 100.50,
  "type": "CREDIT"
}
```

**Responses**
- `201 Created` - Transação criada
- `400` - Validação falhou
- `401` - Authorization header ausente
- `422` - Saldo insuficiente (débito)
- `409` - Conflito de concorrência

#### GET /balances
Saldo consolidado da conta autenticada.

**Headers**
```
Authorization: 123456
```

```
GET /balances
```

**Response**
```json
{
  "accountNumber": "123456",
  "balance": 1250.50,
  "asOf": "2026-01-15T10:30:00.000Z"
}
```

### Swagger UI
```
http://localhost:8080/swagger
```

---

## Exemplos cURL

### Crédito
```bash
curl -X POST http://localhost:8080/transactions \
  -H "Authorization: 123456" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{"amount": 100.50, "type": "CREDIT"}'
```

### Débito
```bash
curl -X POST http://localhost:8080/transactions \
  -H "Authorization: 123456" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{"amount": 50.00, "type": "DEBIT"}'
```

### Saldo Atual
```bash
curl -H "Authorization: 123456" http://localhost:8080/balances
```

### Saldo Histórico
```bash
curl -H "Authorization: 123456" \
  "http://localhost:8080/balances"
```

### Testar Idempotência
```bash
KEY="550e8400-e29b-41d4-a716-446655440000"
curl -X POST http://localhost:8080/transactions \
  -H "Authorization: 123456" \
  -H "Idempotency-Key: $KEY" \
  -H "Content-Type: application/json" \
  -d '{"amount": 100.00, "type": "CREDIT"}'

# Repetir - retorna mesma transação
curl -X POST http://localhost:8080/transactions \
  -H "Authorization: 123456" \
  -H "Idempotency-Key: $KEY" \
  -H "Content-Type: application/json" \
  -d '{"amount": 200.00, "type": "CREDIT"}'
```

---

## Documentação

- [Arquitetura](docs/ARCHITECTURE.md) - Hexagonal, domínio, infra, trade-offs
- [API Reference](docs/API.md) - Endpoints, headers, códigos, exemplos
- [Roadmap](docs/ROADMAP.md) - Próximos passos e decisões de arquitetura com mais detalhes

---

## Decisões Arquiteturais
Toda decisão arquitetural, deve começar com análise dos requisitos funcionais (o que deve ser feito) e não funcionais (como deverá ser feito). Nesse ponto é comum o levantamentos de algumas informações como:
- Quais as tecnologias mais familiares ao time/projeto/empresa?
- Quantas contas serão atendidas?
- Volume médio de transações por dia/semana/mês?
- Proporção de escrita vs leitura?
- Existem plano de expansão?
- Quais informações são necessárias em cada entidade? (ex: Status, external-id, multi-moedas)
- Como será a Auditoria?
- Quais os demais tipos de transações teremos? (ex: PIX, Taxas, Estorno, Bloqueios)
- API será exporta a internet ou apenas interna?
E muito mais.

Portanto, por se tratar de uma POC com poucas horas de implementação, vamos algumas decisões e os motivos.
Para mais detalhes de até onde poderíamos chegar com prazo, equipe e orçamento em um sistema de produção real de missão crítica (High Availability), leia o documento de [Roadmap](docs/ROADMAP.md)


| Decisão | Justificativa |
|---------|---------------|
| **SQLite** | Pelo pouco de horas de implementação e agilidade na entrega de valor dessa POC |
| **Dapper** | Performance, simplicidade de setup. Mas poderia ser outro ORM |
| **Money** | Travalhar com datas, ponto flutuante e moeda na computação será sempre um problema, criar o Money ajuda a centralizar decisões de operadores, arredondamento bancário e outros. Fazendo uso de DRY (Don't Repeat Yourself) |
| **Banker's Rounding** | Padrão financeiro |
| **Snapshot assíncrono** | Baixo custo para o banco; melhora a performance, e é uma iniciativa simplificada de CQRS |
| **BackgroundService** | Simplicidade da implementação |
| **Auth simbólico** | Em uma aplicação real, usaria JWT/JWS, mTLS ou OAuth2. Mas nessa PoC, escolhi um "token simbólico" |
| **OCC-Version** | Uma forma simples de evitar concorrência por **Optimistic Concurrency Control**. Em sistemas com muita concorrência, essa estratégia pode chegar um overhead de degradação exponencial (O(n²))
| **Lock/Redis** | As implementações do `IdempotencyLock` e `IConcurrencyStore` foram feitas em memória, porém em uma aplicação real, com escala horizontal (mais instancias/pods) é necessário distribuir esses locks e store. Nesse é recomendado o uso de Redis em Cluster ou MemGrid |

---

## Estrutura do Repositório

```
.
├── docs/
│   ├── ARCHITECTURE.md
│   ├── CONCURRENCY.md
│   ├── API.md
│   └── ROADMAP.md
├── src/
│   ├── BankLedger.Api/
│   ├── BankLedger.Application/
│   ├── BankLedger.Domain/
│   └── BankLedger.Infrastructure/
├── tests/
│   ├── BankLedger.Domain.Tests/
│   ├── BankLedger.Application.Tests/
│   └── BankLedger.Infrastructure.Tests/
├── Dockerfile
├── docker-compose.yml
├── .gitignore
├── Directory.Build.props
├── BankLedger.sln
└── README.md
```
