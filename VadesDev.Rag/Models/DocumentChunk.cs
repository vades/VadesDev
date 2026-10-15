namespace VadesDev.Rag.Models;

/// <summary>
/// One piece of knowledge: its id, the file it came from, and its text.
/// </summary>
public record DocumentChunk(string Id, string Source, string Text);