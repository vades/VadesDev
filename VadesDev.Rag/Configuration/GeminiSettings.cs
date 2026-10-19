namespace VadesDev.Rag.Configuration;

/// <summary>
/// Gemini settings: OpenAI-compatible endpoint and model names.
/// The API key is not part of it, so it never appears in logs or in ToString().
/// </summary>
public record GeminiSettings(string Endpoint, string ChatModel, string EmbeddingModel);