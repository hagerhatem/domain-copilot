export interface StepSummary {
  stepIndex: number;
  agentRole: string;
  status: string;
  output: string | null;
}

export interface PendingApprovalRun {
  runId: string;
  clinicalCaseId: string;
  draftSubjective: string | null;
  draftAssessmentAndPlan: string | null;
  steps: StepSummary[];
}