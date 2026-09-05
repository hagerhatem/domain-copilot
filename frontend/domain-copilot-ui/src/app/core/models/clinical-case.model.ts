export interface CreateClinicalCaseRequest {
  caseReference: string;
  presentingComplaint: string;
  proposedMedication: string;
  patientContext: string | null;
  currentMedications: string[];
}

export interface ClinicalCaseSummary {
  id: string;
  caseReference: string;
  presentingComplaint: string;
  proposedMedication: string;
  createdAt: string;
}