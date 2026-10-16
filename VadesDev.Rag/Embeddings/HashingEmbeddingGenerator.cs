using System.Numerics.Tensors;
using Microsoft.Extensions.AI;

namespace VadesDev.Rag.Embeddings;

/// <summary>
/// Fake embedding model: "hashed bag of words". Each word is hashed to one of 4096 slots and counted.
/// Similar WORDS give similar vectors (real models also match similar MEANING).
/// </summary>
public sealed class HashingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dimensions = 4096;

    /// <summary>
    /// Creates one embedding for each text, in the same order.
    /// </summary>
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var text in values)
        {
            result.Add(new Embedding<float>(Embed(text)));
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Builds the vector of one text: counts each word in its hashed slot, then scales the vector to length 1.
    /// </summary>
    private static float[] Embed(string text)
    {
        var vector = new float[Dimensions];

        foreach (var word in TextTokenizer.Tokenize(text))
        {
            var slot = (int)(Fnv1AHasher.Hash(word) % Dimensions);
            vector[slot] += 1f;
        }

        ScaleToUnitLength(vector);
        return vector;
    }

    /// <summary>
    /// L2 normalization: scales the vector to length 1, so the dot product equals the cosine similarity.
    /// </summary>
    private static void ScaleToUnitLength(float[] vector)
    {
        float norm = TensorPrimitives.Norm<float>(vector);

        if (norm > 0)
        {
            TensorPrimitives.Divide(vector, norm, vector);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}