using Financeiro.ServiceDefaults;
using Services;
using Services.Gmail;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var gmailOptions = builder.Configuration.GetSection("Gmail").Get<GmailOptions>() ?? new GmailOptions();

// Modo de autenticação único: gera o refresh token do Gmail e encerra.
if (args.Contains("--auth"))
{
    await GmailAuthCommand.RunAsync(gmailOptions);
    return;
}

builder.Services.AddContaAgent(builder.Configuration);

// Worker em background: varredura periódica de faturas (configurável na seção "Worker").
var scanOptions = builder.Configuration.GetSection("Worker").Get<BackgroundScanOptions>() ?? new BackgroundScanOptions();
builder.Services.AddSingleton(scanOptions);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
