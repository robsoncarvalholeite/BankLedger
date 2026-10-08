# Boas Práticas de engenharia, arquitetura financeira e regulatório

> Este documento lista boas práticas de mercado (engenharia, arquitetura bancária e regulatório) que **conscientemente não entram nesta versão**, com a justificativa da estratégia adotada no lugar. A ordem aproxima a prioridade sugerida para as próximas versões.
>

## Como ler

- **Prática**: o que o mercado recomenda.
- **Por que não agora**: o trade-off assumido nesta POC.
- **O que fizemos no lugar**: a decisão que mantém o caminho de evolução aberto.
- **Gatilho**: sinal de que chegou a hora de adotar.

---

## 1. Mensageria / broker (eventos de domínio, outbox, filas)

- **Prática**: publicar `TransactionRegistered` em um broker (Kafka, RabbitMQ, SQS) via *Transactional Outbox*, desacoplando o cálculo de snapshot, notificações, antifraude e contabilidade. Consumidores idempotentes, DLQ, *retry* com backoff.
- **Por que não agora**: um broker adiciona infraestrutura (cluster, monitoramento, semântica *at-least-once*) que não muda a correção do núcleo e dificulta o "subir com 1 comando". A premissa da POC é isolar regras de negócio e provar consistência do ledger antes de distribuir.
- **O que fizemos no lugar**: job `Snapshot` in-process, lendo apenas contas com movimentação pendente. Os casos de uso retornam resultados que podem virar eventos sem alterar o domínio; a tabela `transactions` já funciona como *log* ordenado por `sequence`.
- **Importância**: em produção, o broker é o que permite **operar sob instabilidade e falhas parciais**: a API grava e responde; o restante acontece de forma assíncrona e reprocessável. Também é o caminho natural para integrar antifraude (Res. Conjunta 6/2023) e contabilidade (COSIF).

## 2. PostgreSQL/Cassandra (ou outro Banco de Dados) em vez de SQLite

- **Prática**: banco servidor, sharding, réplicas de leitura/escrita, backup PITR, `SELECT ... FOR UPDATE`, *advisory locks*, particionamento por data ou hash de contas.
- **Por que não agora**: SQLite elimina dependência externa nos testes e no setup. O que importa provar é a lógica de consistência, e ela foi escrita **sem depender do SQLite**: OCC Lock por `occ-version`, SQL simples.
- **Gatilho**: mais de uma instância da API, volume de escrita acima de algumas centenas de TPS, ou exigência de replicação/backup contínuo (Res. CMN 4.557/2017 art. 20, continuidade).

## 3. Entity Framework Core

- **Prática**: ORM completo com *migrations* tipadas, evolução do banco de dados de forma versionada e auditavel (*change tracking*).
- **Por que não agora**: premissa de simplicidade (Dapper). Em um ledger, o SQL explícito deixa visível.
- **O que fizemos no lugar**: migrações SQL embarcadas e aplicadas no startup; DTOs de linha mapeados manualmente.
- **Gatilho**: crescimento do modelo (dezenas de tabelas), vários times atuando na mesma estrutura de persistência.

## 4. Partidas dobradas (double-entry) e integração contábil (COSIF)

- **Prática**: todo movimento gera lançamento com débito em uma conta e crédito em outra (ex.: conta do cliente vs. conta transitória/caixa), com soma zero garantida; mapeamento para plano de contas COSIF (Res. BCB 2/2020; Res. CMN 4.966/2021).
- **Por que não agora**: o desafio pede registro por conta de cliente e saldo; a contraparte contábil exige plano de contas e eventos externos (liquidação, tarifas) fora do escopo.
- **O que fizemos no lugar**: *single-entry* imutável por conta no domínio, que viram naturalmente uma "perna" do lançamento duplo.
- **Gatilho**: necessidade de fechamento contábil, conciliação com sistemas de liquidação e relatórios eviados ao BCB e Fazenda com base na **Resolução BCB nº 2/2020**

## 5. Event Sourcing / CQRS formal com read model separado

- **Prática**: eventos como fonte de verdade, projeções assíncronas, *read models* otimizados, isolando Command de Query.
- **Por que não agora**: o ledger append-only já é, na prática, um *event store* mínimo; projeções assíncronas dependem de mensageria (item 1).
- **O que fizemos no lugar**: snapshot de saldo como projeção simples e reconstruível a partir das transações.
- **Gatilho**: múltiplas visões (extrato, limites, relatórios) com SLAs de leitura distintos.

## 6. Autenticação e autorização reais (OAuth2/OIDC, JWT, mTLS)

- **Prática**: tokens assinados emitidos por um IdP, *scopes*, rotação de chaves, mTLS entre serviços, certificados ICP-Brasil onde exigido; segurança de APIs conforme Res. CMN 5.274/2025 e Res. BCB 538/2025.
- **Por que não agora**: premissa explícita de token simbólico (número da conta) para focar no ledger.
- **O que fizemos no lugar**: middleware único de autenticação/autorização com `Authorization:`.
- **Gatilho**: qualquer exposição além de ambiente local.

## 7. TLS, rate limiting, WAF e hardening de rede

- **Prática**: Implementação de um APIGateway com HTTPS obrigatório com HSTS, *rate limiting* por token/IP, WAF, segregação de rede, gestão de certificados (Res. CMN 5.274/2025).
- **Por que não agora**: em POC local o TLS é terminado por proxy/ingress; rate limiting sem identidade real protege pouco.
- **Gatilho**: Segurança com deploy em ambiente cloud com Kubernets, Ingress e etc.

## 8. Observabilidade completa (OpenTelemetry, métricas, tracing distribuído, Serilog/Seq)

- **Prática**: OTLP para traces/metrics/logs, APN, dashboards de latência p95/p99, alertas de erro, *sampling*.
- **Por que não agora**: por ter poucas horas para implementar o desafio e documentar tudo.
- **Gatilho**: Desde início em produção é necessário telemetria e acompanhamento da saúde da aplicação.

## 9. Extrato paginado, filtros e identificação de beneficiário

- **Prática**: `GET /accounts/{n}/transactions?from&to&cursor`, com contraparte identificada em cada linha (Res. CMN 4.949/2021 art. 4º, V).

## 10. Data de efetivação ≠ data de registro (lançamentos retroativos) e fuso contábil

- **Prática**: distinguir `effective_date` (competência) de `posted_at` (registro), com saldo por data contábil em demais fusos e tratamento de dias não úteis.
- **Por que não agora**: Por aumentar complexidade na aplicação, sem a necessidade real.
- **Gatilho**: Deve ser analizado nos requistos funcionais a necessidade real da aplicação.

## 11. Estorno / reversão e novos tipos de transação

- **Prática**: nunca apagar; estornar com lançamento inverso vinculado ao original, tipos `FEE`, `INTEREST`, `TRANSFER_IN/OUT`.
- **Por que não agora**: Para simplicidade do projeto.
- **Gatilho**: primeira necessidade de corrigir lançamento em produção.

## 12. Expiração e limpeza de chaves de idempotência

- **Prática**: TTL (12h–7d) com job de limpeza; cabeçalho `Idempotency-Key`
- **Por que não agora**: volume da POC não justifica.
- **Gatilho**: crescimento do cache ou lock ou política de retenção definida.

## 13. Backups, retenção de 10 anos e arquivamento

- **Prática**: backup contínuo, testes de restauração, retenção mínima de 10 anos dos registros de operações (Circular BCB 3.978/2020 art. 67), arquivamento frio com integridade verificável (hash encadeado).
- **Por que não agora**: POC local; SQLite sem backup
- **Gatilho**: Por ser demanda legal, é necessário resolver antes de produção.

## 14. Criptografia em repouso e gestão de segredos

- **Prática**: disco/volume criptografado; segredos em cofre (Vault, AWS Secrets Manager, Azure Key Vault) para rotatividade de secrets.
- **Por que não agora**: não há segredos reais na POC; `.env.example` é o único arquivo versionado.

## 15. Pipeline completo de qualidade e segurança (SAST, dependabot, mutation testing, load test)

- **Prática**: Quality-Gate, SonnarQube, WASP/SAST, verificação de vulnerabilidades em dependências (`dotnet list package --vulnerable`), Stryker.NET (mutação), k6 para carga, pentest periódico.
> **Resolução CMN 4.968/2021 art. 5º, IV:** _testes periódicos de segurança para os sistemas de informações e de tecnologia_
- **Por que não agora**: o CI mínimo (build sem warnings, testes, cobertura 100% do core) é o que o desafio exige; o restante é incremental.
- **Gatilho**: antes do primeiro deploy fora do ambiente local.

## 16. Versionamento de API e contrato

- **Prática**: Api com `/v1`,  `/v2` implementada pelo Api Gateway, testes de contrato (Pact), política de depreciação.
- **Gatilho**: Os consumidores de API precisam da documentação e versionamento das APIs que consomem.

## 17. KYC/onboarding e ciclo de vida da conta

- **Prática**: identificação e qualificação do titular (Res. CMN 4.753/2019 art. 2º), status de conta (ativa, bloqueada, encerrada), limites por perfil, PLD/FT (Circular 3.978/2020).
- **Por que não agora**: No envio da primeira transação, a conta é criada, caso ela não exista.
- **Gatilho**: qualquer dado pessoal real (também aciona a LGPD: minimização, base legal, registro de tratamento).

## 18. Multi-moeda

- **Prática**: `Money` com `Currency` (ISO 4217) e regras de conversão.
- **Por que não agora**: o escopo é BRL; carregar moeda em todo valor adiciona ruído sem exercitar decisões relevantes.
- **O que fizemos no lugar**: `Money` encapsula escala e arredondamento; adicionar `Currency` é mudança local no VO e uma coluna.


# Conclusão

Muito mais poderia ser incorporado a este documento, mas, como mencionado, é necessário compreender melhor os requisitos funcionais e não funcionais da aplicação antes de definir uma arquitetura complexa. Evitar a otimização prematura e o (Overengineering)[https://en.wikipedia.org/wiki/Overengineering] é uma habilidade importante para todos os profissionais envolvidos no desenvolvimento, considerando que complexidade traz custos que vão além de tempo e dinheiro, como manutenção, operação, evolução e custo de oportunidade. 

```

"A simplicidade é o ponto máximo da sofisticação." — Leonardo da Vinci

```

**Eliminar o "desnecessário" para que o "necessário" possa aparecer**
