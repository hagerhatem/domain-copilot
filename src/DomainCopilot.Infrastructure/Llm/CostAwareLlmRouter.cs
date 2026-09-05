using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DomainCopilot.Application.Llm;
using Microsoft.Extensions.DependencyInjection;

namespace DomainCopilot.Infrastructure.Llm;

/// <summary>
/// Deterministic cost-aware router (Twist T3, Prompt 9.2): wraps two ILLMProvider
/// instances - "cheap" and "strong" - and picks between them based solely on the
/// caller-supplied CompletionRequest.Complexity. The LLM itself never decides which
/// model handles a request; that would defeat the point of a Cost Governor twist.
///
/// STATUS as of Phase 9.2: both "cheap" and "strong" are registered against the
/// SAME local OllamaLLMProvider in Program.cs (no hosted provider exists yet - see
/// ILLMProvider's XML docs). The routing logic below is fully real; it starts
/// actually saving cost the moment a hosted "strong" provider is wired in, with
/// zero changes needed to this class.
///
/// Mapping rule (this simplifies ADR-004's fuller per-agent/sub-task table down to
/// a complexity-only rule, per what Prompt 9.2 asks for; ADR-004 remains the full
/// reference):
///   - TaskComplexity.Low      -> cheap   (GuidelineResearcher: corpus search /
///                                retrieval only, produces no clinical judgment)
///   - TaskComplexity.Moderate -> cheap   (reserved for SafetyChecker per ADR-004's
///                                fuller table, but SafetyCheckerAgent currently has
///                                NO ILLMProvider dependency at all - its
///                                interaction/contraindication verdict comes
///                                entirely from the deterministic
///                                SqlDrugInteractionLookup, by deliberate design (see
///                                that class's XML doc). No CompletionRequest is
///                                ever actually constructed with this complexity
///                                today; this mapping exists for if/when a bounded
///                                LLM explanation step is added to that agent later)
///   - TaskComplexity.High     -> strong  (DocumentationDrafter: produces the
///                                actual clinical note text a Clinician will
///                                approve/reject/edit - the single highest-stakes
///                                LLM output in the entire pipeline)
///
/// GuidelineResearcherAgent now passes TaskComplexity.Low and
/// DocumentationDrafterAgent now passes TaskComplexity.High explicitly (see each
/// agent's ExecuteAsync). SafetyCheckerAgent passes nothing because it never
/// constructs a CompletionRequest at all. End-to-end routing for Phase 9.2 is
/// correct as of this revision.
/// </summary>
public sealed class CostAwareLlmRouter : ILLMProvider
{
    public const string CheapProviderKey = "cheap";
    public const string StrongProviderKey = "strong";

    private readonly ILLMProvider _cheap;
    private readonly ILLMProvider _strong;

    public CostAwareLlmRouter(
        [FromKeyedServices(CheapProviderKey)] ILLMProvider cheap,
        [FromKeyedServices(StrongProviderKey)] ILLMProvider strong)
    {
        _cheap = cheap ?? throw new ArgumentNullException(nameof(cheap));
        _strong = strong ?? throw new ArgumentNullException(nameof(strong));
    }

    public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default) =>
        Route(request).CompleteAsync(request, cancellationToken);

    public IAsyncEnumerable<StreamToken> StreamAsync(CompletionRequest request, CancellationToken cancellationToken = default) =>
        Route(request).StreamAsync(request, cancellationToken);

    public Task<ToolCallResponse> CallToolAsync(ToolCallRequest request, CancellationToken cancellationToken = default) =>
        Route(request.Completion).CallToolAsync(request, cancellationToken);

    public Task<EmbedResponse> EmbedAsync(EmbedRequest request, CancellationToken cancellationToken = default) =>
        // Embeddings carry no TaskComplexity concept and are actually served through
        // the separate IEmbeddingService (see Program.cs), not through ILLMProvider,
        // anywhere else in this codebase. Routed to "cheap" here only so this method
        // is never a dead throw if something calls it via ILLMProvider directly.
        _cheap.EmbedAsync(request, cancellationToken);

    private ILLMProvider Route(CompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Complexity switch
        {
            TaskComplexity.Low => _cheap,
            TaskComplexity.Moderate => _cheap,
            TaskComplexity.High => _strong,
            _ => throw new ArgumentOutOfRangeException(
                nameof(request), request.Complexity, "Unhandled TaskComplexity value.")
        };
    }

}