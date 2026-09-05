export interface StepTrace {
  stepIndex: number;
  agentRole: string;
  toolName: string | null;
  status: string;
  input: string;
  output: string | null;
  errorMessage: string | null;
  modelUsed: string | null;
  totalTokens: number;
  estimatedCostUsd: number;
  startedAt: string | null;
  completedAt: string | null;
}

export interface ApprovalTrace {
  decisionType: string;
  clinicianUserId: string;
  editedContent: string | null;
  rejectionReason: string | null;
  decidedAt: string;
}

export interface RunTrace {
  runId: string;
  clinicalCaseId: string;
  correlationId: string;
  status: string;
  terminationReason: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  steps: StepTrace[];
  totalTokens: number;
  totalEstimatedCostUsd: number;
  approval: ApprovalTrace | null;
}