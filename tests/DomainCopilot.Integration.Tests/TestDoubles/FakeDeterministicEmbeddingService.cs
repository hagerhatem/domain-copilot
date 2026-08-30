namespace DomainCopilot.Integration.Tests.TestDoubles;

using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Domain.Common;

/// <summary>
/// TEST DOUBLE ONLY — never reference this from Infrastructure or Program.cs.
///
/// Produces a deterministic embedding via a hashed bag-of-words vector, L2-normalized.
/// Texts sharing more words get higher cosine similarity, which is enough to make
/// assertion (c) — "a known phrase returns a relevant chunk" — meaningful without any
/// network call, API key, or non-determinism. It does NOT validate real semantic
/// retrieval quality; that's what FR-3's evaluation harness is for, against the real
/// IEmbeddingService implementation once it exists.
/// </summary>
public sealed class FakeDeterministicEmbeddingService : IEmbeddingService
{
    public int Dimensions { get; }

    public FakeDeterministicEmbeddingService(int dimensions = 256)
    {
        Dimensions = dimensions;
    }

    public Task<Result<IReadOnlyList<float[]>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var vectors = texts.Select(Embed).ToList();
        return Task.FromResult(Result<IReadOnlyList<float[]>>.Success(vectors));
    }

    public float[] Embed(string text)
    {
        var vector = new float[Dimensions];

        var words = text
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '(', ')', '"', '\'' },
                StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            var bucket = Math.Abs(word.GetHashCode()) % Dimensions;
            vector[bucket] += 1f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;

        return vector;
    }
}
