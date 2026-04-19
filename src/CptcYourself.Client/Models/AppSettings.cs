namespace CptcYourself.Client.Models;

public class AppSettings
{
    public string EncryptedApiKey { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;      // hex-encoded 16-byte salt
    public string Iv { get; set; } = string.Empty;        // hex-encoded 12-byte IV (AES-GCM)
}
