using VadesDev.Rag.Agents;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Documents;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;

await using var dataSource = PostgresDataSourceFactory.Create(AppConfiguration.Build());
var store = new PostgresChunkStore(dataSource);
await store.EnsureSchemaAsync();

var ingestion = new IngestionPipeline(
    new MarkdownFileReader(), new ChunkIndexer(new HashingEmbeddingGenerator()), store, "fake");
var folderIngestion = new FolderIngestion(ingestion, store, "fake");

var folder = Path.Combine(Path.GetTempPath(), "folder-test");
Directory.CreateDirectory(folder);
var a = Path.Combine(folder, "a.md");
var b = Path.Combine(folder, "b.md");
var c = Path.Combine(folder, "c.md");

File.WriteAllText(a, "# A\n\nText one.");
File.WriteAllText(b, "# B\n\nText two.");
File.WriteAllText(c, "# C\n\nText three.");

Console.WriteLine("--- run 1: three new files");
await folderIngestion.IngestFolderAsync(folder);

Console.WriteLine("--- run 2: unchanged");
await folderIngestion.IngestFolderAsync(folder);

Console.WriteLine("--- run 3: b.md deleted, c.md emptied");
File.Delete(b);
File.WriteAllText(c, "");
await folderIngestion.IngestFolderAsync(folder);

Console.WriteLine("--- run 4: missing folder");
try { await folderIngestion.IngestFolderAsync(Path.Combine(folder, "missing")); }
catch (DirectoryNotFoundException exception) { Console.WriteLine(exception.Message); }

Console.WriteLine("--- run 5: folder empty");
File.Delete(a);
File.Delete(c);
await folderIngestion.IngestFolderAsync(folder);

Directory.Delete(folder);