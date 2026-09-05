export interface AskRequest {
  question: string;
  topK?: number;
}

export interface Citation {
  sourceDocumentFileName: string;
  section: string | null;
  pageNumber: number | null;
  text: string;
  fusedScore: number;
}

export interface AskResponse {
  question: string;
  citations: Citation[];
}

export interface InsufficientEvidenceDto {
  code: string;
  message: string;
  query: string;
  reason: 'NoRelevantChunks' | 'ConflictingSources' | 'LowConfidence' | 'OutOfCorpus';
}