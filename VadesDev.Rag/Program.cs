using VadesDev.Rag.Configuration;
using VadesDev.Rag.Models;

var chunk = new DocumentChunk("faq-1", "refund-policy.md", "Test");
var indexed = new IndexedChunk(chunk, new float[] { 0.1f, 0.2f, 0.3f });
var hit = new SearchHit(chunk, 0.85f);
var (c, score) = hit;

var defaults = new RagSettings();
var custom = new RagSettings(TopK: 3);
Console.WriteLine(defaults);
Console.WriteLine(custom.TopK);

Console.WriteLine(indexed.Chunk.Id);
Console.WriteLine(indexed.Vector.Length);
Console.WriteLine($"{c.Id} {score:F3}");