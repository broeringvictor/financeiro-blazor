var builder = DistributedApplication.CreateBuilder(args);

// Credenciais centralizadas no AppHost (user-secrets em dev / variáveis de ambiente em prod).
// Lidas da configuração em Parameters:<nome>.
var anthropicApiKey = builder.AddParameter("anthropic-apikey", secret: true);
var gmailClientId = builder.AddParameter("gmail-clientid", secret: true);
var gmailClientSecret = builder.AddParameter("gmail-clientsecret", secret: true);
var gmailRefreshToken = builder.AddParameter("gmail-refreshtoken", secret: true);
var pdfPassword = builder.AddParameter("pdf-password", secret: true);

// PID do AppHost: cada processo filho monitora esse PID e se encerra junto (ver ServiceDefaults).
var appHostPid = Environment.ProcessId.ToString();

// Worker em background (varredura periódica de faturas). Sem endpoints HTTP.
// As env vars com "__" sobrescrevem a configuração (Agente:ApiKey, Gmail:ClientId, ...).
builder.AddProject<Projects.Services>("services")
    .WithEnvironment("APPHOST_PID", appHostPid)
    .WithEnvironment("Agente__ApiKey", anthropicApiKey)
    .WithEnvironment("Gmail__ClientId", gmailClientId)
    .WithEnvironment("Gmail__ClientSecret", gmailClientSecret)
    .WithEnvironment("Gmail__RefreshToken", gmailRefreshToken)
    .WithEnvironment("Pdf__Password", pdfPassword);

// Front-end Blazor. Roda o agente no próprio processo, então também recebe as credenciais.
builder.AddProject<Projects.WebApp>("webapp")
    .WithExternalHttpEndpoints()
    .WithEnvironment("APPHOST_PID", appHostPid)
    .WithEnvironment("Agente__ApiKey", anthropicApiKey)
    .WithEnvironment("Gmail__ClientId", gmailClientId)
    .WithEnvironment("Gmail__ClientSecret", gmailClientSecret)
    .WithEnvironment("Gmail__RefreshToken", gmailRefreshToken)
    .WithEnvironment("Pdf__Password", pdfPassword);

builder.Build().Run();
