// Path: VadesDev.Rag/Cli/FolderIngestion.cs
using VadesDev.Rag.Storage;

namespace VadesDev.Rag.Cli;

/// <summary>
/// Ingests every Markdown file of a folder, one file at a time, and afterwards removes the rows
/// of files that were deleted from the folder or became empty.
/// </summary>
public class FolderIngestion
{
    private const string FilePattern = "*.md";

    private readonly IngestionPipeline _pipeline;
    private readonly PostgresChunkStore _store;
    private readonly string _embeddingModel;

    public FolderIngestion(IngestionPipeline pipeline, PostgresChunkStore store, string embeddingModel)
    {
        _pipeline = pipeline;
        _store = store;
        _embeddingModel = embeddingModel;
    }

    /// <summary>
    /// Ingests all files of the folder, then removes the rows of sources that are no longer there.
    /// Throws a clear error if the folder does not exist, before anything is changed.
    /// </summary>
    public async Task IngestFolderAsync(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Knowledge folder not found: {folder}");
        }

        var keepSources = await IngestFilesAsync(FindFiles(folder));

        await RemoveMissingAsync(keepSources);
    }

    /// <summary>
    /// Returns the paths of the files in the folder (top level only), sorted by name.
    /// </summary>
    private static string[] FindFiles(string folder)
    {
        return [.. Directory.GetFiles(folder, FilePattern, SearchOption.TopDirectoryOnly)
            .OrderBy(file => file, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Ingests the files in order and returns the source names of the files that have content.
    /// The source name is the file name, built the same way as in MarkdownFileReader, so both must stay equal.
    /// </summary>
    private async Task<string[]> IngestFilesAsync(string[] files)
    {
        var keepSources = new List<string>();

        foreach (var file in files)
        {
            if (await _pipeline.IngestAsync(file))
            {
                keepSources.Add(Path.GetFileName(file));
            }
        }

        return [.. keepSources];
    }

    /// <summary>
    /// Deletes the rows of this embedding model whose source is not in the list, and prints how many were removed.
    /// </summary>
    private async Task RemoveMissingAsync(string[] keepSources)
    {
        var removed = await _store.DeleteSourcesExceptAsync(_embeddingModel, keepSources);

        if (removed > 0)
        {
            Console.WriteLine($"Removed {removed} row(s) of deleted or empty files.");
        }
    }
}