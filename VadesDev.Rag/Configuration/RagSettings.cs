namespace VadesDev.Rag.Configuration;

/// <summary>
/// Search settings: how many chunks to keep (TopK) and the minimum similarity score to accept one (MinScore).
/// </summary>
public record RagSettings(int TopK = 2, float MinScore = 0.60f);