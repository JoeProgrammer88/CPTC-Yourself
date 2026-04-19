using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CptcYourself.Client.Models;

namespace CptcYourself.Client.Services;

/// <summary>
/// Calls Google AI Studio (Gemini + Imagen) APIs directly from the browser.
/// All requests use the user-supplied API key — nothing is stored server-side.
/// </summary>
public class GoogleAiService(HttpClient http)
{
    private const string GeminiModel = "gemini-2.0-flash";
    private const string ImagenModel = "imagen-3.0-generate-002";

    public async Task<string> GenerateImagePromptAsync(
        string photoBase64,
        ArtStyle style,
        ArtGenre genre,
        string apiKey)
    {
        var systemInstruction =
            "You are a creative director specialising in career-inspiration artwork for a " +
            "Center for Advanced Manufacturing at a community college. " +
            "Given a photo of a person, create a vivid image-generation prompt that shows " +
            "that person thriving in a manufacturing or engineering career. " +
            "The prompt must specify the art style and genre supplied by the user, describe " +
            "the environment (modern CNC lab, robotics bay, quality-inspection station, etc.), " +
            "and capture a sense of pride and accomplishment. " +
            "Return ONLY the image-generation prompt — no preamble, no explanation.";

        var styleText = style.ToDisplayName();
        var genreText = genre.ToDisplayName();

        var body = new
        {
            system_instruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[]
            {
                new
                {
                    role  = "user",
                    parts = new object[]
                    {
                        new
                        {
                            inline_data = new
                            {
                                mime_type = "image/jpeg",
                                data      = photoBase64
                            }
                        },
                        new
                        {
                            text = $"Art style: {styleText}. Genre: {genreText}. " +
                                   "Create the image-generation prompt now."
                        }
                    }
                }
            },
            generationConfig = new { temperature = 1.0, maxOutputTokens = 512 }
        };

        var url      = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent?key={apiKey}";
        var response = await http.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();

        using var doc     = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var       prompt  = doc.RootElement
                               .GetProperty("candidates")[0]
                               .GetProperty("content")
                               .GetProperty("parts")[0]
                               .GetProperty("text")
                               .GetString()
                           ?? throw new InvalidOperationException("Empty prompt from Gemini.");
        return prompt.Trim();
    }

    /// <summary>
    /// Returns a base64-encoded PNG/JPEG as produced by Imagen 3.
    /// </summary>
    public async Task<string> GenerateImageAsync(string prompt, string apiKey)
    {
        var body = new
        {
            instances = new[]
            {
                new { prompt }
            },
            parameters = new
            {
                sampleCount    = 1,
                aspectRatio    = "1:1",
                safetyFilterLevel = "block_few"
            }
        };

        var url      = $"https://generativelanguage.googleapis.com/v1beta/models/{ImagenModel}:predict?key={apiKey}";
        var response = await http.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();

        using var doc      = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var       b64      = doc.RootElement
                                .GetProperty("predictions")[0]
                                .GetProperty("bytesBase64Encoded")
                                .GetString()
                            ?? throw new InvalidOperationException("No image data returned from Imagen.");
        return b64;
    }
}
