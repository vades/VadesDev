using System.Security.Cryptography;
using System.Text;
using VadesDev.Rag.Models;
using VadesDev.Rag.Search;

namespace VadesDev.Rag.Tests.Search;

/// <summary>
/// Tests for ChunkHasher: the fingerprint is stable, sensitive to ids, texts and order, and has a fixed format.
/// </summary>
public class ChunkHasherTests
{
    /// <summary>
    /// Creates a chunk with a fixed source, so each test only writes the id and the text.
    /// </summary>
    private static DocumentChunk Make(string id, string text)
    {
        return new DocumentChunk(id, "a.md", text);
    }

    /// <summary>
    /// The same chunks always give the same fingerprint.
    /// </summary>
    [Fact]
    public void Compute_ReturnsSameHashForSameChunks()
    {
        var first = ChunkHasher.Compute([Make("a-1", "Hello"), Make("a-2", "World")]);
        var second = ChunkHasher.Compute([Make("a-1", "Hello"), Make("a-2", "World")]);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// A changed text changes the fingerprint.
    /// </summary>
    [Fact]
    public void Compute_ReturnsDifferentHashWhenTextChanges()
    {
        var original = ChunkHasher.Compute([Make("a-1", "Hello")]);
        var changed = ChunkHasher.Compute([Make("a-1", "Hello!")]);

        Assert.NotEqual(original, changed);
    }

    /// <summary>
    /// A changed id changes the fingerprint.
    /// </summary>
    [Fact]
    public void Compute_ReturnsDifferentHashWhenIdChanges()
    {
        var original = ChunkHasher.Compute([Make("a-1", "Hello")]);
        var changed = ChunkHasher.Compute([Make("a-2", "Hello")]);

        Assert.NotEqual(original, changed);
    }

    /// <summary>
    /// The order of the chunks matters.
    /// </summary>
    [Fact]
    public void Compute_ReturnsDifferentHashWhenOrderChanges()
    {
        var a = Make("a-1", "Hello");
        var b = Make("a-2", "World");

        Assert.NotEqual(ChunkHasher.Compute([a, b]), ChunkHasher.Compute([b, a]));
    }

    /// <summary>
    /// The fingerprint is SHA-256 as lowercase hex: exactly 64 characters from 0-9 and a-f.
    /// </summary>
    [Fact]
    public void Compute_ReturnsLowercaseHexOf64Characters()
    {
        var hash = ChunkHasher.Compute([Make("a-1", "Hello")]);

        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    /// <summary>
    /// No chunks give the well-known SHA-256 of an empty input.
    /// </summary>
    [Fact]
    public void Compute_ReturnsSha256OfEmptyInputForNoChunks()
    {
        var hash = ChunkHasher.Compute([]);

        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    /// <summary>
    /// Moving characters between the id and the text changes the fingerprint, so the NUL separator works.
    /// </summary>
    [Fact]
    public void Compute_DoesNotConfuseIdAndTextBoundaries()
    {
        var first = ChunkHasher.Compute([Make("a", "bc")]);
        var second = ChunkHasher.Compute([Make("ab", "c")]);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The hash input is exactly "id NUL text NUL id NUL text ...". Changing this format would make every
    /// stored file look changed, so all files would be embedded again.
    /// </summary>
    [Fact]
    public void Compute_HashesIdsAndTextsJoinedWithNul()
    {
        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes("a-1\0Hello\0a-2\0World")));

        var hash = ChunkHasher.Compute([Make("a-1", "Hello"), Make("a-2", "World")]);

        Assert.Equal(expected, hash);
    }
}