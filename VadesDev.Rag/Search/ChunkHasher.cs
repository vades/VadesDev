using System.Security.Cryptography;
using System.Text;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Search;

/// <summary>
/// Computes a fingerprint (SHA-256) of a set of chunks. If a chunk id or text changes, the fingerprint changes.
/// </summary>
public static class ChunkHasher
{
    /// <summary>
    /// Returns the SHA-256 hash of all chunk ids and texts, as a lowercase hex string.
    /// </summary>
    public static string Compute(DocumentChunk[] chunks)
    {
        var text = string.Join('\0', chunks.Select(c => $"{c.Id}\0{c.Text}"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        return Convert.ToHexStringLower(bytes);
    }
}