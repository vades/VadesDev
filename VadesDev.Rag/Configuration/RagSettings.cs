namespace VadesDev.Rag.Configuration;

/// <summary>
/// Search settings: the minimum similarity score to accept a chunk (MinScore, depends on the embedding model)
/// and how many chunks to keep (TopK).
/// </summary>
public record RagSettings(float MinScore, int TopK = 2);