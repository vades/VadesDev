namespace VadesDev.Rag.Models;

/// <summary>
/// A chunk together with its embedding vector. This is what the search keeps in memory.
/// </summary>
public record IndexedChunk(DocumentChunk Chunk, ReadOnlyMemory<float> Vector);