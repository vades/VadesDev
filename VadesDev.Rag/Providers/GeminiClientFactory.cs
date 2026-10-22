using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using VadesDev.Rag.Configuration;

namespace VadesDev.Rag.Providers;

/// <summary>
/// Creates the Gemini clients (chat and embeddings) through Google's OpenAI-compatible endpoint.
/// </summary>
public class GeminiClientFactory
{
    private readonly GeminiSettings _settings;
    private readonly OpenAIClient _client;

    public GeminiClientFactory(GeminiSettings settings, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _settings = settings;
        _client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) });
    }

    /// <summary>
    /// Creates the chat client for the model named in the settings.
    /// </summary>
    public IChatClient CreateChatClient()
    {
        return _client.GetChatClient(_settings.ChatModel).AsIChatClient();
    }

    /// <summary>
    /// Creates the embedding generator for the model named in the settings.
    /// </summary>
    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator()
    {
        return _client.GetEmbeddingClient(_settings.EmbeddingModel).AsIEmbeddingGenerator();
    }
}