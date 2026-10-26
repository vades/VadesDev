using Microsoft.Extensions.Configuration;
using VadesDev.Rag.Configuration;

namespace VadesDev.Rag.Providers;

/// <summary>
/// Builds the AI provider selected in the configuration (Rag:Provider): chat client, embedder, model name and MinScore.
/// </summary>
public class AiProviderFactory
{
    private readonly IConfiguration _configuration;

    public AiProviderFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Creates the provider named in Rag:Provider. Throws a clear error for an unknown name.
    /// </summary>
    public AiProvider Create()
    {
        var name = _configuration["Rag:Provider"] ?? "";

        return name.ToLowerInvariant() switch
        {
            "ollama" => CreateOllama(),
            "gemini" => CreateGemini(),
            _ => throw new InvalidOperationException($"Unknown provider '{name}'. Use 'Gemini' or 'Ollama'.")
        };
    }

    /// <summary>
    /// Builds the Gemini provider from the Gemini section and the API key.
    /// </summary>
    private AiProvider CreateGemini()
    {
        var settings = AppConfiguration.GetRequired<GeminiSettings>(_configuration, "Gemini");
        var factory = new GeminiClientFactory(settings, _configuration["Gemini:ApiKey"] ?? "");

        return new AiProvider(
            "Gemini",
            factory.CreateChatClient(),
            factory.CreateEmbeddingGenerator(),
            settings.EmbeddingModel,
            settings.MinScore);
    }

    /// <summary>
    /// Builds the Ollama provider from the Ollama section.
    /// </summary>
    private AiProvider CreateOllama()
    {
        var settings = AppConfiguration.GetRequired<OllamaSettings>(_configuration, "Ollama");
        var factory = new OllamaClientFactory(settings);

        return new AiProvider(
            "Ollama",
            factory.CreateChatClient(),
            factory.CreateEmbeddingGenerator(),
            settings.EmbeddingModel,
            settings.MinScore);
    }
}