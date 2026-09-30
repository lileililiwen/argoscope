import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { TenantDto, TenantMemberDto, TenantRole } from '../api/types';

const roles: TenantRole[] = ['Owner', 'Editor', 'Viewer'];

export function Tenants() {
  const [tenantId, setTenantId] = useState('');
  const [tenant, setTenant] = useState<TenantDto | null>(null);
  const [members, setMembers] = useState<TenantMemberDto[]>([]);
  const [myRole, setMyRole] = useState<TenantRole | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [newTenantName, setNewTenantName] = useState('');
  const [newOwnerSubject, setNewOwnerSubject] = useState('');
  const [inviteName, setInviteName] = useState('');
  const [inviteRole, setInviteRole] = useState<TenantRole>('Viewer');

  async function load(id: string) {
    try {
      const t = await api.getTenant(id);
      const list = await api.listMembers(id);
      setTenant(t);
      setMembers(list);
      // The caller's own membership is informational only; role-specific
      // behavior hides owner controls unless the caller is an owner.
      const mine = list.find((m) => m.state === 'Active');
      setMyRole(mine && mine.role === 'Owner' ? 'Owner' : null);
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  useEffect(() => {
    if (tenantId) load(tenantId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function createTenant() {
    try {
      setNotice(null);
      const t = await api.createTenant({ name: newTenantName, ownerSubject: newOwnerSubject });
      setTenantId(t.tenantId);
      setNewTenantName('');
      setNewOwnerSubject('');
      setNotice(`Tenant ${t.name} created.`);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function invite() {
    if (!tenant) return;
    try {
      setNotice(null);
      const created = await api.inviteMember(tenant.tenantId, { displayName: inviteName, role: inviteRole });
      setInviteName('');
      setNotice(`Invite issued for ${created.member.displayName}; token: ${created.inviteToken ?? '(hidden)'}`);
      await load(tenant.tenantId);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function changeRole(membershipId: string, role: TenantRole) {
    if (!tenant) return;
    try {
      setNotice(null);
      await api.changeRole(tenant.tenantId, membershipId, role);
      await load(tenant.tenantId);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function revoke(membershipId: string) {
    if (!tenant) return;
    try {
      setNotice(null);
      await api.revokeMember(tenant.tenantId, membershipId);
      await load(tenant.tenantId);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function migrate() {
    if (!tenant) return;
    try {
      setNotice(null);
      const result = await api.runMigration(tenant.tenantId);
      setNotice(`Migration assigned ${result.assigned} of ${result.total} portfolios.`);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <div className="card">
      <h2>Tenants and membership</h2>
      <p className="muted">
        Hosted Argoscope isolates every portfolio by tenant. Owners manage invites;
        editors mutate portfolio data; viewers read. The last active owner cannot
        be removed. Sessions use secure HttpOnly cookies with CSRF protection.
      </p>
      <div className="row">
        <input
          placeholder="Tenant id (GUID)"
          value={tenantId}
          onChange={(e) => setTenantId(e.target.value)}
          style={{ width: '20rem' }}
        />
        <button onClick={() => tenantId && load(tenantId)}>Open tenant</button>
      </div>
      <div className="row">
        <input
          placeholder="New tenant name"
          value={newTenantName}
          onChange={(e) => setNewTenantName(e.target.value)}
        />
        <input
          placeholder="Owner subject (OIDC sub)"
          value={newOwnerSubject}
          onChange={(e) => setNewOwnerSubject(e.target.value)}
        />
        <button onClick={createTenant} disabled={!newTenantName || !newOwnerSubject}>
          Create tenant
        </button>
      </div>
      {error ? <div className="card error">{error}</div> : null}
      {notice ? <div className="card">{notice}</div> : null}
      {tenant ? (
        <>
          <h3>{tenant.name}</h3>
          {myRole !== 'Owner' ? (
            <p className="muted">Read-only view: membership management requires the Owner role.</p>
          ) : null}
          <table>
            <thead>
              <tr><th>Display name</th><th>Subject</th><th>Role</th><th>State</th><th>Invite expires</th><th></th></tr>
            </thead>
            <tbody>
              {members.map((m) => (
                <tr key={m.membershipId}>
                  <td>{m.displayName}</td>
                  <td>{m.subject || '—'}</td>
                  <td>{m.role}</td>
                  <td>{m.state}</td>
                  <td>{m.inviteExpiresAtUtc ?? '—'}</td>
                  <td>
                    {myRole === 'Owner' && m.state === 'Active' ? (
                      <>
                        <select
                          value={m.role}
                          onChange={(e) => changeRole(m.membershipId, e.target.value as TenantRole)}
                        >
                          {roles.map((r) => <option key={r} value={r}>{r}</option>)}
                        </select>
                        {' '}
                        <button onClick={() => revoke(m.membershipId)}>Revoke</button>
                      </>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {myRole === 'Owner' ? (
            <>
              <div className="row">
                <input
                  placeholder="Invite display name"
                  value={inviteName}
                  onChange={(e) => setInviteName(e.target.value)}
                />
                <select value={inviteRole} onChange={(e) => setInviteRole(e.target.value as TenantRole)}>
                  {roles.map((r) => <option key={r} value={r}>{r}</option>)}
                </select>
                <button onClick={invite} disabled={!inviteName}>Invite (7-day single-use)</button>
              </div>
              <div className="row">
                <button onClick={migrate}>Migrate legacy portfolios to this tenant</button>
              </div>
            </>
          ) : null}
        </>
      ) : null}
    </div>
  );
}
