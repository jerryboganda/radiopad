import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import * as React from 'react';

const statusMock = vi.fn();
const accountMock = vi.fn();
const adminSettingsMock = vi.fn();
const adminStatusMock = vi.fn();
const adminDiagnosticsMock = vi.fn();
const adminQuotasMock = vi.fn();
const adminUsageMock = vi.fn();
const entitlementMock = vi.fn();
const beginAuthMock = vi.fn();
const linkLocalCliMock = vi.fn();
const revokeAccountMock = vi.fn();
const previewContextMock = vi.fn();
const startSessionMock = vi.fn();
const chatMock = vi.fn();

// Full permission catalog (from frontend/lib/permissions.ts PermissionKey union).
// CopilotAdminPage wraps its content in <PermissionGate permission="prompt_overrides.manage">,
// and components on both pages call usePermissions() -> api.me(). Grant every key so the
// admin gate opens and the user page's permission probe resolves cleanly.
const ALL_PERMISSIONS = [
  'reports.read', 'reports.draft', 'reports.edit', 'reports.validate', 'reports.sign', 'reports.export',
  'rulebooks.read', 'rulebooks.manage', 'rulebooks.approve',
  'templates.read', 'templates.manage', 'templates.approve',
  'providers.read', 'providers.manage',
  'audit.read', 'audit.verify', 'audit.export',
  'users.read', 'users.manage', 'users.revoke_sessions',
  'billing.read', 'billing.manage',
  'security.manage', 'tenant_settings.manage',
  'validation_packs.read', 'validation_packs.manage', 'validation_packs.run',
  'mcp_tools.invoke', 'mcp_tools.manage',
  'prompt_overrides.manage', 'prompt_overrides.approve',
];

vi.mock('@/lib/api', () => ({
  api: {
    me: vi.fn(async () => ({
      user: { permissions: ALL_PERMISSIONS, role: 0, roleName: 'MedicalDirector' },
    })),
    copilot: {
      status: () => statusMock(),
      account: () => accountMock(),
      entitlement: () => entitlementMock(),
      beginAuth: (body: unknown) => beginAuthMock(body),
      linkLocalCli: (body: unknown) => linkLocalCliMock(body),
      revokeAccount: () => revokeAccountMock(),
      previewContext: (body: unknown) => previewContextMock(body),
      startSession: (body: unknown) => startSessionMock(body),
      chat: (body: unknown) => chatMock(body),
      admin: {
        settings: () => adminSettingsMock(),
        status: () => adminStatusMock(),
        diagnostics: () => adminDiagnosticsMock(),
        quotas: () => adminQuotasMock(),
        usage: () => adminUsageMock(),
        saveQuotas: (body: unknown) => Promise.resolve(body),
      },
    },
  },
}));

import CopilotPage from '@/app/copilot/page';
import CopilotAdminPage from '@/app/admin/copilot/page';

const status = {
  enabled: false,
  emergencyDisabled: true,
  defaultMode: 'Disabled',
  runtimeStatus: 'Disabled',
  kind: 'copilot_disabled',
  message: 'Copilot is fail-closed.',
  allowedModes: ['Disabled'],
  phiBlocked: true,
  promptLoggingEnabled: false,
  contextLoggingEnabled: false,
  gitHubHost: 'github.com',
  gitHubOrganization: '',
  unsupportedFeatures: ['IDE token scraping', 'frontend or IPC token exposure'],
};

const settings = {
  enabled: false,
  emergencyDisabled: true,
  defaultMode: 'Disabled',
  allowedModes: ['Disabled'],
  gitHubEnterpriseSlug: '',
  gitHubOrganization: '',
  gitHubHost: 'github.com',
  sdkRuntimeEnabled: false,
  cliRuntimeEnabled: false,
  allowByoAccounts: false,
  allowEnvironmentTokenAuth: false,
  requireOsKeychainForCli: true,
  promptLoggingEnabled: false,
  contextLoggingEnabled: false,
  retentionPolicy: 'metadata_only',
  policyJson: '{"phi":"blocked"}',
  gitHubAppId: '',
  gitHubAppInstallationId: '',
  oAuthClientId: '',
  gitHubAppPrivateKeyConfigured: false,
  oAuthClientSecretConfigured: false,
  gitHubAppPrivateKeySecretRef: '',
  oAuthClientSecretRef: '',
};

describe('Copilot pages', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    statusMock.mockResolvedValue(status);
    accountMock.mockResolvedValue({
      mode: 'Disabled',
      gitHubLogin: '',
      tokenStatus: 'none',
      ssoStatus: 'unknown',
      seatStatus: 'unknown',
      denialReason: 'copilot_disabled',
      lastAuthenticatedAt: null,
      revokedAt: null,
      entitlementAllowed: false,
      entitlementSource: 'tenant_policy',
    });
    entitlementMock.mockResolvedValue({
      allowed: false,
      mode: 'Disabled',
      source: 'tenant_policy',
      gitHubLogin: '',
      ssoStatus: 'unknown',
      seatStatus: 'unknown',
      denialReason: 'copilot_disabled',
      checkedAt: new Date().toISOString(),
      expiresAt: null,
    });
    adminSettingsMock.mockResolvedValue(settings);
    adminStatusMock.mockResolvedValue(status);
    adminDiagnosticsMock.mockResolvedValue({ runId: 'diag-1', status, results: { kind: status.kind } });
    adminQuotasMock.mockResolvedValue([
      { scopeType: 'tenant', scopeKey: '', feature: 'chat', windowSeconds: 3600, maxRequests: 100, maxConcurrent: 5, enabled: true },
    ]);
    adminUsageMock.mockResolvedValue({ total: 0, completed: 0, blocked: 0, failed: 0, cancelled: 0, running: 0, byStatus: [] });
  });

  it('renders user Copilot page as fail-closed without exposing token fields', async () => {
    render(<CopilotPage />);

    expect(await screen.findByText('GitHub Copilot')).toBeTruthy();
    expect(screen.getByText(/Runtime not ready/)).toBeTruthy();
    expect(screen.getByText(/PHI routing/)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Start Copilot chat' })).toHaveProperty('disabled', true);
    expect(screen.queryByText(/gho_|ghu_|github_pat_/)).toBeNull();
  });

  it('renders admin Copilot controls with locked input classes', async () => {
    const { container } = render(<CopilotAdminPage />);

    await waitFor(() => expect(adminSettingsMock).toHaveBeenCalled());
    expect(screen.getByText(/Fail-closed/)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Save Copilot settings' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Save quotas' })).toBeTruthy();
    expect(container.querySelectorAll('.rp-input').length).toBeGreaterThanOrEqual(8);
    expect(screen.getByText(/Secret fields are write-only references/)).toBeTruthy();
  });
});
