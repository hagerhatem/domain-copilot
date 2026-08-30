namespace DomainCopilot.Evaluation.Contracts;

/// <summary>
/// TEMPORARY PORT. This mirrors what the real Application-layer retrieval
/// port (e.g. IRetrievalService in DomainCopilot.Application) will expose.
///
/// Once Phase 1/2 (ingestion + retrieval) land, delete this interface and
/// the harness's project-level references to it, add a ProjectReference to
/// DomainCopilot.Application from this .csproj, and point
/// Program.cs at the real adapter instead of Pipeline/StubPipeline.cs.
/// Nothing in GoldenSet/, Metrics/, or Reporting/ should need to change -
/// that boundary is the whole point of this interface existing.
/// </summary>
public interface IRetrievalPipeline
{
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string question, int topK, CancellationToken ct = default);
}
