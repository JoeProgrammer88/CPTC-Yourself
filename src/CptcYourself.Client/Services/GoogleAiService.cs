using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using CptcYourself.Client.Models;

namespace CptcYourself.Client.Services;

/// <summary>
/// Calls Google AI Studio or Vertex AI to produce a career-inspiration image.
///
/// AI Studio  → single-step: Gemini 2.5 Flash native image generation
///              (responseModalities: ["IMAGE"]; webcam photo fed directly in)
///
/// Vertex AI  → two-step: Gemini generates a rich image prompt from the photo,
///              then Imagen 3 (capability model) renders the image using the
///              webcam photo as a SUBJECT reference so the person's face and
///              likeness appear in the output.
///              (Vertex AI does not yet support Gemini multi-modal output.)
/// </summary>
public class GoogleAiService(HttpClient http, IJSRuntime js, AppStateService state)
{
    private const string GeminiModel = "gemini-2.5-flash";
    // imagen-3.0-capability-001 supports subject/style reference images;
    // imagen-3.0-generate-002 does not.
    private const string ImagenModel = "imagen-3.0-capability-001";

    public Task<string> GenerateCareerImageAsync(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program) =>
        state.Provider == ApiProvider.VertexAi
            ? GenerateCareerImageVertexAsync(photoBase64, style, genre, program)
            : GenerateCareerImageAiStudioAsync(photoBase64, style, genre, program);

    // ── AI Studio: single-step Gemini native image generation ─────────────────

    private async Task<string> GenerateCareerImageAiStudioAsync(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program)
    {
        var body = BuildGeminiImageRequestBody(photoBase64, style, genre, program);
        var url  = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent?key={state.PlainTextApiKey}";

        var response = await http.PostAsJsonAsync(url, body);
        await EnsureSuccessAsync(response, "Gemini (image generation)");
        return await ParseGeminiImageResponseAsync(response);
    }

    // ── Vertex AI: two-step Gemini prompt → Imagen 3 ─────────────────────────

    private async Task<string> GenerateCareerImageVertexAsync(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program)
    {
        // Step 1 – ask Gemini to craft a detailed image prompt from the photo
        var promptBody = BuildGeminiPromptRequestBody(photoBase64, style, genre, program);
        var promptUrl  = $"https://{state.VertexLocation}-aiplatform.googleapis.com/v1/projects/{state.VertexProjectId}" +
                         $"/locations/{state.VertexLocation}/publishers/google/models/{GeminiModel}:generateContent";

        var promptReq  = await BuildVertexRequestAsync(promptUrl, promptBody);
        var promptResp = await http.SendAsync(promptReq);
        await EnsureSuccessAsync(promptResp, "Gemini via Vertex (prompt generation)");
        var imagePrompt = await ParseGeminiTextResponseAsync(promptResp);

        // Step 2 – render with Imagen 3, using the webcam photo as a subject
        //           reference so the output depicts the actual person
        var imagenBody = BuildImagenSubjectRequestBody(imagePrompt, photoBase64);
        var imagenUrl  = $"https://{state.VertexLocation}-aiplatform.googleapis.com/v1/projects/{state.VertexProjectId}" +
                         $"/locations/{state.VertexLocation}/publishers/google/models/{ImagenModel}:predict";

        var imagenReq  = await BuildVertexRequestAsync(imagenUrl, imagenBody);
        var imagenResp = await http.SendAsync(imagenReq);
        await EnsureSuccessAsync(imagenResp, "Imagen via Vertex (image generation)");
        return await ParseImagenResponseAsync(imagenResp);
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

    // ── Request builders ──────────────────────────────────────────────────────

    /// <summary>
    /// AI Studio request body: asks Gemini to output an image directly.
    /// responseModalities: ["IMAGE"] is not supported on Vertex AI.
    /// </summary>
    private static object BuildGeminiImageRequestBody(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program)
    {
        var systemInstruction =
            "You are a career-inspiration artist for the Center for Precision Technology and " +
            "Careers (CPTC). Using the person's face and likeness from the provided photo, " +
            "generate a vivid image that faithfully depicts this specific person thriving in a " +
            $"{program.ToDisplayName()} career. " +
            $"Render the image in a {style.ToDisplayName()} art style with a {genre.ToDisplayName()} tone. " +
            "Show a realistic, detailed professional environment appropriate for that program " +
            "(e.g. the tools, machinery, screens, or lab setting a graduate would work in). " +
            "Preserve the person's facial features and likeness. " +
            "Capture a strong sense of pride, confidence, and accomplishment.";

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
                        new
                        {
                            text = $"Generate a {style.ToDisplayName()} career image showing this person " +
                                   $"working in the {program.ToDisplayName()} field."
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "IMAGE" },
                temperature        = 1.0
            }
        };
    }

    /// <summary>
    /// Vertex AI request body: asks Gemini to write a detailed image-gen prompt
    /// (text only — no responseModalities) that Imagen 3 will render.
    /// </summary>
    private static object BuildGeminiPromptRequestBody(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program)
    {
        var systemInstruction =
            "You are a creative director specialising in career-inspiration artwork for the " +
            "Center for Precision Technology and Careers (CPTC). " +
            "Given a photo of a person and their chosen program of study, write a vivid " +
            "image-generation prompt that shows that specific person thriving in a career " +
            $"related to the '{program.ToDisplayName()}' program. " +
            $"The prompt must specify the '{style.ToDisplayName()}' art style and '{genre.ToDisplayName()}' genre, " +
            "describe the exact professional environment (tools, machinery, screens, or lab), " +
            "include the person's notable physical features from the photo (hair colour, " +
            "approximate age, distinguishing features) so the image closely resembles them, " +
            "and convey pride and accomplishment. " +
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
                        new
                        {
                            text = $"Program: {program.ToDisplayName()}. Style: {style.ToDisplayName()}. " +
                                   $"Genre: {genre.ToDisplayName()}. Write the image-generation prompt now."
                        }
                    }
                }
            },
            generationConfig = new { temperature = 1.0, maxOutputTokens = 512 }
        };
    }

    /// <summary>
    /// Imagen 3 capability model request with the webcam photo as a SUBJECT
    /// reference image so the generated image preserves the person's likeness.
    /// </summary>
    private static object BuildImagenSubjectRequestBody(string prompt, string photoBase64) => new
    {
        instances = new[]
        {
            new
            {
                prompt          = prompt,
                referenceImages = new[]
                {
                    new
                    {
                        referenceType  = "REFERENCE_TYPE_SUBJECT",
                        referenceId    = 1,
                        referenceImage = new { bytesBase64Encoded = photoBase64 },
                        subjectImageConfig = new { subjectType = "SUBJECT_TYPE_PERSON" }
                    }
                }
            }
        },
        parameters = new { sampleCount = 1, aspectRatio = "1:1", safetyFilterLevel = "block_few" }
    };

    // ── Response parsers ──────────────────────────────────────────────────────

    private static async Task<string> ParseGeminiImageResponseAsync(HttpResponseMessage response)
    {
        using var doc  = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var       parts = doc.RootElement
                             .GetProperty("candidates")[0]
                             .GetProperty("content")
                             .GetProperty("parts");

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("inlineData", out var inlineData))
                return inlineData.GetProperty("data").GetString()
                       ?? throw new InvalidOperationException("Empty image bytes in Gemini response.");
        }

        throw new InvalidOperationException("Gemini returned no image in its response.");
    }

    private static async Task<string> ParseGeminiTextResponseAsync(HttpResponseMessage response)
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
