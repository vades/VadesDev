namespace VadesDev.Rag.Configuration;

/// <summary>
/// Ollama settings: address of the local server, model names and the MinScore calibrated for the embedding model.
/// A local server needs no API key.
/// </summary>
public record OllamaSettings(string Endpoint, string ChatModel, string EmbeddingModel, float MinScore);