using VadesDev.Rag.Configuration;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Knowledge;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;
using Microsoft.Extensions.AI;

await using var dataSource = PostgresDataSourceFactory.Create(AppConfiguration.Build());
var store = new PostgresChunkStore(dataSource);
await store.EnsureSchemaAsync();

var embedder = new HashingEmbeddingGenerator();
var index = await new ChunkIndexer(embedder).BuildIndexAsync(new DummyKnowledgeBase().GetChunks());

await store.ReplaceChunksAsync("fake-hashing", index);
await store.ReplaceChunksAsync("fake-hashing", index);     // twice: replace must not duplicate
await store.ReplaceChunksAsync("fake-hashing-2", index);   // a second model with the same ids

var queryVector = await embedder.GenerateVectorAsync("What are your support hours?");

foreach (var (chunk, score) in await store.SearchAsync("fake-hashing", queryVector, 3))
{
    Console.WriteLine($"{chunk.Id} {score:F3}");
}

Console.WriteLine($"Other model: {(await store.SearchAsync("fake-hashing-2", queryVector, 10)).Count} rows");
Console.WriteLine($"Unknown model: {(await store.SearchAsync("nothing", queryVector, 10)).Count} rows");