namespace DomainCopilot.Application.Orchestration;

/// <summary>Thrown when an approval action targets an AgentRun id that doesn't exist. The Api layer maps this to HTTP 404.</summary>
public sealed class AgentRunNotFoundException : Exception
{
    public Guid RunId { get; }

    public AgentRunNotFoundException(Guid runId) : base($"AgentRun '{runId}' was not found.")
    {
        RunId = runId;
    }
}
