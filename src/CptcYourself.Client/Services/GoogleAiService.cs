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
    private const string TtsModel    = "gemini-2.5-flash";

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

    // ── Song generation ───────────────────────────────────────────────────────

    public Task<(string Lyrics, string? AudioBase64, string AudioMimeType, string? TtsError)> GenerateSongAsync(
        MusicGenre genre, CptcProgram program, string? name = null) =>
        state.Provider == ApiProvider.VertexAi
            ? GenerateSongVertexAsync(genre, program, name)
            : GenerateSongAiStudioAsync(genre, program, name);

    private async Task<(string Lyrics, string? AudioBase64, string AudioMimeType, string? TtsError)> GenerateSongAiStudioAsync(
        MusicGenre genre, CptcProgram program, string? name)
    {
        var textUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent?key={state.PlainTextApiKey}";
        var ttsUrl  = $"https://generativelanguage.googleapis.com/v1beta/models/{TtsModel}:generateContent?key={state.PlainTextApiKey}";

        // Step 1 – generate lyrics with the text model
        var lyricsResp = await http.PostAsJsonAsync(textUrl, BuildSongLyricsBody(genre, program, name));
        await EnsureSuccessAsync(lyricsResp, "Gemini (lyrics generation)");
        var lyrics = await ParseGeminiTextResponseAsync(lyricsResp);

        // Step 2 – synthesize only the first verse via TTS (best-effort)
        try
        {
            var firstVerse = ExtractFirstVerse(lyrics);
            var ttsResp = await http.PostAsJsonAsync(ttsUrl, BuildTtsSongBody(firstVerse, genre));
            await EnsureSuccessAsync(ttsResp, "Gemini (audio synthesis)");
            var (audioBase64, mimeType) = await ParseGeminiAudioResponseAsync(ttsResp);
            return (lyrics, audioBase64, mimeType, null);
        }
        catch (Exception ex)
        {
            return (lyrics, null, string.Empty, ex.Message);
        }
    }

    private async Task<(string Lyrics, string? AudioBase64, string AudioMimeType, string? TtsError)> GenerateSongVertexAsync(
        MusicGenre genre, CptcProgram program, string? name)
    {
        var geminiUrl = $"https://{state.VertexLocation}-aiplatform.googleapis.com/v1/projects/{state.VertexProjectId}" +
                        $"/locations/{state.VertexLocation}/publishers/google/models/{GeminiModel}:generateContent";
        var ttsUrl    = $"https://{state.VertexLocation}-aiplatform.googleapis.com/v1/projects/{state.VertexProjectId}" +
                        $"/locations/{state.VertexLocation}/publishers/google/models/{TtsModel}:generateContent";

        // Step 1 – generate lyrics
        var lyricsReq  = await BuildVertexRequestAsync(geminiUrl, BuildSongLyricsBody(genre, program, name));
        var lyricsResp = await http.SendAsync(lyricsReq);
        await EnsureSuccessAsync(lyricsResp, "Gemini via Vertex (lyrics generation)");
        var lyrics = await ParseGeminiTextResponseAsync(lyricsResp);

        // Step 2 – synthesize only the first verse via TTS (best-effort)
        // If Gemini TTS is not allowlisted, fall back to Lyria instrumental
        try
        {
            var firstVerse = ExtractFirstVerse(lyrics);
            var ttsReq  = await BuildVertexRequestAsync(ttsUrl, BuildTtsSongBody(firstVerse, genre));
            var ttsResp = await http.SendAsync(ttsReq);
            await EnsureSuccessAsync(ttsResp, "Gemini via Vertex (audio synthesis)");
            var (audioBase64, mimeType) = await ParseGeminiAudioResponseAsync(ttsResp);
            return (lyrics, audioBase64, mimeType, null);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("allowlist", StringComparison.OrdinalIgnoreCase))
        {
            // Gemini audio not allowlisted — try Lyria for instrumental
            try
            {
                var (lyriaAudio, lyriaMime) = await GenerateLyriaInstrumentalAsync(genre, lyrics);
                return (lyrics, lyriaAudio, lyriaMime, null);
            }
            catch (Exception lyriaEx)
            {
                return (lyrics, null, string.Empty, $"Lyria instrumental failed: {lyriaEx.Message}");
            }
        }
        catch (Exception ex)
        {
            return (lyrics, null, string.Empty, ex.Message);
        }
    }

    private async Task<(string AudioBase64, string MimeType)> GenerateLyriaInstrumentalAsync(
        MusicGenre genre, string lyrics)
    {
        // Lyria uses a global endpoint and a different interactions API shape
        var url          = $"https://aiplatform.googleapis.com/v1beta1/projects/{state.VertexProjectId}/locations/global/interactions";
        var musicPrompt  = BuildLyriaPrompt(genre, lyrics);
        var body         = new
        {
            model = "lyria-3-clip-preview",
            input = new[] { new { type = "text", text = musicPrompt } }
        };
        var req  = await BuildVertexRequestAsync(url, body);
        var resp = await http.SendAsync(req);
        await EnsureSuccessAsync(resp, "Lyria (instrumental generation)");

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        foreach (var output in doc.RootElement.GetProperty("outputs").EnumerateArray())
        {
            if (output.TryGetProperty("type", out var t) && t.GetString() == "audio")
            {
                var data     = output.GetProperty("data").GetString()
                               ?? throw new InvalidOperationException("Empty audio data from Lyria.");
                var mimeType = output.TryGetProperty("mime_type", out var mt)
                               ? mt.GetString() ?? "audio/mpeg"
                               : "audio/mpeg";
                return (data, mimeType);
            }
        }
        throw new InvalidOperationException("Lyria returned no audio output.");
    }

    private static string BuildLyriaPrompt(MusicGenre genre, string lyrics)
    {
        // Extract mood/theme words from lyrics to enrich the prompt
        var styleDescriptor = genre switch
        {
            MusicGenre.Rap        => "hip-hop beat with punchy 808s and a driving rhythm",
            MusicGenre.Rock       => "driving rock track with electric guitar riffs and live drums",
            MusicGenre.Country    => "country song with acoustic guitar, fiddle, and warm pedal steel",
            MusicGenre.Pop        => "upbeat pop track with bright synths, catchy melody, and modern production",
            MusicGenre.RnB        => "smooth R&B groove with soulful chords and laid-back drums",
            MusicGenre.Jazz       => "live jazz arrangement with piano, upright bass, and brushed drums",
            MusicGenre.HipHop     => "boom-bap hip-hop beat with sampled chops and a head-nodding groove",
            MusicGenre.Electronic => "electronic track with pulsing synths, four-on-the-floor kick, and atmosphere",
            MusicGenre.Folk       => "folk song with fingerpicked acoustic guitar, light percussion, and warm strings",
            MusicGenre.Metal      => "heavy metal track with distorted guitars, double-kick drums, and powerful energy",
            _                     => "instrumental music track"
        };
        return $"Instrumental {styleDescriptor}. Motivational, professional, uplifting tone. " +
               $"No vocals. Suitable as background music for a career inspiration theme.";
    }

    /// <summary>
    /// Extracts the first labelled section (e.g. [Verse 1]) from the lyrics.
    /// Falls back to the first non-empty paragraph if no section header is found.
    /// </summary>
    private static string ExtractFirstVerse(string lyrics)
    {
        var lines = lyrics.Split('\n');
        var section = new List<string>();
        bool inSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                if (inSection && section.Count > 0) break; // end of first section
                inSection = true;
                continue; // skip the header line itself
            }
            if (inSection)
            {
                if (trimmed.Length == 0 && section.Count > 0) break; // blank line ends section
                if (trimmed.Length > 0) section.Add(trimmed);
            }
        }

        return section.Count > 0
            ? string.Join('\n', section)
            : string.Join('\n', lines.Take(8)); // fallback: first 8 lines
    }

    private static object BuildSongLyricsBody(MusicGenre genre, CptcProgram program, string? name) => new
    {
        system_instruction = new
        {
            parts = new[]
            {
                new
                {
                    text =
                        "You are a professional songwriter. Write authentic, catchy song lyrics " +
                        "that fit the requested genre and subject matter. " +
                        "Format the lyrics with section headers like [Verse 1], [Chorus], [Verse 2], etc. " +
                        "Return ONLY the lyrics — no preamble, no explanation, no commentary."
                }
            }
        },
        contents = new[]
        {
            new
            {
                role  = "user",
                parts = new[]
                {
                    new
                    {
                        text =
                            (string.IsNullOrWhiteSpace(name)
                                ? $"Write a {genre.ToDisplayName()} song about being a {program.ToDisplayName()} student or professional at CPTC (Center for Precision Technology and Careers). "
                                : $"Write a {genre.ToDisplayName()} song about {name}, a {program.ToDisplayName()} student or professional at CPTC (Center for Precision Technology and Careers). Mention their name naturally in the lyrics. ") +
                            $"Activities in this field include: {program.ToSceneDescription()}. " +
                            $"Make it motivational and authentic to the {genre.ToDisplayName()} style. " +
                            "Structure: [Verse 1], [Chorus], [Verse 2], [Chorus], [Bridge], [Chorus]."
                    }
                }
            }
        },
        generationConfig = new { temperature = 1.0, maxOutputTokens = 3000 }
    };

    private static object BuildTtsSongBody(string lyrics, MusicGenre genre) => new
    {
        contents = new[]
        {
            new
            {
                parts = new[]
                {
                    new
                    {
                        text =
                            $"Perform the following {genre.ToDisplayName()} song lyrics " +
                            $"with energy and style appropriate for {genre.ToDisplayName()} music:\n\n{lyrics}"
                    }
                }
            }
        },
        generationConfig = new
        {
            responseModalities = new[] { "AUDIO" },
            speechConfig       = new
            {
                voiceConfig = new
                {
                    prebuiltVoiceConfig = new { voiceName = GetVoiceForGenre(genre) }
                }
            }
        }
    };

    private static string GetVoiceForGenre(MusicGenre genre) => genre switch
    {
        MusicGenre.Rap        => "Puck",
        MusicGenre.HipHop     => "Puck",
        MusicGenre.Electronic => "Puck",
        MusicGenre.Rock       => "Charon",
        MusicGenre.Metal      => "Charon",
        MusicGenre.Country    => "Kore",
        MusicGenre.Folk       => "Kore",
        MusicGenre.Jazz       => "Fenrir",
        MusicGenre.Pop        => "Aoede",
        MusicGenre.RnB        => "Aoede",
        _                     => "Kore"
    };

    private static async Task<(string AudioBase64, string MimeType)> ParseGeminiAudioResponseAsync(
        HttpResponseMessage response)
    {
        using var doc   = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var       parts = doc.RootElement
                             .GetProperty("candidates")[0]
                             .GetProperty("content")
                             .GetProperty("parts");

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("inlineData", out var inlineData))
            {
                var data     = inlineData.GetProperty("data").GetString()
                               ?? throw new InvalidOperationException("Empty audio bytes in Gemini TTS response.");
                var mimeType = inlineData.TryGetProperty("mimeType", out var mt)
                               ? mt.GetString() ?? "audio/pcm"
                               : "audio/pcm";
                return (data, mimeType);
            }
        }

        throw new InvalidOperationException("Gemini TTS returned no audio in its response.");
    }
}
