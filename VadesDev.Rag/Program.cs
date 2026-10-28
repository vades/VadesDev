using VadesDev.Rag.Configuration;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Knowledge;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;

await using var dataSource = PostgresDataSourceFactory.Create(AppConfiguration.Build());
var store = new PostgresChunkStore(dataSource);
await store.EnsureSchemaAsync();

var all = new DummyKnowledgeBase().GetChunks();
var chunks = all.Where(c => c.Source == "support.md").ToArray();
var embedder = new HashingEmbeddingGenerator();
var index = await new ChunkIndexer(embedder).BuildIndexAsync(chunks);
var hash = ChunkHasher.Compute(chunks);

Console.WriteLine(await store.IsUpToDateAsync("fake", "support.md", hash));        // False: nothing stored yet
await store.ReplaceChunksAsync("fake", hash, index);
Console.WriteLine(await store.IsUpToDateAsync("fake", "support.md", hash));        // True
Console.WriteLine(await store.IsUpToDateAsync("fake", "support.md", "other"));     // False: other fingerprint
Console.WriteLine(await store.IsUpToDateAsync("other-model", "support.md", hash)); // False: other model

try
{
    var twoSources = await new ChunkIndexer(embedder).BuildIndexAsync(all);
    await store.ReplaceChunksAsync("fake", "x", twoSources);
}
catch (ArgumentException exception)
{
    Console.WriteLine(exception.Message);
}