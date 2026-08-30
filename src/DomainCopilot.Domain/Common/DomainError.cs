namespace DomainCopilot.Domain.Common;

public abstract record DomainError(string Code, string Message);
