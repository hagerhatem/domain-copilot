using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DomainCopilot.Application.Agents;
using DomainCopilot.Application.Agents.DocumentationDrafter;
using DomainCopilot.Application.Agents.GuidelineResearcher;
using DomainCopilot.Application.Agents.SafetyChecker;
using DomainCopilot.Application.Common;
using DomainCopilot.Application.CostGovernor.Ports;
using DomainCopilot.Application.Orchestration;
using DomainCopilot.Application.Orchestration.Ports;
using DomainCopilot.Domain.Entities;
using DomainCopilot.Domain.Errors;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DomainCopilot.Application.Tests.Orchestration;

public sealed class RunClinicalWorkflowUseCaseTests
{
    private static RunClinicalWorkflowCommand ValidCommand(Guid userId) => new(
        ClinicalCaseId: Guid.NewGuid(),
        InitiatedByUserId: userId,
        CorrelationId: "corr-1",
        CaseSummary: "case summary",
        ProposedMedication: "med-a",
        CurrentMedications: Array.Empty<string>(),
        PatientContext: null);

    /// <summary>
    /// Prompt 9.3's required proof: a user with zero remaining budget cannot start a
    /// new run, even by calling the use case's ExecuteAsync directly - the same
    /// method any future Api controller/endpoint would call. There is no separate
    /// UI-side check being bypassed here; this IS the only gate.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ThrowsBudgetExceededError_AndNeverStartsAnyAgent_WhenBudgetIsInsufficient()
    {
        var userId = Guid.NewGuid();
        var command = ValidCommand(userId);

        var tokenBudgetService = new Mock<ITokenBudgetService>();
        tokenBudgetService
            .Setup(s => s.HasSufficientBudgetAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        tokenBudgetService
            .Setup(s => s.GetStatusAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenBudgetStatus(
                userId,
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(29),
                100_000,
                100_000,
                0,
                Array.Empty<UsageRecordSummary>()));

        var guidelineResearcher = new Mock<IGuidelineResearcherAgent>();
        var safetyChecker = new Mock<ISafetyCheckerAgent>();
        var documentationDrafter = new Mock<IDocumentationDrafterAgent>();
        var repository = new Mock<IAgentRunRepository>();
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var orchestrator = new PipelineOrchestrator(
    guidelineResearcher.Object,
    safetyChecker.Object,
    documentationDrafter.Object,
    repository.Object,
    clock.Object,
    new OrchestratorOptions(),
    tokenBudgetService.Object,
    Mock.Of<ILogger<PipelineOrchestrator>>());

        var useCase = new RunClinicalWorkflowUseCase(
            orchestrator, tokenBudgetService.Object, new WorkflowCostEstimationOptions(), Mock.Of<ILogger<RunClinicalWorkflowUseCase>>());

        await Assert.ThrowsAsync<BudgetExceededError>(() => useCase.ExecuteAsync(command, CancellationToken.None));

        // Proves the run genuinely never started: no agent - and therefore no LLM
        // call, no retrieval call, no drug-interaction lookup - ever ran.
        guidelineResearcher.Verify(
            a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<GuidelineResearcherInput>(), It.IsAny<CancellationToken>()),
            Times.Never);
        safetyChecker.Verify(
            a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<SafetyCheckerInput>(), It.IsAny<CancellationToken>()),
            Times.Never);
        documentationDrafter.Verify(
            a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<DocumentationDrafterInput>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(r => r.AddAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
