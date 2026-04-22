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
/// Both providers use a two-step pipeline:
///   Step 1 – Gemini (text) analyzes the webcam photo and produces a precise
///             artist's description of the subject's appearance.
///   Step 2 – The image is generated using that description to anchor likeness:
///     AI Studio  → Gemini 2.5 Flash native image generation via multi-turn
///                  context (photo + description → image output).
///     Vertex AI  → Imagen 3 capability model with the photo as a SUBJECT
///                  reference image (Vertex doesn't support Gemini image output).
/// </summary>
public class GoogleAiService(HttpClient http, IJSRuntime js, AppStateService state)
{
    private const string GeminiModel = "gemini-2.5-flash";
    // imagen-3.0-capability-001 supports subject/style reference images;
    // imagen-3.0-generate-002 does not.
    private const string ImagenModel = "imagen-3.0-capability-001";

    public Task<(string ImageBase64, string Prompt)> GenerateCareerImageAsync(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program) =>
        state.Provider == ApiProvider.VertexAi
            ? GenerateCareerImageVertexAsync(photoBase64, style, genre, program)
            : GenerateCareerImageAiStudioAsync(photoBase64, style, genre, program);

    // ── AI Studio: two-step Gemini (analyze → image) ──────────────────────────

    private async Task<(string ImageBase64, string Prompt)> GenerateCareerImageAiStudioAsync(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program)
    {
        var baseUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent?key={state.PlainTextApiKey}";

        // Step 1 – extract a precise description of the subject from the photo
        var analyzeBody = BuildAnalyzePersonBody(photoBase64);
        var analyzeResp = await http.PostAsJsonAsync(baseUrl, analyzeBody);
        await EnsureSuccessAsync(analyzeResp, "Gemini (subject analysis)");
        var personDescription = await ParseGeminiTextResponseAsync(analyzeResp);

        // Step 2 – generate the career image using a multi-turn context that
        //          includes both the photo and the committed description
        var imageBody = BuildAiStudioImageBody(photoBase64, personDescription, style, genre, program);
        var imageResp = await http.PostAsJsonAsync(baseUrl, imageBody);
        await EnsureSuccessAsync(imageResp, "Gemini (image generation)");
        var imageBase64 = await ParseGeminiImageResponseAsync(imageResp);

        var promptSummary =
            $"[Subject analysis]\n{personDescription}\n\n" +
            $"[Scene]\n{style.ToDisplayName()} {genre.ToDisplayName()} image of the person above " +
            $"depicted as a {program.ToDisplayName()} professional: {program.ToSceneDescription()}.";
        return (imageBase64, promptSummary);
    }

    // ── Vertex AI: two-step Gemini prompt → Imagen 3 ─────────────────────────

    private async Task<(string ImageBase64, string Prompt)> GenerateCareerImageVertexAsync(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program)
    {
        var geminiUrl = $"https://{state.VertexLocation}-aiplatform.googleapis.com/v1/projects/{state.VertexProjectId}" +
                        $"/locations/{state.VertexLocation}/publishers/google/models/{GeminiModel}:generateContent";

        // Step 1 – Gemini analyzes the subject and writes an Imagen-optimised prompt
        var promptBody = BuildVertexPromptRequestBody(photoBase64, style, genre, program);
        var promptReq  = await BuildVertexRequestAsync(geminiUrl, promptBody);
        var promptResp = await http.SendAsync(promptReq);
        await EnsureSuccessAsync(promptResp, "Gemini via Vertex (prompt generation)");
        var imagePrompt = await ParseGeminiTextResponseAsync(promptResp);

        // Step 2 – Imagen 3 renders the image using the prompt AND the photo as
        //          a SUBJECT reference so the person's likeness is preserved
        var imagenBody = BuildImagenSubjectRequestBody(imagePrompt, photoBase64);
        var imagenUrl  = $"https://{state.VertexLocation}-aiplatform.googleapis.com/v1/projects/{state.VertexProjectId}" +
                         $"/locations/{state.VertexLocation}/publishers/google/models/{ImagenModel}:predict";

        var imagenReq  = await BuildVertexRequestAsync(imagenUrl, imagenBody);
        var imagenResp = await http.SendAsync(imagenReq);
        await EnsureSuccessAsync(imagenResp, "Imagen via Vertex (image generation)");
        var imageBase64 = await ParseImagenResponseAsync(imagenResp);
        return (imageBase64, imagePrompt);
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
    /// Step 1 (both providers): ask Gemini to produce a precise artist's brief
    /// describing the subject's appearance from the webcam photo.
    /// </summary>
    private static object BuildAnalyzePersonBody(string photoBase64) => new
    {
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
                        text =
                            "You are an artist's assistant. Study the main person in this photo and " +
                            "write a precise, detailed physical description that an artist could use to " +
                            "draw or paint this exact person. Cover: approximate age, gender presentation, " +
                            "skin tone, hair colour and style, eye colour if visible, face shape, any " +
                            "notable facial features, approximate build, and any distinctive characteristics. " +
                            "Be specific and objective. Return ONLY the description — no preamble."
                    }
                }
            }
        },
        generationConfig = new { temperature = 0.2, maxOutputTokens = 300 }
    };

    /// <summary>
    /// AI Studio Step 2: multi-turn Gemini request that includes the photo,
    /// the committed subject description, and asks for native image output.
    /// </summary>
    private static object BuildAiStudioImageBody(string photoBase64, string personDescription, ArtStyle style, ArtGenre genre, CptcProgram program) => new
    {
        system_instruction = new
        {
            parts = new[]
            {
                new
                {
                    text =
                        "You are a career-inspiration artist for the Center for Precision Technology " +
                        "and Careers (CPTC). Your task is to composite the subject from a reference " +
                        "photo into a vivid career scene. " +
                        "Rules: (1) Extract the main person from the reference photo. " +
                        "(2) Faithfully preserve every physical detail of their appearance — face, " +
                        "hair, skin tone, build. Do NOT change their looks. " +
                        $"(3) Place them into a scene where they are {program.ToSceneDescription()}. " +
                        $"(4) Render entirely in {style.ToDisplayName()} art style, {genre.ToDisplayName()} tone. " +
                        "(5) The person should be the clear, confident focal point of the image."
                }
            }
        },
        contents = new object[]
        {
            // Turn 1 – user submits the photo
            new
            {
                role  = "user",
                parts = new object[]
                {
                    new { inline_data = new { mime_type = "image/jpeg", data = photoBase64 } },
                    new { text = "Here is the reference photo of the subject." }
                }
            },
            // Turn 2 – model acknowledges with the committed description
            new
            {
                role  = "model",
                parts = new[] { new { text = $"Understood. Subject description: {personDescription}" } }
            },
            // Turn 3 – user requests the career image, photo included again so it
            //          is the direct visual input at generation time
            new
            {
                role  = "user",
                parts = new object[]
                {
                    new { inline_data = new { mime_type = "image/jpeg", data = photoBase64 } },
                    new
                    {
                        text =
                            $"Using this photo and the subject description above, generate a " +
                            $"{style.ToDisplayName()}, {genre.ToDisplayName()} career image of this exact " +
                            $"person working as a {program.ToDisplayName()} professional at CPTC. " +
                            $"Show them {program.ToSceneDescription()}. " +
                            "Preserve their face, hair, skin tone, and build exactly as shown in the photo."
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

    /// <summary>
    /// Vertex AI Step 1: Gemini writes an Imagen-optimised prompt that includes
    /// the subject's physical features extracted from the photo.
    /// </summary>
    private static object BuildVertexPromptRequestBody(string photoBase64, ArtStyle style, ArtGenre genre, CptcProgram program) => new
    {
        system_instruction = new
        {
            parts = new[]
            {
                new
                {
                    text =
                        "You are an expert Imagen 3 prompt engineer for the Center for Precision " +
                        "Technology and Careers (CPTC). Given a photo of a person, write a single " +
                        "highly detailed image-generation prompt that will produce a career-inspiration " +
                        "image of that specific person. " +
                        "Prompt structure rules: " +
                        $"(1) Open with the art style: '{style.ToDisplayName()} style, {genre.ToDisplayName()} tone'. " +
                        "(2) Describe the subject using exact physical details observed from the photo " +
                        "(age, gender, skin tone, hair colour/style, face shape, build). " +
                        $"(3) Place them in this scene: {program.ToSceneDescription()}. " +
                        "(4) End with lighting/mood: 'cinematic lighting, sharp focus, high detail, " +
                        "professional composition, conveying pride and accomplishment'. " +
                        "Return ONLY the prompt — no preamble, no explanation."
                }
            }
        },
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
                               $"Genre: {genre.ToDisplayName()}. Write the Imagen prompt now."
                    }
                }
            }
        },
        generationConfig = new { temperature = 0.4, maxOutputTokens = 400 }
    };

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
