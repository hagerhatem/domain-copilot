using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainCopilot.Infrastructure.Common;

using DomainCopilot.Application.Common;

/// <summary>
/// Real-time implementation of IClock. This is the production/DI-registered
/// implementation; unit and integration tests use their own fixed/fake IClock
/// (e.g. IngestionPipelineTests.FixedClock) for determinism, never this one.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
