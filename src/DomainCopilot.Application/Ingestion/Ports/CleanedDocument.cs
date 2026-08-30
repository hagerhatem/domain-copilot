namespace DomainCopilot.Application.Ingestion.Ports;

public sealed record CleanedSection(string? SectionTitle, int? PageNumber, string Text);

public sealed record CleanedDocument(IReadOnlyList<CleanedSection> Sections)
{
    public string FullText => string.Join("\n\n", Sections.Select(s => s.Text));
}
