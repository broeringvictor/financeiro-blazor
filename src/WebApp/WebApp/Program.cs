using Financeiro.ServiceDefaults;
using Services;
using Services.WhatsApp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using WebApp.Components;
using WebApp.Components.Account;
using WebApp.Data;
using WebApp.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Agente de IA (Anthropic + tools do Gmail) executado no próprio processo do WebApp.
builder.Services.AddContaAgent(builder.Configuration);

// Ingestão/pagamento de faturas (usa o DbContext com o usuário autenticado).
builder.Services.AddScoped<WebApp.Services.IngestaoFaturaService>();
builder.Services.AddScoped<WebApp.Services.BuscaFaturaOrchestrator>();

// Gestão de categorias (árvore principal → subcategoria, CRUD e seed/backfill dos padrões).
builder.Services.AddScoped<WebApp.Services.CategoryService>();

// Agregações do dashboard (saldo, gastos por mês, previsão de gastos).
builder.Services.AddScoped<WebApp.Services.FinanceSummaryService>();

// Busca automática diária das contas com AutoSearch=true (seção "AutoSearchFaturas").
var autoSearchFaturasOptions = builder.Configuration
    .GetSection("AutoSearchFaturas")
    .Get<WebApp.Services.AutoSearchFaturasOptions>() ?? new WebApp.Services.AutoSearchFaturasOptions();
builder.Services.AddSingleton(autoSearchFaturasOptions);
builder.Services.AddHostedService<WebApp.Services.AutoSearchFaturasWorker>();

// Geração de faturas em aberto a partir da recorrência da conta (independe de e-mail/boleto; seção "GeracaoFaturas").
builder.Services.AddScoped<WebApp.Services.GeracaoFaturaService>();
var geracaoFaturasOptions = builder.Configuration
    .GetSection("GeracaoFaturas")
    .Get<WebApp.Services.GeracaoFaturaOptions>() ?? new WebApp.Services.GeracaoFaturaOptions();
builder.Services.AddSingleton(geracaoFaturasOptions);
builder.Services.AddHostedService<WebApp.Services.GeracaoFaturasWorker>();

// Alerta diário de vencimentos por WhatsApp (cliente Evolution + serviço + worker; seção "VencimentoAlerta").
builder.Services.AddEvolutionWhatsApp(builder.Configuration);
builder.Services.AddScoped<WebApp.Services.VencimentoAlertaService>();
var vencimentoAlertaOptions = builder.Configuration
    .GetSection("VencimentoAlerta")
    .Get<WebApp.Services.VencimentoAlertaOptions>() ?? new WebApp.Services.VencimentoAlertaOptions();
builder.Services.AddSingleton(vencimentoAlertaOptions);
builder.Services.AddHostedService<WebApp.Services.VencimentoAlertaWorker>();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    // App de uso pessoal, sem SMTP: cadastro simples, sem confirmação de e-mail nem redefinição por e-mail.
    options.SignIn.RequireConfirmedAccount = false;
    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Data Protection: chaves que assinam os cookies de autenticação e os tokens antiforgery.
// Sem persistência, cada restart/redeploy do container gera chaves novas e invalida os cookies
// já emitidos ("An exception was thrown while deserializing the token"). Em prod, aponta para o
// volume persistente (DataProtection:KeysDirectory=/app/data/keys); em dev fica sem valor e usa
// o default efêmero, sem configuração extra. ApplicationName fixo mantém o ring estável entre deploys.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Financeiro");
var keysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(keysDirectory))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
}

var app = builder.Build();

// Aplica migrations pendentes no start. App pessoal, instância única — sem risco de corrida
// entre processos concorrentes aplicando o schema ao mesmo tempo.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();

    // Seed das categorias padrão + backfill das transações/contas legadas (enum → CategoryId).
    await scope.ServiceProvider.GetRequiredService<WebApp.Services.CategoryService>().SeedAllUsersAsync();
}

app.MapDefaultEndpoints();

// Atrás de um reverse proxy (Caddy) que termina o TLS: sem isso, UseHttpsRedirection/UseHsts
// veem a requisição como HTTP. Só aplicado fora de Development, pra não afetar o AppHost local
// (que fala http diretamente com o processo, sem proxy no meio).
if (!app.Environment.IsDevelopment())
{
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    forwardedHeadersOptions.KnownIPNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeadersOptions);
}

// Cultura pt-BR fixa (separador decimal vírgula, R$, datas dd/MM) independente do locale do servidor.
var supportedCultures = new[] { "pt-BR" };
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("pt-BR")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WebApp.Client._Imports).Assembly);

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.MapFaturaEndpoints();

// Só em dev: dispara o alerta de vencimentos na hora, sem esperar o horário diário (teste local).
// POST http://localhost:<porta>/dev/alertas/vencimentos
if (app.Environment.IsDevelopment())
{
    app.MapPost("/dev/alertas/vencimentos",
        async (WebApp.Services.VencimentoAlertaService svc, CancellationToken ct) =>
            Results.Ok(await svc.EnviarAsync(ct)));
}

app.Run();
