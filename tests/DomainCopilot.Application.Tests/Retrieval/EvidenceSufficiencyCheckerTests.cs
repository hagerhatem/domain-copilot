namespace DomainCopilot.Application.Tests.Retrieval;

using DomainCopilot.Application.Retrieval;
using DomainCopilot.Application.Retrieval.Ports;
using DomainCopilot.Domain.Errors;
using DomainCopilot.Domain.Ingestion;

public sealed class EvidenceSufficiencyCheckerTests
{
    // Default options: RrfK=60, RankCutoff=10 => MinFusedScore = 1/70, MinSupportingChunks=2.
    private static readonly EvidenceSufficiencyOptions DefaultOptions = new();

    private static RetrievedChunk MakeChunk(double fusedScore) => new(
        ChunkId: new ChunkId(Guid.NewGuid()),
        DocumentId: new DocumentId(Guid.NewGuid()),
        SourceDocumentFileName: "who_hypertension-guideline_2021-08_v1.pdf",
        GuidelineVersionLabel: "2021-08",
        GuidelineEffectiveDate: new DateTimeOffset(2021, 8, 1, 0, 0, 0, TimeSpan.Zero),
        DocumentVersion: 1,
        Section: "Dosage and Administration",
        PageNumber: 12,
        Text: "Sample retrieved chunk text.",
        FusedScore: fusedScore,
        DenseRank: 1,
        KeywordRank: null);

    [Fact]
    public void Check_NoChunksRetrieved_ReturnsInsufficientWithNoRelevantChunksReason()
    {
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);

        var result = sut.Check("What is the first-line treatment for hypertension?", Array.Empty<RetrievedChunk>());

        Assert.False(result.IsSufficient);
        Assert.NotNull(result.RefusalError);
        Assert.Equal(InsufficientEvidenceReason.NoRelevantChunks, result.RefusalError!.Reason);
        Assert.Empty(result.SupportingChunks);
    }

    [Fact]
    public void Check_ChunksRetrievedButAllBelowThreshold_ReturnsInsufficientWithLowConfidenceReason()
    {
        // Distinguishes "retrieval found nothing" from "retrieval found things, but
        // none of it was relevant enough" — the two must map to different reasons.
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);
        var chunks = new[] { MakeChunk(0.0), MakeChunk(0.001) };

        var result = sut.Check("query", chunks);

        Assert.False(result.IsSufficient);
        Assert.Equal(InsufficientEvidenceReason.LowConfidence, result.RefusalError!.Reason);
    }

    [Fact]
    public void Check_ScoreExactlyAtThreshold_CountsAsSupporting()
    {
        // Boundary: the comparison is >=, so a score exactly equal to MinFusedScore
        // must count. This is the single most important boundary in this class.
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);
        var atThreshold = DefaultOptions.MinFusedScore;
        var chunks = new[] { MakeChunk(atThreshold), MakeChunk(atThreshold) };

        var result = sut.Check("query", chunks);

        Assert.True(result.IsSufficient);
        Assert.Equal(2, result.SupportingChunks.Count);
    }

    [Fact]
    public void Check_ScoreInfinitesimallyBelowThreshold_DoesNotCountAsSupporting()
    {
        // Boundary: the smallest possible violation of >= must still be excluded.
        // Math.BitDecrement gives the actual next-representable-double below the
        // threshold -- subtracting double.Epsilon (the smallest positive double in
        // absolute terms, ~4.9e-324) has no effect here: it's many orders of
        // magnitude too small to change a value around 1/70 once added/subtracted,
        // so MinFusedScore - double.Epsilon silently evaluates back to MinFusedScore
        // itself under IEEE 754 rounding. That was the original bug in this test.
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);
        var justBelow = Math.BitDecrement(DefaultOptions.MinFusedScore);
        var chunks = new[] { MakeChunk(justBelow), MakeChunk(justBelow) };

        var result = sut.Check("query", chunks);

        Assert.False(result.IsSufficient);
        Assert.Equal(InsufficientEvidenceReason.LowConfidence, result.RefusalError!.Reason);
    }

    [Fact]
    public void Check_ExactlyMinSupportingChunksAtThreshold_IsSufficient()
    {
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);
        var chunks = Enumerable.Repeat(DefaultOptions.MinFusedScore, DefaultOptions.MinSupportingChunks)
            .Select(MakeChunk)
            .ToArray();

        var result = sut.Check("query", chunks);

        Assert.True(result.IsSufficient);
        Assert.Equal(DefaultOptions.MinSupportingChunks, result.SupportingChunks.Count);
    }

    [Fact]
    public void Check_OneFewerThanMinSupportingChunks_ReturnsInsufficientWithLowConfidenceReason()
    {
        // Boundary: MinSupportingChunks - 1 qualifying chunks must still refuse, even
        // though every one of them individually cleared the score threshold.
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);
        var chunks = Enumerable.Repeat(DefaultOptions.MinFusedScore, DefaultOptions.MinSupportingChunks - 1)
            .Select(MakeChunk)
            .ToArray();

        var result = sut.Check("query", chunks);

        Assert.False(result.IsSufficient);
        Assert.Equal(InsufficientEvidenceReason.LowConfidence, result.RefusalError!.Reason);
    }

    [Fact]
    public void Check_MoreThanMinSupportingChunksAboveThreshold_ReturnsAllQualifyingChunksNotJustTheMinimum()
    {
        // SupportingChunks should reflect everything that qualified, not be truncated
        // to MinSupportingChunks — downstream (future) LLM grounding wants all of it.
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);
        var chunks = new[]
        {
            MakeChunk(DefaultOptions.MinFusedScore),
            MakeChunk(DefaultOptions.MinFusedScore + 0.01),
            MakeChunk(DefaultOptions.MinFusedScore + 0.02),
        };

        var result = sut.Check("query", chunks);

        Assert.True(result.IsSufficient);
        Assert.Equal(3, result.SupportingChunks.Count);
    }

    [Fact]
    public void Check_MixOfQualifyingAndNonQualifyingChunks_OnlyCountsQualifyingOnesTowardMinimum()
    {
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);
        var chunks = new[]
        {
            MakeChunk(DefaultOptions.MinFusedScore),       // qualifies
            MakeChunk(DefaultOptions.MinFusedScore / 2),   // does not qualify
        };

        var result = sut.Check("query", chunks);

        // Only one chunk actually qualifies; default MinSupportingChunks is 2.
        Assert.False(result.IsSufficient);
        Assert.Equal(InsufficientEvidenceReason.LowConfidence, result.RefusalError!.Reason);
    }

    [Fact]
    public void Check_CustomOptionsWithMinSupportingChunksOfOne_SingleQualifyingChunkIsSufficient()
    {
        var options = new EvidenceSufficiencyOptions { MinSupportingChunks = 1 };
        var sut = new EvidenceSufficiencyChecker(options);
        var chunks = new[] { MakeChunk(options.MinFusedScore) };

        var result = sut.Check("query", chunks);

        Assert.True(result.IsSufficient);
        Assert.Single(result.SupportingChunks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_EmptyOrWhitespaceQuery_ThrowsArgumentException(string query)
    {
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);

        Assert.Throws<ArgumentException>(() => sut.Check(query, Array.Empty<RetrievedChunk>()));
    }

    [Fact]
    public void Check_NullChunkList_ThrowsArgumentNullException()
    {
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);

        Assert.Throws<ArgumentNullException>(() => sut.Check("query", null!));
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new EvidenceSufficiencyChecker(null!));
    }

    [Fact]
    public void RefusalError_MessageMentionsTheOriginalQuery()
    {
        // A clinician reading this refusal needs to see which question was refused,
        // not just a generic "insufficient evidence" string.
        var sut = new EvidenceSufficiencyChecker(DefaultOptions);
        const string query = "What is the maximum daily dose of metformin in renal impairment?";

        var result = sut.Check(query, Array.Empty<RetrievedChunk>());

        Assert.Contains(query, result.RefusalError!.Message);
    }
}
