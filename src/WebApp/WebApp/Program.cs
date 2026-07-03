using Financeiro.ServiceDefaults;
using Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using WebApp.Components;
using WebApp.Components.Account;
using WebApp.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Agente de IA (Anthropic + tools do Gmail) executado no próprio processo do WebApp.
builder.Services.AddContaAgent(builder.Configuration);

// Ingestão/pagamento de faturas (usa o DbContext com o usuário autenticado).
builder.Services.AddScoped<WebApp.Services.IngestaoFaturaService>();
builder.Services.AddScoped<WebApp.Services.BuscaFaturaOrchestrator>();

// Agregações do dashboard (saldo, gastos por mês, previsão de gastos).
builder.Services.AddScoped<WebApp.Services.FinanceSummaryService>();

// Busca automática diária das contas com AutoSearch=true (seção "AutoSearchFaturas").
var autoSearchFaturasOptions = builder.Configuration
    .GetSection("AutoSearchFaturas")
    .Get<WebApp.Services.AutoSearchFaturasOptions>() ?? new WebApp.Services.AutoSearchFaturasOptions();
builder.Services.AddSingleton(autoSearchFaturasOptions);
builder.Services.AddHostedService<WebApp.Services.AutoSearchFaturasWorker>();

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

var app = builder.Build();

// Aplica migrations pendentes no start. App pessoal, instância única — sem risco de corrida
// entre processos concorrentes aplicando o schema ao mesmo tempo.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
}

app.MapDefaultEndpoints();

// Atrás de um reverse proxy (Caddy) que termina o TLS: sem isso, UseHttpsRedirection/UseHsts e o
// redirect de login do Blazor (RedirectToLogin.razor -> NavigationManager) veem a requisição como
// HTTP e geram links absolutos errados. Confia no hop imediato (o proxy não é exposto direto).
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// ForwardedHeaders sozinho não é suficiente: o NavigationManager do Blazor (usado por
// RedirectToLogin.razor) constrói a URL absoluta do redirect por um caminho interno que não
// reflete o Request.Scheme já corrigido acima. Como todo tráfego real chega via Caddy com TLS
// (nunca http puro), força o scheme de forma incondicional logo na entrada do pipeline.
app.Use(async (context, next) =>
{
    context.Request.Scheme = "https";
    await next();
});

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

app.Run();
