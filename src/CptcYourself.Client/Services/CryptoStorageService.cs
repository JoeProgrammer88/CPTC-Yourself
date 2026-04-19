using Microsoft.JSInterop;
using CptcYourself.Client.Models;
using System.Text.Json;

namespace CptcYourself.Client.Services;

public class CryptoStorageService(IJSRuntime js)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Encrypt apiKey with password, persist to IndexedDB, return the record.</summary>
    public async Task<AppSettings> SaveApiKeyAsync(string apiKey, string password)
    {
        var result = await js.InvokeAsync<JsonElement>("cptcInterop.saveApiKey", apiKey, password);
        return new AppSettings
        {
            EncryptedApiKey = result.GetProperty("encryptedApiKey").GetString()!,
            Salt            = result.GetProperty("salt").GetString()!,
            Iv              = result.GetProperty("iv").GetString()!
        };
    }

    /// <summary>Load the stored (encrypted) settings record, or null if nothing saved yet.</summary>
    public async Task<AppSettings?> LoadSettingsAsync()
    {
        var result = await js.InvokeAsync<JsonElement?>("cptcInterop.loadSettings");
        if (result is null || result.Value.ValueKind == JsonValueKind.Null)
            return null;
        return new AppSettings
        {
            EncryptedApiKey = result.Value.GetProperty("encryptedApiKey").GetString()!,
            Salt            = result.Value.GetProperty("salt").GetString()!,
            Iv              = result.Value.GetProperty("iv").GetString()!
        };
    }

    /// <summary>Decrypt the stored API key. Throws on wrong password.</summary>
    public async Task<string> DecryptApiKeyAsync(AppSettings settings, string password) =>
        await js.InvokeAsync<string>("cptcInterop.decryptApiKey",
            settings.EncryptedApiKey, settings.Salt, settings.Iv, password);

    /// <summary>Remove stored settings from IndexedDB.</summary>
    public async Task ClearSettingsAsync() =>
        await js.InvokeVoidAsync("cptcInterop.clearSettings");
}
