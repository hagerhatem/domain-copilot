using DomainCopilot.Application.Agents;
using DomainCopilot.Application.Agents.GuidelineResearcher;
using DomainCopilot.Application.Llm;
using DomainCopilot.Application.Retrieval;
using DomainCopilot.Application.Retrieval.Ports;
using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;
using DomainCopilot.Infrastructure.Agents;
using Moq;
using Xunit;

namespace DomainCopilot.Application.Tests.Agents;

// Requires: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, Moq
// packages referenced by DomainCopilot.Application.Tests.csproj. Add whichever are
// missing; if this test project targets Application only per Clean Architecture and
// referencing Infrastructure.Agents feels wrong to you, feel free to move this whole
// file to DomainCopilot.Integration.Tests instead - the tests themselves don't care
// which project hosts them, only that they compile against both layers.
public sealed class GuidelineResearcherAgentTests
{
    private static AgentContext ValidContext() => new(Guid.NewGuid(), Guid.NewGuid(), "corr-1");

    [Fact]
    public void GuidelineResearcherInput_RejectsEmptyCaseSummary()
    {
        Assert.Throws<ArgumentException>(() => new GuidelineResearcherInput(""));
        Assert.Throws<ArgumentException>(() => new GuidelineResearcherInput("   "));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsOnNullContext()
    {
        var agent = CreateAgent(Mock.Of<IHybridRetrievalService>(), Mock.Of<ILLMProvider>());
        await Assert.ThrowsAsync<ArgumentNullException>(() => agent.ExecuteAsync(null!, new GuidelineResearcherInput("case")));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsOnNullInput()
    {
        var agent = CreateAgent(Mock.Of<IHybridRetrievalService>(), Mock.Of<ILLMProvider>());
        await Assert.ThrowsAsync<ArgumentNullException>(() => agent.ExecuteAsync(ValidContext(), null!));
    }

    [Fact]
    public void AllowedTools_IsExactlyTheDeclaredSet()
    {
        var agent = CreateAgent(Mock.Of<IHybridRetrievalService>(), Mock.Of<ILLMProvider>());
        Assert.Equal(new[] { AgentTool.SearchCorpus, AgentTool.FetchDocumentById }, agent.AllowedTools);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccessWithMappedExcerpts_WhenEvidenceIsSufficient()
    {
        var chunk1 = MakeChunk("chunk text 1", fusedScore: 1.0);
        var chunk2 = MakeChunk("chunk text 2", fusedScore: 1.0);

        var retrieval = new Mock<IHybridRetrievalService>();
        retrieval.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<RetrievedChunk>>.Success(new[] { chunk1, chunk2 }));

        var agent = CreateAgent(retrieval.Object, Mock.Of<ILLMProvider>());
        var result = await agent.ExecuteAsync(ValidContext(), new GuidelineResearcherInput("case summary"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Output!.Excerpts.Count);
        Assert.Equal(chunk1.Text, result.Output.Excerpts[0].ExcerptText);
        Assert.Equal(chunk1.ChunkId.Value, result.Output.Excerpts[0].ChunkId);
    }

    [Fact]
    public async Task ExecuteAsync_RefusesAfterMaxAttempts_WhenEvidenceStaysInsufficient()
    {
        var retrieval = new Mock<IHybridRetrievalService>();
        retrieval.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<RetrievedChunk>>.Success(Array.Empty<RetrievedChunk>()));

        var llm = new Mock<ILLMProvider>();
        llm.Setup(l => l.CompleteAsync(It.IsAny<CompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompletionResponse("reformulated query", 10, 5, "test-model"));

        var agent = CreateAgent(retrieval.Object, llm.Object);
        var result = await agent.ExecuteAsync(ValidContext(), new GuidelineResearcherInput("case summary"));

        Assert.Equal(AgentOutcome.Refused, result.Outcome);
        Assert.NotNull(result.RefusalReason);
        retrieval.Verify(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        llm.Verify(l => l.CompleteAsync(It.IsAny<CompletionRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailed_OnRetrievalInfrastructureError()
    {
        var retrieval = new Mock<IHybridRetrievalService>();
        retrieval.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<RetrievedChunk>>.Failure(new TestDomainError()));

        var agent = CreateAgent(retrieval.Object, Mock.Of<ILLMProvider>());
        var result = await agent.ExecuteAsync(ValidContext(), new GuidelineResearcherInput("case summary"));

        Assert.Equal(AgentOutcome.Failed, result.Outcome);
        retrieval.Verify(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static GuidelineResearcherAgent CreateAgent(IHybridRetrievalService retrieval, ILLMProvider llm) =>
        new(retrieval, new EvidenceSufficiencyChecker(new EvidenceSufficiencyOptions { MinSupportingChunks = 1 }), llm, topK: 5);

    private static RetrievedChunk MakeChunk(string text, double fusedScore) => new(
        ChunkId: ChunkId.New(),
        DocumentId: DocumentId.New(),
        SourceDocumentFileName: "test.pdf",
        GuidelineVersionLabel: "v1",
        GuidelineEffectiveDate: null,
        DocumentVersion: 1,
        Section: null,
        PageNumber: null,
        Text: text,
        FusedScore: fusedScore,
        DenseRank: 1,
        KeywordRank: 1);
}

internal sealed record TestDomainError() : DomainError("TEST_ERROR", "test failure");
