using DomainCopilot.Application.Agents;
using DomainCopilot.Application.Agents.SafetyChecker;
using DomainCopilot.Application.Retrieval.Ports;
using DomainCopilot.Domain.Common;
using DomainCopilot.Infrastructure.Agents;
using Moq;
using Xunit;

namespace DomainCopilot.Application.Tests.Agents;

public sealed class SafetyCheckerAgentTests
{
    private static AgentContext ValidContext() => new(Guid.NewGuid(), Guid.NewGuid(), "corr-1");

    [Fact]
    public void SafetyCheckerInput_RejectsEmptyProposedMedication()
    {
        Assert.Throws<ArgumentException>(() => new SafetyCheckerInput("", new List<string>()));
    }

    [Fact]
    public void SafetyCheckerInput_RejectsNullCurrentMedications()
    {
        Assert.Throws<ArgumentNullException>(() => new SafetyCheckerInput("metformin", null!));
    }

    [Fact]
    public void AllowedTools_IsExactlyTheDeclaredSet_AndDoesNotIncludeAnyLLMTool()
    {
        var agent = CreateAgent(Mock.Of<IDrugInteractionLookup>(), Mock.Of<IHybridRetrievalService>());
        Assert.Equal(new[] { AgentTool.LookupDrugInteraction, AgentTool.SearchCorpus }, agent.AllowedTools);
    }

    [Fact]
    public async Task ExecuteAsync_Refuses_WhenProposedMedicationUnrecognized_AndSkipsRetrieval()
    {
        var lookup = new Mock<IDrugInteractionLookup>();
        lookup.Setup(l => l.Check("glucanorex", It.IsAny<IReadOnlyList<string>>()))
            .Returns(new DrugInteractionLookupResult(false, Array.Empty<DrugInteractionFinding>()));

        var retrieval = new Mock<IHybridRetrievalService>();
        var agent = CreateAgent(lookup.Object, retrieval.Object);

        var result = await agent.ExecuteAsync(ValidContext(), new SafetyCheckerInput("glucanorex", new[] { "metformin" }));

        Assert.Equal(AgentOutcome.Refused, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsDeterministicFindings_WithFullConfidence_NeverFromLLM()
    {
        var finding = new DrugInteractionFinding("hyperkalemia risk", SafetyWarningSeverity.Caution, "RULE_TEST");
        var lookup = new Mock<IDrugInteractionLookup>();
        lookup.Setup(l => l.Check("spironolactone", It.IsAny<IReadOnlyList<string>>()))
            .Returns(new DrugInteractionLookupResult(true, new[] { finding }));

        var retrieval = new Mock<IHybridRetrievalService>();
        retrieval.Setup(r => r.RetrieveAsync(It.IsAny<RetrievalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<RetrievedChunk>>.Success(Array.Empty<RetrievedChunk>()));

        var agent = CreateAgent(lookup.Object, retrieval.Object);
        var result = await agent.ExecuteAsync(ValidContext(), new SafetyCheckerInput("spironolactone", new[] { "lisinopril" }));

        Assert.True(result.IsSuccess);
        var warning = Assert.Single(result.Output!.Warnings);
        Assert.Equal(SafetyWarningSource.DeterministicLookup, warning.Source);
        Assert.Equal("RULE_TEST", warning.SourceReferenceId);
        Assert.Equal(1.0, warning.Confidence);
    }

    private static SafetyCheckerAgent CreateAgent(IDrugInteractionLookup lookup, IHybridRetrievalService retrieval) =>
        new(lookup, retrieval);
}
