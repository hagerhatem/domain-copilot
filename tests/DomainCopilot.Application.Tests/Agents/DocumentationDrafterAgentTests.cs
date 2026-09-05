using DomainCopilot.Application.Agents;
using DomainCopilot.Application.Agents.DocumentationDrafter;
using DomainCopilot.Application.Agents.GuidelineResearcher;
using DomainCopilot.Application.Agents.SafetyChecker;
using DomainCopilot.Application.Llm;
using DomainCopilot.Infrastructure.Agents;
using Moq;
using Xunit;

namespace DomainCopilot.Application.Tests.Agents;

public sealed class DocumentationDrafterAgentTests
{
    private static AgentContext ValidContext() => new(Guid.NewGuid(), Guid.NewGuid(), "corr-1");

    [Fact]
    public void DocumentationDrafterInput_RejectsEmptyCaseSummary()
    {
        Assert.Throws<ArgumentException>(() =>
            new DocumentationDrafterInput("", Array.Empty<GuidelineExcerpt>(), Array.Empty<SafetyWarning>()));
    }

    [Fact]
    public void AllowedTools_IsExactlyTheDeclaredSet()
    {
        var agent = new DocumentationDrafterAgent(Mock.Of<ILLMProvider>());
        Assert.Equal(new[] { AgentTool.DraftClinicalNote }, agent.AllowedTools);
    }

    [Fact]
    public async Task ExecuteAsync_Refuses_WhenNoGroundingProvidedAtAll_WithoutCallingLLM()
    {
        var llm = new Mock<ILLMProvider>();
        var agent = new DocumentationDrafterAgent(llm.Object);

        var input = new DocumentationDrafterInput("case summary", Array.Empty<GuidelineExcerpt>(), Array.Empty<SafetyWarning>());
        var result = await agent.ExecuteAsync(ValidContext(), input);

        Assert.Equal(AgentOutcome.Refused, result.Outcome);
        llm.Verify(l => l.CompleteAsync(It.IsAny<CompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccessWithParsedSections_WhenModelFollowsFormat()
    {
        var llm = new Mock<ILLMProvider>();
        llm.Setup(l => l.CompleteAsync(It.IsAny<CompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompletionResponse(
                "SUBJECTIVE:\nPatient reports X.\nASSESSMENT AND PLAN:\nContinue current regimen.", 50, 30, "test-model"));

        var agent = new DocumentationDrafterAgent(llm.Object);
        var excerpt = new GuidelineExcerpt(Guid.NewGuid(), Guid.NewGuid(), "Doc", "v1", "excerpt text", 0.5);
        var input = new DocumentationDrafterInput("case summary", new[] { excerpt }, Array.Empty<SafetyWarning>());

        var result = await agent.ExecuteAsync(ValidContext(), input);

        Assert.True(result.IsSuccess);
        Assert.Contains("Patient reports X.", result.Output!.Draft.Subjective);
        Assert.Contains("Continue current regimen.", result.Output.Draft.AssessmentAndPlan);
        llm.Verify(l => l.CompleteAsync(It.IsAny<CompletionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesOnceThenFallsBackGracefully_WhenModelNeverFollowsFormat()
    {
        var llm = new Mock<ILLMProvider>();
        llm.Setup(l => l.CompleteAsync(It.IsAny<CompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompletionResponse("free-form text with no markers", 20, 10, "test-model"));

        var agent = new DocumentationDrafterAgent(llm.Object);
        var excerpt = new GuidelineExcerpt(Guid.NewGuid(), Guid.NewGuid(), "Doc", "v1", "excerpt text", 0.5);
        var input = new DocumentationDrafterInput("case summary", new[] { excerpt }, Array.Empty<SafetyWarning>());

        var result = await agent.ExecuteAsync(ValidContext(), input);

        Assert.True(result.IsSuccess); // graceful degradation, not Failed/Refused
        Assert.Contains("did not follow", result.Output!.Draft.Subjective);
        llm.Verify(l => l.CompleteAsync(It.IsAny<CompletionRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
