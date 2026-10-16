namespace VadesDev.Rag.Models;

/// <summary>
/// A chunk that matched a question, together with its cosine similarity score (higher = more similar).
/// </summary>
public record SearchHit(DocumentChunk Chunk, float Score);