var builder = DistributedApplication.CreateBuilder(args);

// Credenciais centralizadas no AppHost (user-secrets em dev / variáveis de ambiente em prod).
// Lidas da configuração em Parameters:<nome>.
var anthropicApiKey = builder.AddParameter("anthropic-apikey", secret: true);
var gmailClientId = builder.AddParameter("gmail-clientid", secret: true);
var gmailClientSecret = builder.AddParameter("gmail-clientsecret", secret: true);
var gmailRefreshToken = builder.AddParameter("gmail-refreshtoken", secret: true);
var pdfPassword = builder.AddParameter("pdf-password", secret: true);

// Segredo compartilhado (a "senha entre os workers"): a Evolution só aceita chamadas com este
// valor no header "apikey", e o Services usa o mesmo pra assinar os envios. Injetado nos dois lados
// pelo AppHost, então nunca sai de sincronia. Defina em user-secrets (dev) / env var (não usado aqui,
// pois a Evolution só sobe no dev via Aspire — em produção o docker-compose usa EVOLUTION_API_KEY).
var evolutionApiKey = builder.AddParameter("evolution-apikey", secret: true);

// PID do AppHost: cada processo filho monitora esse PID e se encerra junto (ver ServiceDefaults).
var appHostPid = Environment.ProcessId.ToString();

// WhatsApp (só dev local; em produção a Evolution roda pelo docker-compose da VPS).
// Postgres é obrigatório pela Evolution v2; o Aspire gera e injeta a senha sozinho. Redis é
// dispensado (1 usuário, envio simples → cache local basta).
var evolutionPostgres = builder.AddPostgres("evolution-postgres")
    .WithDataVolume("evolution-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);
var evolutionDb = evolutionPostgres.AddDatabase("evolution");

// A Evolution exige a connection string no formato URI (postgresql://user:pass@host:porta/db),
// diferente do formato Npgsql do Aspire — montada aqui a partir do recurso do Postgres.
var evolutionDbUri = ReferenceExpression.Create(
    $"postgresql://postgres:{evolutionPostgres.Resource.PasswordParameter}@{evolutionPostgres.Resource.Host}:{evolutionPostgres.Resource.Port}/evolution?schema=public");

var evolution = builder.AddContainer("evolution-api", "evoapicloud/evolution-api", "v2.3.7")
    .WithHttpEndpoint(targetPort: 8080, name: "http")
    .WithVolume("evolution-instances", "/evolution/instances")
    .WithEnvironment("AUTHENTICATION_API_KEY", evolutionApiKey)
    .WithEnvironment("DATABASE_ENABLED", "true")
    .WithEnvironment("DATABASE_PROVIDER", "postgresql")
    .WithEnvironment("DATABASE_CONNECTION_URI", evolutionDbUri)
    .WithEnvironment("DATABASE_CONNECTION_CLIENT_NAME", "evolution")
    .WithEnvironment("DATABASE_SAVE_DATA_INSTANCE", "true")
    .WithEnvironment("CACHE_REDIS_ENABLED", "false")
    .WithEnvironment("CACHE_LOCAL_ENABLED", "true")
    .WaitFor(evolutionDb);

// SERVER_URL = o próprio endpoint (a Evolution usa pra montar URLs de QR code/mídia).
evolution.WithEnvironment("SERVER_URL", evolution.GetEndpoint("http"));

// Webhook global de entrada: a Evolution POSTa MESSAGES_UPSERT no WebApp quando chega mensagem no grupo
// (importação de boletos). Do container, o host é "host.docker.internal"; a porta 5078 é fixa no launchSettings
// do WebApp. O token na URL é a própria ApiKey global (validada pelo endpoint), então nada de segredo novo.
var webhookUrl = ReferenceExpression.Create(
    $"http://host.docker.internal:5078/webhooks/evolution/{evolutionApiKey.Resource}");
evolution.WithEnvironment("WEBHOOK_GLOBAL_ENABLED", "true");
evolution.WithEnvironment("WEBHOOK_GLOBAL_URL", webhookUrl);
evolution.WithEnvironment("WEBHOOK_EVENTS_MESSAGES_UPSERT", "true");

// Front-end Blazor. Roda o agente no próprio processo, então também recebe as credenciais.
// O alerta de vencimentos por WhatsApp vive aqui (o WebApp tem o banco de faturas e é o que roda
// em produção), então recebe o endpoint da Evolution + o segredo compartilhado.
builder.AddProject<Projects.WebApp>("webapp")
    .WithExternalHttpEndpoints()
    .WithEnvironment("APPHOST_PID", appHostPid)
    .WithEnvironment("Agente__ApiKey", anthropicApiKey)
    .WithEnvironment("Gmail__ClientId", gmailClientId)
    .WithEnvironment("Gmail__ClientSecret", gmailClientSecret)
    .WithEnvironment("Gmail__RefreshToken", gmailRefreshToken)
    .WithEnvironment("Pdf__Password", pdfPassword)
    // WhatsApp: BaseUrl resolve pro endpoint da Evolution; ApiKey é o segredo compartilhado.
    .WithEnvironment("Evolution__BaseUrl", evolution.GetEndpoint("http"))
    .WithEnvironment("Evolution__ApiKey", evolutionApiKey)
    .WaitFor(evolution);

builder.Build().Run();
