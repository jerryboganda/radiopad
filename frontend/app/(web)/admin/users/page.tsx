'use client';

import { useEffect, useMemo, useState } from 'react';
import { api, type UserRow } from '@/lib/api';
import { isAuthError, useAuthSession } from '@/lib/useAuthSession';
import SignInRequired from '@/components/ui/SignInRequired';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import Banner from '@/components/ui/Banner';

function humanizeRole(role: string): string {
  return role.replace(/([a-z])([A-Z])/g, '$1 $2');
}

type Credential = { kind: 'created' | 'reset'; email: string; tempPassword: string };

export default function UsersAdminPage() {
  const session = useAuthSession();
  const [rows, setRows] = useState<UserRow[] | null>(null);
  const [roles, setRoles] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [authBlocked, setAuthBlocked] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [credential, setCredential] = useState<Credential | null>(null);

  // Add-user form.
  const [showAdd, setShowAdd] = useState(false);
  const [newEmail, setNewEmail] = useState('');
  const [newName, setNewName] = useState('');
  const [newRole, setNewRole] = useState('Radiologist');
  const [newTempPw, setNewTempPw] = useState('');
  const [adding, setAdding] = useState(false);

  async function reload() {
    try {
      const [list, rolesResp] = await Promise.all([api.users.list(), api.users.roles().catch(() => ({ roles: [] }))]);
      setRows(list);
      if (rolesResp.roles.length) {
        const values = rolesResp.roles.map((r) => r.value);
        setRoles(values);
        if (!values.includes(newRole)) setNewRole(values[0] ?? 'Radiologist');
      }
      setAuthBlocked(false);
    } catch (e) {
      const err = e as Error & { status?: number };
      if (isAuthError(err) || err.status === 403) setAuthBlocked(true);
      else setError(err.message);
    }
  }

  useEffect(() => {
    if (session.loading || session.signedOut) return;
    void reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session.loading, session.signedOut]);

  const sorted = useMemo(
    () => (rows ? [...rows].sort((a, b) => a.email.localeCompare(b.email)) : null),
    [rows],
  );

  function flash(message: string) {
    setInfo(message);
    setError(null);
  }

  async function withRow<T>(id: string, fn: () => Promise<T>): Promise<T | undefined> {
    setBusyId(id);
    setError(null);
    setInfo(null);
    try {
      return await fn();
    } catch (e) {
      setError((e as Error & { body?: { error?: string } }).body?.error || (e as Error).message);
      return undefined;
    } finally {
      setBusyId(null);
    }
  }

  async function changeRole(u: UserRow, role: string) {
    await withRow(u.id, async () => {
      await api.users.update(u.id, { role });
      await reload();
      flash(`${u.email} is now ${humanizeRole(role)}.`);
    });
  }

  async function toggleActive(u: UserRow) {
    await withRow(u.id, async () => {
      if (u.isActive) await api.users.lockout(u.id);
      else await api.users.unlock(u.id);
      await reload();
      flash(`${u.email} ${u.isActive ? 'deactivated' : 'reactivated'}.`);
    });
  }

  async function revokeSessions(u: UserRow) {
    await withRow(u.id, async () => {
      await api.users.revokeSessions(u.id);
      flash(`Signed ${u.email} out of all sessions.`);
    });
  }

  async function resetPassword(u: UserRow) {
    await withRow(u.id, async () => {
      const r = await api.users.resetPassword(u.id);
      setCredential({ kind: 'reset', email: u.email, tempPassword: r.tempPassword });
      flash(`Temporary password issued for ${u.email}.`);
    });
  }

  async function resetMfa(u: UserRow) {
    if (!confirm(`Clear ${u.email}'s authenticator? They will re-enroll TOTP on next sign-in.`)) return;
    await withRow(u.id, async () => {
      await api.users.resetMfa(u.id);
      await reload();
      flash(`${u.email} will set up a new authenticator on next sign-in.`);
    });
  }

  async function removeUser(u: UserRow) {
    if (!confirm(`Deactivate ${u.email}? Their account is disabled and all sessions revoked (the record is kept for audit).`)) return;
    await withRow(u.id, async () => {
      await api.users.remove(u.id);
      await reload();
      flash(`${u.email} deactivated.`);
    });
  }

  async function addUser(e: React.FormEvent) {
    e.preventDefault();
    setAdding(true);
    setError(null);
    setInfo(null);
    try {
      const r = await api.users.create({
        email: newEmail.trim(),
        displayName: newName.trim() || undefined,
        role: newRole,
        tempPassword: newTempPw.trim() || undefined,
      });
      setCredential({ kind: 'created', email: r.email, tempPassword: r.tempPassword });
      setNewEmail('');
      setNewName('');
      setNewTempPw('');
      setShowAdd(false);
      await reload();
      flash(`Created ${r.email}.`);
    } catch (e) {
      setError((e as Error & { body?: { error?: string } }).body?.error || (e as Error).message);
    } finally {
      setAdding(false);
    }
  }

  if (session.signedOut) {
    return (
      <div className="rp-container">
        <h1 className="rp-page-title">Users &amp; access</h1>
        <SignInRequired surface="Please sign in to manage users." />
      </div>
    );
  }

  if (authBlocked) {
    return (
      <div className="rp-container">
        <h1 className="rp-page-title">Users &amp; access</h1>
        <SignInRequired
          surface="You don't have access to user management."
          detail="Ask your Medical Director or IT Admin to manage users, or to grant you the Users-manage permission."
        />
      </div>
    );
  }

  return (
    <div className="rp-container">
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <h1 className="rp-page-title">Users &amp; access</h1>
          <p className="rp-page-sub">
            Create and manage everyone in your organization — roles, passwords, two-factor, and sessions.
            New users sign in with a temporary password and set up an authenticator on first login.
          </p>
        </div>
        <div className="rp-page-header-actions">
          <button className="primary" onClick={() => { setShowAdd((s) => !s); setCredential(null); }}>
            {showAdd ? 'Close' : 'Add user'}
          </button>
        </div>
      </header>

      {error && <Banner tone="danger" onDismiss={() => setError(null)}>{error}</Banner>}
      {info && <Banner tone="success" onDismiss={() => setInfo(null)}>{info}</Banner>}

      {credential && (
        <div className="rp-panel rp-cred-callout rp-anim-fade-in-up">
          <div className="rp-panel-title">
            {credential.kind === 'created' ? 'User created' : 'Temporary password issued'}
          </div>
          <p className="rp-page-sub">
            Hand this temporary password to <strong>{credential.email}</strong> securely. It is shown
            <strong> once</strong>. They&rsquo;ll be required to set up an authenticator app on first sign-in.
          </p>
          <div className="rp-cred-row">
            <code className="rp-cred-pw">{credential.tempPassword}</code>
            <button
              className="primary-ghost"
              onClick={() => navigator.clipboard?.writeText(credential.tempPassword).then(() => flash('Temporary password copied.')).catch(() => {})}
            >
              Copy
            </button>
            <button className="ghost" onClick={() => setCredential(null)}>Dismiss</button>
          </div>
        </div>
      )}

      {showAdd && (
        <form className="rp-panel" onSubmit={addUser}>
          <div className="rp-panel-title">Add a user</div>
          <div className="rp-form-grid">
            <label className="rp-field">
              <span>Work email</span>
              <input className="rp-input" type="email" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} required placeholder="name@org.example" autoComplete="off" />
            </label>
            <label className="rp-field">
              <span>Full name</span>
              <input className="rp-input" value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="Dr. Jane Doe" autoComplete="off" />
            </label>
            <label className="rp-field">
              <span>Role</span>
              <select className="rp-input" value={newRole} onChange={(e) => setNewRole(e.target.value)}>
                {(roles.length ? roles : [newRole]).map((r) => (
                  <option key={r} value={r}>{humanizeRole(r)}</option>
                ))}
              </select>
            </label>
            <label className="rp-field">
              <span>Temporary password <span className="rp-page-sub">(optional — generated if blank)</span></span>
              <input className="rp-input" value={newTempPw} onChange={(e) => setNewTempPw(e.target.value)} placeholder="Leave blank to auto-generate" autoComplete="off" />
            </label>
          </div>
          <div className="rp-row" style={{ justifyContent: 'flex-end', gap: 8, marginTop: 12 }}>
            <button type="button" className="ghost" onClick={() => setShowAdd(false)} disabled={adding}>Cancel</button>
            <button type="submit" className="primary" disabled={adding} aria-busy={adding}>
              {adding && <span className="rp-spinner sm" aria-hidden />}
              {adding ? 'Creating…' : 'Create user'}
            </button>
          </div>
        </form>
      )}

      {!sorted && !error && (
        <div className="rp-panel"><TableSkeleton rows={6} cols={5} /></div>
      )}

      {sorted && sorted.length === 0 && (
        <EmptyState
          title="No users yet"
          description="Use “Add user” to create the first account for your organization."
        />
      )}

      {sorted && sorted.length > 0 && (
        <div className="rp-panel rp-anim-fade-in-up" style={{ padding: 0, overflowX: 'auto' }} aria-live="polite">
          <table className="rp-table rp-users-table">
            <thead>
              <tr>
                <th>User</th>
                <th>Role</th>
                <th>Status</th>
                <th>Two-factor</th>
                <th style={{ textAlign: 'right' }}>Manage</th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((u) => {
                const busy = busyId === u.id;
                return (
                  <tr key={u.id} className={u.isActive ? '' : 'rp-user-inactive'}>
                    <td>
                      <div className="rp-user-id">
                        <span className="rp-user-name">{u.displayName || u.email}</span>
                        <span className="rp-user-email">{u.email}</span>
                      </div>
                    </td>
                    <td>
                      <select
                        className="rp-input rp-role-select"
                        value={u.role}
                        disabled={busy}
                        onChange={(e) => changeRole(u, e.target.value)}
                      >
                        {(roles.includes(u.role) ? roles : [u.role, ...roles]).map((r) => (
                          <option key={r} value={r}>{humanizeRole(r)}</option>
                        ))}
                      </select>
                    </td>
                    <td>
                      {u.locked ? <span className="badge danger">Locked</span>
                        : u.isActive ? <span className="badge ok">Active</span>
                        : <span className="badge warn">Inactive</span>}
                    </td>
                    <td>
                      {u.mfaEnabled
                        ? <span className="badge ok">Enrolled</span>
                        : <span className="badge info">Not set up</span>}
                    </td>
                    <td>
                      <div className="rp-user-actions" aria-busy={busy}>
                        {busy && <span className="rp-spinner sm" aria-hidden />}
                        <button className="subtle" disabled={busy} aria-busy={busy} onClick={() => resetPassword(u)}>Reset password</button>
                        <button className="subtle" disabled={busy || !u.mfaEnabled} aria-busy={busy} onClick={() => resetMfa(u)}>Reset 2FA</button>
                        <button className="subtle" disabled={busy} aria-busy={busy} onClick={() => toggleActive(u)}>{u.isActive ? 'Deactivate' : 'Reactivate'}</button>
                        <button className="subtle" disabled={busy} aria-busy={busy} onClick={() => revokeSessions(u)}>Revoke sessions</button>
                        <button className="subtle rp-danger-action" disabled={busy} aria-busy={busy} onClick={() => removeUser(u)}>Remove</button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
