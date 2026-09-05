using DomainCopilot.Application.Agents;
using DomainCopilot.Application.Agents.GuidelineResearcher;
using DomainCopilot.Application.Llm;
using DomainCopilot.Application.Retrieval;
using DomainCopilot.Application.Retrieval.Ports;
using DomainCopilot.Domain.Entities;
using DomainCopilot.Domain.ValueObjects;

namespace DomainCopilot.Infrastructure.Agents;

/// <summary>
/// Concrete IGuidelineResearcherAgent implementation (Prompt 7.3). Lives in
/// Infrastructure per that prompt's explicit instruction, consistent with how
/// IHybridRetrievalService's concrete adapter (RrfHybridRetrievalService) already
/// lives here rather than in Application.
///
/// Termination condition (FR-4/7.3): retries up to MaxSearchAttempts times, stopping
/// as soon as EvidenceSufficiencyChecker reports sufficient evidence. Between
/// attempts, ILLMProvider reformulates the search query - this is this agent's one
/// "reasoning" use of the LLM. The excerpts themselves always come verbatim from
/// retrieval, never from the LLM, so a bad reformulation can only change what gets
/// searched for, never fabricate clinical content into the final output.
/// </summary>
public sealed class GuidelineResearcherAgent : IGuidelineResearcherAgent
{
    private const int MaxSearchAttempts = 3;

    private readonly IHybridRetrievalService _retrievalService;
    private readonly EvidenceSufficiencyChecker _evidenceSufficiencyChecker;
    private readonly ILLMProvider _llmProvider;
    private readonly int _topK;

    public AgentRole Role => AgentRole.GuidelineResearcher;

    public IReadOnlyList<AgentTool> AllowedTools { get; } = new[] { AgentTool.SearchCorpus, AgentTool.FetchDocumentById };

    public GuidelineResearcherAgent(
        IHybridRetrievalService retrievalService,
        EvidenceSufficiencyChecker evidenceSufficiencyChecker,
        ILLMProvider llmProvider,
        int topK = 10)
    {
        _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
        _evidenceSufficiencyChecker = evidenceSufficiencyChecker ?? throw new ArgumentNullException(nameof(evidenceSufficiencyChecker));
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));

        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be positive.");
        _topK = topK;
    }

    public async Task<AgentResult<GuidelineResearcherOutput>> ExecuteAsync(
        AgentContext context, GuidelineResearcherInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        var queryText = input.CaseSummary;
        Domain.Errors.InsufficientEvidenceError? lastRefusal = null;
        var totalUsage = TokenUsage.Zero;
        string? modelUsed = null;

        for (var attempt = 1; attempt <= MaxSearchAttempts; attempt++)
        {
            var retrievalResult = await _retrievalService.RetrieveAsync(new RetrievalQuery(queryText, _topK), cancellationToken);

            if (retrievalResult.IsFailure)
            {
                // Infrastructure failure, not "found nothing" - fail immediately
                // rather than burning further attempts/tokens on a retrieval backend
                // that is down. Retry-with-backoff for this failure class is the
                // orchestrator's job (FR-5), not this agent's.
                return AgentResult<GuidelineResearcherOutput>.Failed(
                    $"Retrieval failed on attempt {attempt}: {retrievalResult.Error!.Message}", totalUsage, modelUsed);
            }

            var sufficiency = _evidenceSufficiencyChecker.Check(queryText, retrievalResult.Value);

            if (sufficiency.IsSufficient)
            {
                var excerpts = sufficiency.SupportingChunks.Select(MapToExcerpt).ToList();
                return AgentResult<GuidelineResearcherOutput>.Success(new GuidelineResearcherOutput(excerpts), totalUsage, modelUsed);
            }

            lastRefusal = sufficiency.RefusalError;

            if (attempt < MaxSearchAttempts)
            {
                var (reformulated, usage, model) = await ReformulateQueryAsync(input.CaseSummary, queryText, attempt, cancellationToken);
                queryText = reformulated;
                totalUsage = TokenUsage.Create(totalUsage.PromptTokens + usage.PromptTokens, totalUsage.CompletionTokens + usage.CompletionTokens);
                modelUsed = model;
            }
        }

        // Exhausted MaxSearchAttempts without sufficient evidence - correct, required
        // refusal (D0's central risk), not a failure.
        return AgentResult<GuidelineResearcherOutput>.Refused(lastRefusal!, totalUsage, modelUsed);
    }

    private async Task<(string ReformulatedQuery, TokenUsage Usage, string ModelUsed)> ReformulateQueryAsync(
        string originalCaseSummary, string previousQuery, int attemptJustFailed, CancellationToken ct)
    {
        var request = new CompletionRequest(new[]
        {
            new ChatMessage("system",
                "You rewrite clinical search queries to find better guideline matches. " +
                "Output ONLY the rewritten query text, nothing else - no explanation, no quotes."),
            new ChatMessage("user",
                $"Original case summary: {originalCaseSummary}\n" +
                $"Previous search query (attempt {attemptJustFailed}, found insufficient evidence): {previousQuery}\n" +
                "Rewrite the query to be more likely to match relevant clinical guideline text - use clinical/" +
                "generic drug names, broaden or narrow terms, or emphasize the core clinical question. Do not " +
                "invent clinical facts not present in the original summary.")
                }, Temperature: 0.3, MaxOutputTokens: 100, Complexity: TaskComplexity.Low);

        var response = await _llmProvider.CompleteAsync(request, ct);
        var reformulated = string.IsNullOrWhiteSpace(response.Text) ? previousQuery : response.Text.Trim();

        return (reformulated, TokenUsage.Create(response.PromptTokens, response.CompletionTokens), response.ModelUsed);
    }

    private static GuidelineExcerpt MapToExcerpt(RetrievedChunk chunk) => new(
        documentId: chunk.DocumentId.Value,
        chunkId: chunk.ChunkId.Value,
        documentTitle: chunk.SourceDocumentFileName,
        documentVersion: chunk.GuidelineVersionLabel ?? $"v{chunk.DocumentVersion}",
        excerptText: chunk.Text,
        relevanceScore: chunk.FusedScore,
        sectionTitle: chunk.Section,
        pageNumber: chunk.PageNumber);
}
