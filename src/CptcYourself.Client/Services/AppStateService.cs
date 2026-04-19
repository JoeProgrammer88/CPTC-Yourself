using CptcYourself.Client.Models;

namespace CptcYourself.Client.Services;

/// <summary>
/// Holds runtime session state: decrypted API key and current selections.
/// Does NOT persist the plain-text key across page refreshes.
/// </summary>
public class AppStateService
{
    // Set after the user unlocks with their password
    public string? PlainTextApiKey { get; private set; }
    public bool IsUnlocked => PlainTextApiKey is not null;

    public ArtStyle SelectedStyle { get; set; } = ArtStyle.Cartoon;
    public ArtGenre SelectedGenre { get; set; } = ArtGenre.Inspirational;

    public void SetApiKey(string key) => PlainTextApiKey = key;
    public void Lock() => PlainTextApiKey = null;
}
