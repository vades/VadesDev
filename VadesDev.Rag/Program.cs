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

var path = Path.Combine(Path.GetTempPath(), "ingest-test.md");

File.WriteAllText(path, "# A\n\nText one.\n\n# B\n\nText two.");
Console.WriteLine(await ingestion.IngestAsync(path));

Console.WriteLine(await ingestion.IngestAsync(path));

File.WriteAllText(path, "# A\n\nText one changed.\n\n# B\n\nText two.");
Console.WriteLine(await ingestion.IngestAsync(path));

File.WriteAllText(path, "");
Console.WriteLine(await ingestion.IngestAsync(path));

Console.WriteLine(await store.DeleteSourcesExceptAsync("fake", []));

File.Delete(path);