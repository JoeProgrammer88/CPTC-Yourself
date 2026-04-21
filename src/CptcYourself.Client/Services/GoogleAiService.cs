using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using CptcYourself.Client.Models;

namespace CptcYourself.Client.Services;

/// <summary>
/// Calls Google AI Studio (Gemini + Imagen) or Vertex AI APIs directly from the browser.
/// Reads credentials and provider from AppStateService.
/// </summary>
public class GoogleAiService(HttpClient http, IJSRuntime js, AppStateService state)
{
    
    private const string AiStudioGeminiModel = "gemini-2.0-flash";
    private const string AiStudioImagenModel = "imagen-3.0-generate-002";
    
    // Model Names for Vertex AI: https://docs.cloud.google.com/vertex-ai/generative-ai/docs/learn/model-versions
    private const string VertexGeminiModel   = "gemini-2.0-flash-001";
    private const string VertexImagenModel   = "imagen-3.0-generate-001";

    private string GeminiModel => state.Provider == ApiProvider.VertexAi ? VertexGeminiModel : AiStudioGeminiModel;
    private string ImagenModel => state.Provider == ApiProvider.VertexAi ? VertexImagenModel : AiStudioImagenModel;

    public Task<string> GenerateImagePromptAsync(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program) =>
        state.Provider == ApiProvider.VertexAi
            ? GenerateImagePromptVertexAsync(photoBase64, style, genre, program)
            : GenerateImagePromptAiStudioAsync(photoBase64, style, genre, program);

    public Task<string> GenerateImageAsync(string prompt) =>
        state.Provider == ApiProvider.VertexAi
            ? GenerateImageVertexAsync(prompt)
            : GenerateImageAiStudioAsync(prompt);

    // ── AI Studio ─────────────────────────────────────────────────────────────

    private async Task<string> GenerateImagePromptAiStudioAsync(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program)
    {
        var body = BuildGeminiRequestBody(photoBase64, style, genre, program);
        var url  = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent?key={state.PlainTextApiKey}";

        var response = await http.PostAsJsonAsync(url, body);
        await EnsureSuccessAsync(response, "Gemini (prompt generation)");
        return await ParseGeminiPromptAsync(response);
    }

    private async Task<string> GenerateImageAiStudioAsync(string prompt)
    {
        var body = BuildImagenRequestBody(prompt);
        var url  = $"https://generativelanguage.googleapis.com/v1beta/models/{ImagenModel}:predict?key={state.PlainTextApiKey}";

        var response = await http.PostAsJsonAsync(url, body);
        await EnsureSuccessAsync(response, "Imagen (image generation)");
        return await ParseImagenResponseAsync(response);
    }

    // ── Vertex AI ─────────────────────────────────────────────────────────────

    private async Task<string> GenerateImagePromptVertexAsync(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program)
    {
        var body  = BuildGeminiRequestBody(photoBase64, style, genre, program);
        var url   = $"https://{state.VertexLocation}-aiplatform.googleapis.com/v1/projects/{state.VertexProjectId}" +
                    $"/locations/{state.VertexLocation}/publishers/google/models/{GeminiModel}:generateContent";

        var request = await BuildVertexRequestAsync(url, body);
        var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response, "Gemini via Vertex (prompt generation)");
        return await ParseGeminiPromptAsync(response);
    }

    private async Task<string> GenerateImageVertexAsync(string prompt)
    {
        var body = BuildImagenRequestBody(prompt);
        var url  = $"https://{state.VertexLocation}-aiplatform.googleapis.com/v1/projects/{state.VertexProjectId}" +
                   $"/locations/{state.VertexLocation}/publishers/google/models/{ImagenModel}:predict";

        var request = await BuildVertexRequestAsync(url, body);
        var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response, "Imagen via Vertex (image generation)");
        return await ParseImagenResponseAsync(response);
    }

    private async Task<HttpRequestMessage> BuildVertexRequestAsync(string url, object body)
    {
        var accessToken = await js.InvokeAsync<string>("cptcInterop.getVertexAccessToken", state.PlainTextApiKey);
        var json        = JsonSerializer.Serialize(body);
        var request     = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    // ── Shared request / response helpers ────────────────────────────────────

    private static object BuildGeminiRequestBody(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program)
    {
        var systemInstruction =
            "You are a creative director specialising in career-inspiration artwork for the " +
            "Center for Precision Technology and Careers (CPTC) at a community college. " +
            "Given a photo of a person and their chosen program of study, create a vivid " +
            "image-generation prompt that shows that person thriving in a career directly " +
            "related to their program. " +
            "The prompt must specify the art style and genre supplied by the user, vividly " +
            $"describe an environment specific to the '{program.ToDisplayName()}' program " +
            "(e.g. the tools, machinery, screens, or lab setting a graduate of that program " +
            "would work in), and capture a sense of pride and accomplishment. " +
            "Return ONLY the image-generation prompt — no preamble, no explanation.";

        return new
        {
            system_instruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[]
            {
                new
                {
                    role  = "user",
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = "image/jpeg", data = photoBase64 } },
                        new { text = $"Program: {program.ToDisplayName()}. Art style: {style.ToDisplayName()}. Genre: {genre.ToDisplayName()}. Create the image-generation prompt now." }
                    }
                }
            },
            generationConfig = new { temperature = 1.0, maxOutputTokens = 512 }
        };
    }

    private static object BuildImagenRequestBody(string prompt) => new
    {
        instances  = new[] { new { prompt } },
        parameters = new { sampleCount = 1, aspectRatio = "1:1", safetyFilterLevel = "block_few" }
    };

    private static async Task<string> ParseGeminiPromptAsync(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return (doc.RootElement
                   .GetProperty("candidates")[0]
                   .GetProperty("content")
                   .GetProperty("parts")[0]
                   .GetProperty("text")
                   .GetString()
               ?? throw new InvalidOperationException("Empty prompt from Gemini.")).Trim();
    }

    private static async Task<string> ParseImagenResponseAsync(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement
                  .GetProperty("predictions")[0]
                  .GetProperty("bytesBase64Encoded")
                  .GetString()
               ?? throw new InvalidOperationException("No image data returned from Imagen.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string step)
    {
        if (response.IsSuccessStatusCode) return;

        string detail;
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            detail = doc.RootElement
                        .GetProperty("error")
                        .GetProperty("message")
                        .GetString() ?? body;
        }
        catch
        {
            detail = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }

        throw new HttpRequestException($"{step} failed ({(int)response.StatusCode}): {detail}");
    }
}
