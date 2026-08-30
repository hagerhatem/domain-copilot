namespace DomainCopilot.Application.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
