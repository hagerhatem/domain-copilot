export interface RunHistoryItem {
  runId: string;
  clinicalCaseId: string;
  status: string;
  createdAt: string;
  completedAt: string | null;
  terminationReason: string | null;
}