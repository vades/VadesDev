using VadesDev.Rag.Models;
using VadesDev.Rag.Search;

var a = new DocumentChunk("a-1", "a.md", "Hello");
var b = new DocumentChunk("a-2", "a.md", "World");

Console.WriteLine(ChunkHasher.Compute([a, b]));
Console.WriteLine(ChunkHasher.Compute([a, b]) == ChunkHasher.Compute([a, b]));                          // same input
Console.WriteLine(ChunkHasher.Compute([a, b]) == ChunkHasher.Compute([b, a]));                          // other order
Console.WriteLine(ChunkHasher.Compute([a, b]) == ChunkHasher.Compute([a, b with { Text = "World!" }])); // other text
Console.WriteLine(ChunkHasher.Compute([]));