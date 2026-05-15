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

export function mobileDictateHref(reportId: string): string {
  return withParam('/mobile/dictate', 'reportId', reportId);
}

export function mobileReportEditHref(reportId: string): string {
  return withParam('/mobile/reports/edit', 'reportId', reportId);
}

export function mobileReportSignHref(reportId: string): string {
  return withParam('/mobile/reports/sign', 'reportId', reportId);
}

export function rulebookEditorHref(id?: string): string {
  if (id) return withParam('/rulebooks/editor', 'id', id);
  return '/rulebooks/editor';
}