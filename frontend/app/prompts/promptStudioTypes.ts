/** Shared local types for the Prompt Studio module. */

export type PromptOverride = {
  id: string;
  rulebookId: string;
  blockKey: string;
  body: string;
  status: 'Draft' | 'Approved';
  approvedByUserId: string | null;
  approvedAt: string | null;
  updatedAt: string;
};

export type TabId = 'test' | 'diff' | 'golden' | 'approval';
