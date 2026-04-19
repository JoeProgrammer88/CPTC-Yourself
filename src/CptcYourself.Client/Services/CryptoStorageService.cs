using Microsoft.JSInterop;
using CptcYourself.Client.Models;
using System.Text.Json;

namespace CptcYourself.Client.Services;

public class CryptoStorageService(IJSRuntime js)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Encrypt the secret with password, persist to IndexedDB, return the record.</summary>
    public async Task<AppSettings> SaveApiKeyAsync(string secret, string password,
        ApiProvider provider = ApiProvider.AiStudio,
        string vertexProjectId = "", string vertexLocation = "us-central1")
    {
        var result = await js.InvokeAsync<JsonElement>("cptcInterop.saveApiKey",
            secret, password, provider.ToString(), vertexProjectId, vertexLocation);

        return new AppSettings
        {
            EncryptedApiKey = result.GetProperty("encryptedApiKey").GetString()!,
            Salt            = result.GetProperty("salt").GetString()!,
            Iv              = result.GetProperty("iv").GetString()!,
            Provider        = provider,
            VertexProjectId = vertexProjectId,
            VertexLocation  = vertexLocation
        };
    }

    /// <summary>Load the stored (encrypted) settings record, or null if nothing saved yet.</summary>
    public async Task<AppSettings?> LoadSettingsAsync()
    {
        var result = await js.InvokeAsync<JsonElement>("cptcInterop.loadSettings");
        if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
            return null;

        var providerStr = result.TryGetProperty("provider", out var pProp) ? pProp.GetString() : "AiStudio";
        var provider    = Enum.TryParse<ApiProvider>(providerStr, out var p) ? p : ApiProvider.AiStudio;

        return new AppSettings
        {
            EncryptedApiKey = result.GetProperty("encryptedApiKey").GetString()!,
            Salt            = result.GetProperty("salt").GetString()!,
            Iv              = result.GetProperty("iv").GetString()!,
            Provider        = provider,
            VertexProjectId = result.TryGetProperty("vertexProjectId", out var projProp) ? projProp.GetString() ?? "" : "",
            VertexLocation  = result.TryGetProperty("vertexLocation",  out var locProp)  ? locProp.GetString()  ?? "us-central1" : "us-central1"
        };
    }

    /// <summary>Decrypt the stored secret. Throws on wrong password.</summary>
    public async Task<string> DecryptApiKeyAsync(AppSettings settings, string password) =>
        await js.InvokeAsync<string>("cptcInterop.decryptApiKey",
            settings.EncryptedApiKey, settings.Salt, settings.Iv, password);

    /// <summary>Remove stored settings from IndexedDB.</summary>
    public async Task ClearSettingsAsync() =>
        await js.InvokeVoidAsync("cptcInterop.clearSettings");
}
