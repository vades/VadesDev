namespace VadesDev.Rag.Embeddings;

/// <summary>
/// Stable FNV-1a hash: the same word gives the same number on every run (string.GetHashCode() changes per process).
/// </summary>
public static class Fnv1AHasher
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    /// <summary>
    /// Returns the FNV-1a hash of a text.
    /// </summary>
    public static uint Hash(string text)
    {
        uint hash = FnvOffsetBasis;

        foreach (char c in text)
        {
            hash = (hash ^ c) * FnvPrime;
        }

        return hash;
    }
}