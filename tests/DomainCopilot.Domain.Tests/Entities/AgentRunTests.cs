using DomainCopilot.Domain.Entities;
using DomainCopilot.Domain.Errors;
using Xunit;

namespace DomainCopilot.Domain.Tests.Entities;

/// <summary>
/// Covers: (a) an AgentRun cannot reach an approved/completed state without first
/// passing through AwaitingApproval, and (c) AgentSteps are recorded in order and
/// exposed to callers without a mutation surface.
///
/// All tests operate purely on in-memory Domain objects — no database, no network,
/// no framework services of any kind.
/// </summary>
public class AgentRunTests
{
    private static AgentRun CreateRunningRun()
    {
        var run = AgentRun.Create(Guid.NewGuid(), Guid.NewGuid(), correlationId: "corr-1");
        run.Start();
        return run;
    }

    // ---------------------------------------------------------------------
    // (a) Cannot transition to an approved state without first being
    //     AwaitingApproval.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(false)] // still Pending
    [InlineData(true)]  // Running, but never asked for approval
    public void ApplyApprovalDecision_WhenNotAwaitingApproval_ThrowsInvalidApprovalStateError(bool started)
    {
        var run = AgentRun.Create(Guid.NewGuid(), Guid.NewGuid(), correlationId: "corr-1");
        if (started)
            run.Start();

        var decision = ApprovalDecision.Approve(run.Id, Guid.NewGuid(), "Draft note content");

        var ex = Assert.Throws<InvalidApprovalStateError>(() => run.ApplyApprovalDecision(decision));

        Assert.Equal(run.Id, ex.RunId);
        Assert.Equal(started ? AgentRunStatus.Running : AgentRunStatus.Pending, ex.CurrentStatus);
        Assert.Null(run.Approval);
        Assert.NotEqual(AgentRunStatus.Completed, run.Status);
    }

    [Fact]
    public void ApplyApprovalDecision_WhenAwaitingApproval_TransitionsToCompleted()
    {
        var run = CreateRunningRun();
        run.RequestApproval();

        var decision = ApprovalDecision.Approve(run.Id, Guid.NewGuid(), "Draft note content");
        run.ApplyApprovalDecision(decision);

        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Same(decision, run.Approval);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public void ApplyApprovalDecision_WithRejectDecision_TransitionsToRejectedNotCompleted()
    {
        var run = CreateRunningRun();
        run.RequestApproval();

        var decision = ApprovalDecision.Reject(run.Id, Guid.NewGuid(), "Draft note content", "Dosage looks wrong");
        run.ApplyApprovalDecision(decision);

        Assert.Equal(AgentRunStatus.Rejected, run.Status);
    }

    [Fact]
    public void ApplyApprovalDecision_CalledTwice_ThrowsInvalidApprovalStateErrorAndKeepsFirstDecision()
    {
        var run = CreateRunningRun();
        run.RequestApproval();

        var firstDecision = ApprovalDecision.Approve(run.Id, Guid.NewGuid(), "Draft note content");
        run.ApplyApprovalDecision(firstDecision);

        // Run is now Completed (terminal) — a second decision must never overwrite the audit trail.
        var secondDecision = ApprovalDecision.Approve(run.Id, Guid.NewGuid(), "Draft note content");

        var ex = Assert.Throws<InvalidApprovalStateError>(() => run.ApplyApprovalDecision(secondDecision));

        Assert.Equal(AgentRunStatus.Completed, ex.CurrentStatus);
        Assert.Same(firstDecision, run.Approval);
    }

    [Fact]
    public void RequestApproval_WhenNotRunning_ThrowsInvalidOperationException()
    {
        var run = AgentRun.Create(Guid.NewGuid(), Guid.NewGuid(), correlationId: "corr-1"); // still Pending

        Assert.Throws<InvalidOperationException>(() => run.RequestApproval());
    }

    // ---------------------------------------------------------------------
    // (c) AgentSteps are recorded in order and exposed immutably.
    // ---------------------------------------------------------------------

    [Fact]
    public void AddStep_MultipleSteps_AreExposedInInsertionOrder()
    {
        var run = CreateRunningRun();
        var step0 = AgentStep.Create(run.Id, 0, AgentRole.GuidelineResearcher, "search corpus for drug X");
        var step1 = AgentStep.Create(run.Id, 1, AgentRole.SafetyChecker, "check interactions for drug X");
        var step2 = AgentStep.Create(run.Id, 2, AgentRole.DocumentationDrafter, "draft note for drug X");

        run.AddStep(step0);
        run.AddStep(step1);
        run.AddStep(step2);

        Assert.Equal(3, run.Steps.Count);
        Assert.Equal(new[] { step0.Id, step1.Id, step2.Id }, run.Steps.Select(s => s.Id));
        Assert.Equal(new[] { 0, 1, 2 }, run.Steps.Select(s => s.StepIndex));
    }

    [Fact]
    public void Steps_AreExposedWithNoMutationSurface()
    {
        var run = CreateRunningRun();
        run.AddStep(AgentStep.Create(run.Id, 0, AgentRole.GuidelineResearcher, "search corpus"));

        // Steps is IReadOnlyList<AgentStep> — there is no compile-time way to Add/Remove
        // through it. Confirm the exposed instance genuinely isn't a plain mutable List<T>.
        Assert.IsAssignableFrom<IReadOnlyList<AgentStep>>(run.Steps);
        Assert.IsNotType<List<AgentStep>>(run.Steps);
    }

    [Fact]
    public void AddStep_WithDuplicateStepIndex_ThrowsInvalidOperationException()
    {
        var run = CreateRunningRun();
        run.AddStep(AgentStep.Create(run.Id, 0, AgentRole.GuidelineResearcher, "first"));

        var duplicate = AgentStep.Create(run.Id, 0, AgentRole.SafetyChecker, "second");

        Assert.Throws<InvalidOperationException>(() => run.AddStep(duplicate));
    }

    [Fact]
    public void AddStep_BelongingToADifferentRun_ThrowsInvalidOperationException()
    {
        var run = CreateRunningRun();
        var stepForAnotherRun = AgentStep.Create(Guid.NewGuid(), 0, AgentRole.GuidelineResearcher, "misdirected step");

        Assert.Throws<InvalidOperationException>(() => run.AddStep(stepForAnotherRun));
    }

    [Fact]
    public void AddStep_BeforeRunIsStarted_ThrowsInvalidOperationException()
    {
        var run = AgentRun.Create(Guid.NewGuid(), Guid.NewGuid(), correlationId: "corr-1"); // Pending, not Running
        var step = AgentStep.Create(run.Id, 0, AgentRole.GuidelineResearcher, "search corpus");

        Assert.Throws<InvalidOperationException>(() => run.AddStep(step));
    }
}
