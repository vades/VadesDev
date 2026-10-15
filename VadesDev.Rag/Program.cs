using System.Numerics.Tensors;
using Microsoft.Extensions.AI;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Knowledge;
using VadesDev.Rag.Models;

var embedder = new HashingEmbeddingGenerator();
var vector = await embedder.GenerateVectorAsync("What are your support hours?");
Console.WriteLine(vector.Length);
Console.WriteLine(TensorPrimitives.Norm(vector.Span));

var empty = await embedder.GenerateVectorAsync("What is the?");
Console.WriteLine(TensorPrimitives.Norm(empty.Span));

/*Console.WriteLine(Fnv1AHasher.Hash(""));
Console.WriteLine(Fnv1AHasher.Hash("a"));*/

/*var words = TextTokenizer.Tokenize("What are your support hours?");
Console.WriteLine(string.Join(", ", words));
Console.WriteLine(string.Join(", ", TextTokenizer.Tokenize("Standard shipping takes 3 to 5 business days within the EU.")));*/

/*var knowledgeBase = new DummyKnowledgeBase();
var chunks = knowledgeBase.GetChunks();
Console.WriteLine(chunks.Length);
Console.WriteLine(chunks[0]);*/

/*var chunk = new DocumentChunk("faq-1", "refund-policy.md", "Test");
var indexed = new IndexedChunk(chunk, new float[] { 0.1f, 0.2f, 0.3f });
var hit = new SearchHit(chunk, 0.85f);
var (c, score) = hit;

var defaults = new RagSettings();
var custom = new RagSettings(TopK: 3);
Console.WriteLine(defaults);
Console.WriteLine(custom.TopK);

Console.WriteLine(indexed.Chunk.Id);
Console.WriteLine(indexed.Vector.Length);
Console.WriteLine($"{c.Id} {score:F3}");*/