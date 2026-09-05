using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainCopilot.Application.Llm;

/// <summary>
/// Explicit, caller-supplied signal for CostAwareLlmRouter's deterministic routing
/// (Twist T3: routing decided by code, never by the LLM itself). The calling agent
/// sets this based on what it is asking the model to do - it is never inferred from
/// the LLM's own output.
/// </summary>
public enum TaskComplexity
{
    Low,
    Moderate,
    High
}
