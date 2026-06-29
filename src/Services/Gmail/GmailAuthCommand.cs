using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.UserSecrets;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Util.Store;

namespace Services.Gmail;

/// <summary>
/// Modo interativo de autenticação (executado uma única vez com `dotnet run -- --auth`).
/// Abre o navegador, obtém o consentimento e grava/sobrescreve o refresh token
/// diretamente nos user-secrets (Gmail:RefreshToken).
/// </summary>
public static class GmailAuthCommand
{
    public static async Task RunAsync(GmailOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            Console.WriteLine("ERRO: configure Gmail:ClientId e Gmail:ClientSecret nos user-secrets antes de rodar --auth.");
            return;
        }

        // Diretório temporário e único: força um novo consentimento e garante refresh token.
        var store = Path.Combine(Path.GetTempPath(), "financeiro-gmail-auth-" + Guid.NewGuid().ToString("N"));

        Console.WriteLine("Abrindo o navegador para autorizar o acesso ao Gmail (somente leitura)...");

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = options.ClientId, ClientSecret = options.ClientSecret },
            [GmailService.Scope.GmailReadonly],
            "user",
            CancellationToken.None,
            new FileDataStore(store, true));

        var refreshToken = credential.Token.RefreshToken;

        // Limpa o store temporário — o token fica nos user-secrets, não em disco solto.
        try { Directory.Delete(store, recursive: true); } catch { /* ignore */ }

        Console.WriteLine();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            Console.WriteLine("Não veio refresh token. Tente novamente (revogue o acesso anterior em https://myaccount.google.com/permissions).");
            return;
        }

        SalvarRefreshTokenNosSecrets(refreshToken);
    }

    /// <summary>
    /// Grava (sobrescreve) Gmail:RefreshToken no secrets.json do UserSecretsId do assembly.
    /// </summary>
    private static void SalvarRefreshTokenNosSecrets(string refreshToken)
    {
        var userSecretsId = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;

        if (string.IsNullOrWhiteSpace(userSecretsId))
        {
            Console.WriteLine("Não encontrei o UserSecretsId. Salve manualmente:");
            Console.WriteLine($"  dotnet user-secrets --project src/Services/Services.csproj set \"Gmail:RefreshToken\" \"{refreshToken}\"");
            return;
        }

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", userSecretsId, "secrets.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        JsonObject root;
        if (File.Exists(path))
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        // Sobrescreve a chave (formato plano usado pelo dotnet user-secrets).
        root["Gmail:RefreshToken"] = refreshToken;

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Refresh token gravado em Gmail:RefreshToken ({path}).");
    }
}
