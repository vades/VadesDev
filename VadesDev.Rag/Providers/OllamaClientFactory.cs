using Microsoft.Extensions.AI;
using OllamaSharp;
using VadesDev.Rag.Configuration;

namespace VadesDev.Rag.Providers;

/// <summary>
/// Creates the Ollama clients (chat and embeddings) for a local Ollama server.
/// </summary>
public class OllamaClientFactory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private readonly OllamaSettings _settings;

    public OllamaClientFactory(OllamaSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Creates the chat client for the model named in the settings.
    /// </summary>
    public IChatClient CreateChatClient()
    {
        return CreateClient(_settings.ChatModel);
    }

    /// <summary>
    /// Creates the embedding generator for the model named in the settings.
    /// </summary>
    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator()
    {
        return CreateClient(_settings.EmbeddingModel);
    }

    /// <summary>
    /// Creates an Ollama client for one model, with a long timeout because a local model can need time to load.
    /// </summary>
    private OllamaApiClient CreateClient(string model)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.Endpoint),
            Timeout = RequestTimeout
        };

        return new OllamaApiClient(httpClient, model);
    }
}