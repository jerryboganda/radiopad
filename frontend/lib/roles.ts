/**
 * Iter-36 — RBAC helpers shared by the governance + model-eval admin
 * dashboards. Mirrors `RadioPad.Domain.UserRole` (see
 * `backend/.../Enums/Enums.cs`). The backend remains the source of
 * truth; these helpers only gate which UI affordances are visible.
 */

export const UserRole = {
  Radiologist: 0,
  ReportingAdmin: 1,
  MedicalDirector: 2,
  ComplianceReviewer: 3,
  ItAdmin: 4,
  BillingAdmin: 5,
} as const;

export type UserRoleValue = (typeof UserRole)[keyof typeof UserRole];

export function isMedicalDirector(role: number | undefined | null): boolean {
  return role === UserRole.MedicalDirector;
}

export function isComplianceReviewer(role: number | undefined | null): boolean {
  return role === UserRole.ComplianceReviewer;
}

export function isItAdmin(role: number | undefined | null): boolean {
  return role === UserRole.ItAdmin;
}

/** Governance dashboard — Medical Director, Compliance Reviewer, IT Admin. */
export function canViewGovernance(role: number | undefined | null): boolean {
  return (
    role === UserRole.MedicalDirector ||
    role === UserRole.ComplianceReviewer ||
    role === UserRole.ItAdmin
  );
}

/** Model-eval dashboard — Medical Director, Compliance Reviewer. */
export function canViewModelEval(role: number | undefined | null): boolean {
  return role === UserRole.MedicalDirector || role === UserRole.ComplianceReviewer;
}

/** Promote-to-production action — Medical Director only. */
export function canPromoteRulebook(role: number | undefined | null): boolean {
  return role === UserRole.MedicalDirector;
}

export const ROLE_LABELS: Record<number, string> = {
  0: 'Radiologist',
  1: 'Reporting Admin',
  2: 'Medical Director',
  3: 'Compliance Reviewer',
  4: 'IT Admin',
  5: 'Billing Admin',
};
