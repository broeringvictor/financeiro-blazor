using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;

namespace Services.Gmail;

/// <summary>
/// Configuração de acesso ao Gmail para execução headless (sem navegador).
/// ClientId, ClientSecret e RefreshToken devem vir de user-secrets / variáveis de
/// ambiente — nunca do appsettings.json versionado.
/// </summary>
public sealed class GmailOptions
{
    /// <summary>Nome da aplicação enviado para a API.</summary>
    public string ApplicationName { get; set; } = "Financeiro";

    /// <summary>Client ID do OAuth (Google Cloud Console).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client Secret do OAuth.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Refresh token obtido uma única vez no consentimento inicial.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Usuário alvo. "me" representa a conta autenticada.</summary>
    public string User { get; set; } = "me";

    /// <summary>
    /// Pasta temporária onde os anexos (PDFs de faturas) são salvos para posterior manipulação.
    /// </summary>
    public string DownloadDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "financeiro-faturas");
}

/// <summary>
/// Cria instâncias autenticadas de <see cref="GmailService"/> usando um refresh token.
/// O access token é renovado automaticamente, sem interação do usuário — adequado a headless.
/// </summary>
public sealed class GmailServiceFactory(GmailOptions options)
{
    public async Task<GmailService> CreateAsync(CancellationToken ct = default)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret,
            },
            // Escopo somente leitura — suficiente para buscar contas/faturas.
            Scopes = [GmailService.Scope.GmailReadonly],
        });

        // Troca o refresh token por um access token de imediato (sem interação).
        var token = await flow.RefreshTokenAsync(options.User, options.RefreshToken, ct);
        var credential = new UserCredential(flow, options.User, token);

        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = options.ApplicationName,
        });
    }
}
