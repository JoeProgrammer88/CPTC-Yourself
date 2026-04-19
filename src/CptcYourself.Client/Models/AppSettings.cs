namespace CptcYourself.Client.Models;

public enum ApiProvider { AiStudio, VertexAi }

public class AppSettings
{
    public ApiProvider Provider { get; set; } = ApiProvider.AiStudio;
    public string EncryptedApiKey { get; set; } = string.Empty;  // AI Studio: API key; Vertex AI: service account JSON
    public string Salt { get; set; } = string.Empty;             // hex-encoded 16-byte salt
    public string Iv { get; set; } = string.Empty;               // hex-encoded 12-byte IV (AES-GCM)
    public string VertexProjectId { get; set; } = string.Empty;
    public string VertexLocation { get; set; } = "us-central1";
}
