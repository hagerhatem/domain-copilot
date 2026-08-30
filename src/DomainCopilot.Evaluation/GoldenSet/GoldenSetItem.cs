using System.Text.Json.Serialization;

namespace DomainCopilot.Evaluation.GoldenSet;

public enum ExpectedBehavior
{
    /// <summary>Must decline to answer the clinical question (out-of-corpus, ambiguous/insufficient info).</summary>
    Refuse,

    /// <summary>Must answer, but must not overstate precision/certainty - flag risk, request specifics, or note evidence gaps.</summary>
    AnswerWithHedge,

    /// <summary>Must answer confidently and directly; the guideline content is unambiguous.</summary>
    AnswerNormally,

    /// <summary>Not about refusal at all - must complete the task but must still pass the human approval gate
    /// regardless of any instruction embedded in ingested document content.</summary>
    GateComplianceCheck
}

public sealed class GoldenSetItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("expected_answer_summary")]
    public string ExpectedAnswerSummary { get; set; } = "";

    [JsonPropertyName("expected_source_documents")]
    public List<string> ExpectedSourceDocuments { get; set; } = new();

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("source_verification")]
    public string? SourceVerification { get; set; }

    [JsonPropertyName("expected_behavior")]
    public string ExpectedBehaviorRaw { get; set; } = "answer_normally";

    [JsonIgnore]
    public ExpectedBehavior ExpectedBehavior => ExpectedBehaviorRaw switch
    {
        "refuse" => ExpectedBehavior.Refuse,
        "answer_with_hedge" => ExpectedBehavior.AnswerWithHedge,
        "answer_normally" => ExpectedBehavior.AnswerNormally,
        "gate_compliance_check" => ExpectedBehavior.GateComplianceCheck,
        _ => throw new InvalidOperationException(
            $"Golden set item '{Id}' has unrecognized expected_behavior '{ExpectedBehaviorRaw}'. " +
            "Expected one of: refuse | answer_with_hedge | answer_normally | gate_compliance_check.")
    };
}

public sealed class GoldenSetFile
{
    [JsonPropertyName("golden_set")]
    public List<GoldenSetItem> GoldenSet { get; set; } = new();
}
