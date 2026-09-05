namespace DomainCopilot.Domain.Ingestion;

public sealed record DocumentId(Guid Value)
{
    public static DocumentId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public sealed class Document
{
    public DocumentId Id { get; private set; } = null!;
    public string SourceKey { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public DocumentFormat Format { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public DocumentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }

    // Supports FR-2's metadata-filtering enhancement (filter retrieval by guideline version/date).
    public string? GuidelineVersionLabel { get; private set; }
    public DateTimeOffset? GuidelineEffectiveDate { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public int ChunkCount { get; private set; }

    private Document() { } // EF Core / serialization

    public static Document CreateNew(
        string sourceKey,
        string fileName,
        DocumentFormat format,
        string contentHash,
        string? guidelineVersionLabel,
        DateTimeOffset? guidelineEffectiveDate,
        DateTimeOffset nowUtc) => new()
        {
            Id = DocumentId.New(),
            SourceKey = sourceKey,
            FileName = fileName,
            Format = format,
            ContentHash = contentHash,
            Version = 1,
            Status = DocumentStatus.Pending,
            GuidelineVersionLabel = guidelineVersionLabel,
            GuidelineEffectiveDate = guidelineEffectiveDate,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

    public void StartNewVersion(string newContentHash, DateTimeOffset nowUtc)
    {
        Version += 1;
        ContentHash = newContentHash;
        Status = DocumentStatus.Pending;
        FailureReason = null;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkStage(DocumentStatus stage, DateTimeOffset nowUtc)
    {
        Status = stage;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkFailed(string reason, DateTimeOffset nowUtc)
    {
        Status = DocumentStatus.Failed;
        FailureReason = reason;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkCompleted(int chunkCount, DateTimeOffset nowUtc)
    {
        Status = DocumentStatus.Completed;
        FailureReason = null;
        ChunkCount = chunkCount;
        UpdatedAtUtc = nowUtc;
    }
}
