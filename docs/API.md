# Documentação da API

## Base URL
```
http://localhost:8080
```

## Autenticação

Autenticação simbólica via header. Em produção, substituir por OAuth2/JWT.

| Header | Obrigatório | Descrição |
|--------|-------------|-----------|
| `Authorization` | Sim | Número da conta (ex: `123456`) |
| `Idempotency-Key` | Sim (POST /transactions) | UUID v4 para idempotência |

> **Nota**: Em produção, substituir por token JWT/OAuth2. O header `Authorization` atualmente aceita qualquer string não vazia como número da conta.

---

## Endpoints

### POST /transactions

Cria uma nova transação (crédito ou débito).

#### Headers
```
Authorization: 123456
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
Content-Type: application/json
```

#### Request Body
```json
{
  "amount": 100.50,
  "type": "CREDIT"
}
```

| Campo | Tipo | Obrigatório | Regras |
|-------|------|-------------|--------|
| `amount` | decimal | Sim | > 0, máx 2 casas decimais |
| `type` | string | Sim | `CREDIT` ou `DEBIT` |

#### Responses

**201 Created** - Transação criada
```json
{
  "id": 1,
  "accountNumber": "123456",
  "amount": 100.50,
  "type": "CREDIT",
  "createdAt": "2026-01-15T10:30:00.000Z",
  "idempotencyKey": "550e8400-e29b-41d4-a716-446655440000"
}
```

**400 Bad Request** - Validação falhou
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "amount": ["The field Amount must be between 0.01 and 999999999999999999."],
    "type": ["The JSON value could not be converted to TransactionType."]
  }
}
```

**401 Unauthorized** - Header Authorization ausente ou vazio
```text
Missing Authorization header
```

**403 Forbidden** - Acesso a conta não autorizada (não implementado, retorna 401)

**422 Unprocessable Entity** - Saldo insuficiente (débito)
```json
{
  "error": "Insufficient balance for account 123456. Requested: R$ 500,00, Available: R$ 100,50",
  "requested": 500.00,
  "available": 100.5
}
```

**409 Conflict** - Conflito de concorrência (raro)
```json
{
  "error": "Concurrency conflict, please retry"
}
```

---

### GET /balances

Consulta saldo consolidado da conta autenticada.

#### Headers
```
Authorization: 123456
```

#### Query Parameters
| Parâmetro | Tipo | Obrigatório | Descrição |
|-----------|------|-------------|-----------|
| `from` | datetime (ISO 8601) | Não | Saldo histórico até esta data |

#### Exemplos
```
GET /balances                    # Saldo atual
GET /balances?from=2026-01-01T00:00:00Z  # Saldo em 01/01/2026
```

#### Responses

**200 OK**
```json
{
  "accountNumber": "123456",
  "balance": 1250.50,
  "asOf": "2026-01-15T10:30:00.000Z"
}
```

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `accountNumber` | string | Número da conta |
| `balance` | decimal | Saldo consolidado |
| `asOf` | datetime | Data/hora do saldo (ou `from` se informado) |

**401 Unauthorized** - Header Authorization ausente
```text
Missing Authorization header
```

---

## Códigos de Status HTTP

| Código | Descrição |
|--------|-----------|
| 200 | Sucesso (GET /balances) |
| 201 | Criado (POST /transactions) |
| 400 | Requisição inválida (validação) |
| 401 | Não autenticado (header ausente) |
| 403 | Acesso proibido (não usado atualmente) |
| 422 | Entidade não processável (saldo insuficiente) |
| 409 | Conflito de concorrência |

---

## Exemplos Completos

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
curl -H "Authorization: 123456" \
  http://localhost:8080/balances
```

### Saldo Histórico
```bash
curl -H "Authorization: 123456" \
  "http://localhost:8080/balances?from=2026-01-01T00:00:00Z"
```

### Testar Idempotência
```bash
KEY="550e8400-e29b-41d4-a716-446655440000"
curl -X POST http://localhost:8080/transactions \
  -H "Authorization: 123456" \
  -H "Idempotency-Key: $KEY" \
  -H "Content-Type: application/json" \
  -d '{"amount": 100.00, "type": "CREDIT"}'

# Repetir mesma chave - deve retornar mesma transação
curl -X POST http://localhost:8080/transactions \
  -H "Authorization: 123456" \
  -H "Idempotency-Key: $KEY" \
  -H "Content-Type: application/json" \
  -d '{"amount": 200.00, "type": "CREDIT"}'
```

---

## Swagger / OpenAPI

Interface interativa: `http://localhost:8080/swagger`

OpenAPI JSON: `http://localhost:8080/swagger/v1/swagger.json`

---

## Códigos de Erro de Negócio

| Código | HTTP | Quando Ocorre |
|--------|------|---------------|
| `INSUFFICIENT_BALANCE` | 422 | Débito > saldo disponível |
| `DUPLICATE_IDEMPOTENCY_KEY` | 200 | Retorna transação existente (não é erro) |
| `CONCURRENCY_CONFLICT` | 409 | Conflito OCC em snapshot (retry automático) |

---

## Formato de Dados

### Money
- **Domínio**: `decimal` com 2 casas (Banker's Rounding)
- **API**: `number` (ex: `100.50`)
- **Banco**: `INTEGER` centavos (ex: `10050`)

### TransactionType
```json
"CREDIT"  // Entrada
"DEBIT"   // Saída
```

### DateTime
- **Formato**: ISO 8601 UTC (ex: `2026-01-15T10:30:00.000Z`)
- **Query `from`**: Aceita ISO 8601 com ou sem timezone