namespace VadesDev.Rag.Models;

/// <summary>
/// The result of reading one file: the file name and its text.
/// </summary>
public record SourceDocument(string Source, string Text);