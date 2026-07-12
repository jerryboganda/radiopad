function withParam(path: string, key: string, value: string): string {
  return `${path}?${key}=${encodeURIComponent(value)}`;
}

export function reportHref(id: string): string {
  return withParam('/reports/view', 'id', id);
}

export function rulebookHref(id: string): string {
  return withParam('/rulebooks/view', 'id', id);
}

export function providerOAuthHref(id: string): string {
  return withParam('/admin/providers/oauth', 'id', id);
}

// Standalone mobile reporting routes were removed: the mobile app is now a
// dictation companion only (see app/(mobile)/companion). Report editing/signing
// happens on the desktop app.

export function rulebookEditorHref(id?: string): string {
  if (id) return withParam('/rulebooks/editor', 'id', id);
  return '/rulebooks/editor';
}