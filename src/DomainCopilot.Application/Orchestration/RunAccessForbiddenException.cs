using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainCopilot.Application.Orchestration;

/// <summary>
/// Raised when an authenticated Clinician attempts to approve/reject/edit-approve
/// or cancel an AgentRun they did not initiate. Distinct from
/// AgentRunNotFoundException (the run genuinely doesn't exist) - here it exists but
/// the caller has no relationship to it (OWASP Web A01 / API1: Broken Object Level
/// Authorization). Maps to HTTP 403, never 404 - a 404 here would leak whether a
/// given run id exists to callers who have no right to know that.
/// </summary>
public sealed class RunAccessForbiddenException : Exception
{
    public Guid RunId { get; }
    public Guid CallerUserId { get; }

    public RunAccessForbiddenException(Guid runId, Guid callerUserId)
        : base($"User {callerUserId} is not authorized to act on run {runId}.")
    {
        RunId = runId;
        CallerUserId = callerUserId;
    }
}