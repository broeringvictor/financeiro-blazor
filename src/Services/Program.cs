using Financeiro.ServiceDefaults;
using Services.Gmail;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var gmailOptions = builder.Configuration.GetSection("Gmail").Get<GmailOptions>() ?? new GmailOptions();

// Único uso deste executável: gerar o refresh token do Gmail (OAuth) e encerrar
// (`dotnet run --project src/Services -- --auth`). A varredura de faturas roda no WebApp
// (AutoSearchFaturasWorker); o restante deste projeto (agente, Gmail, PDF, WhatsApp) é
// biblioteca compartilhada consumida pelo WebApp.
if (args.Contains("--auth"))
{
    await GmailAuthCommand.RunAsync(gmailOptions);
    return;
}

Console.WriteLine(
    "Projeto Services: biblioteca compartilhada do WebApp. Use '--auth' para gerar o refresh token do Gmail.");
