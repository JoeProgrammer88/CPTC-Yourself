using CptcYourself.Client.Models;

namespace CptcYourself.Client.Services;

/// <summary>
/// Holds runtime session state: decrypted credentials and current selections.
/// Does NOT persist the plain-text secret across page refreshes.
/// </summary>
public class AppStateService
{
    // Set after the user unlocks with their password.
    // For AiStudio: the API key string. For VertexAi: the service account JSON string.
    public string? PlainTextApiKey { get; private set; }
    public bool IsUnlocked => PlainTextApiKey is not null;

    public ApiProvider Provider { get; private set; } = ApiProvider.AiStudio;
    public string VertexProjectId { get; private set; } = string.Empty;
    public string VertexLocation { get; private set; } = "us-central1";

    public CptcProgram SelectedProgram { get; set; } = CptcProgram.ManufacturingEngineeringTechnologies;
    public ArtStyle SelectedStyle { get; set; } = ArtStyle.Cartoon;
    public ArtGenre SelectedGenre { get; set; } = ArtGenre.Inspirational;

    public void SetCredentials(string decryptedSecret, ApiProvider provider,
                               string vertexProjectId = "", string vertexLocation = "us-central1")
    {
        PlainTextApiKey  = decryptedSecret;
        Provider         = provider;
        VertexProjectId  = vertexProjectId;
        VertexLocation   = vertexLocation;
    }

    // Kept for backward compatibility
    public void SetApiKey(string key) => PlainTextApiKey = key;

    public void Lock()
    {
        PlainTextApiKey = null;
        Provider        = ApiProvider.AiStudio;
        VertexProjectId = string.Empty;
        VertexLocation  = "us-central1";
    }
}
