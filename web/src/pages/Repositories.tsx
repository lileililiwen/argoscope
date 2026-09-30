import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { MembershipDto } from '../api/types';
import { usePortfolioId } from '../App';

const LIFECYCLES = ['Idea', 'Prototype', 'OpenSource', 'Growing', 'Validated', 'SaaSCandidate', 'Hosted', 'Maintenance', 'Archived'];

export function Repositories() {
  const portfolioId = usePortfolioId();
  const [items, setItems] = useState<MembershipDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({
    nodeId: '',
    ownerLogin: '',
    name: '',
    visibility: 'Public',
    role: 'Owned',
    category: '',
    lifecycle: 'OpenSource',
  });

  async function load() {
    try { setItems(await api.listRepositories(portfolioId)); setError(null); }
    catch (e) { setError((e as Error).message); }
  }
  useEffect(() => { load(); }, [portfolioId]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!form.nodeId.trim() || !form.ownerLogin.trim() || !form.name.trim()) return;
    setBusy(true); setError(null);
    try {
      await api.addRepository(portfolioId, {
        nodeId: form.nodeId.trim(),
        ownerLogin: form.ownerLogin.trim(),
        name: form.name.trim(),
        visibility: form.visibility,
        role: form.role,
        category: form.category.trim() || null,
        lifecycle: form.lifecycle,
      });
      setForm({ ...form, nodeId: '', ownerLogin: '', name: '', category: '' });
      await load();
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  async function remove(id: string) {
    if (!confirm('Remove this membership? (The remote repository is untouched.)')) return;
    try { await api.removeRepository(portfolioId, id); await load(); }
    catch (e) { setError((e as Error).message); }
  }

  async function updateCategory(m: MembershipDto, category: string) {
    try { await api.updateMembership(portfolioId, m.repositoryId, { category: category.trim() || null, lifecycle: m.lifecycle }); await load(); }
    catch (e) { setError((e as Error).message); }
  }

  async function updateLifecycle(m: MembershipDto, lifecycle: string) {
    try { await api.updateMembership(portfolioId, m.repositoryId, { category: m.category, lifecycle }); await load(); }
    catch (e) { setError((e as Error).message); }
  }

  return (
    <>
      <div className="card">
        <h2>Add repository</h2>
        <form onSubmit={submit} className="layout-row">
          <label>Node id (GitHub stable id)<br/><input value={form.nodeId} onChange={e => setForm({ ...form, nodeId: e.target.value })} required /></label>
          <label>Owner login<br/><input value={form.ownerLogin} onChange={e => setForm({ ...form, ownerLogin: e.target.value })} required /></label>
          <label>Repository name<br/><input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
          <label>Visibility<br/>
            <select value={form.visibility} onChange={e => setForm({ ...form, visibility: e.target.value })}>
              <option>Public</option><option>Private</option><option>Internal</option><option>Unknown</option>
            </select>
          </label>
          <label>Role<br/>
            <select value={form.role} onChange={e => setForm({ ...form, role: e.target.value })}>
              <option>Owned</option><option>Competitor</option>
            </select>
          </label>
          <label>Category (optional)<br/><input value={form.category} onChange={e => setForm({ ...form, category: e.target.value })} /></label>
          <label>Lifecycle<br/>
            <select value={form.lifecycle} onChange={e => setForm({ ...form, lifecycle: e.target.value })}>
              {LIFECYCLES.map(l => <option key={l} value={l}>{l}</option>)}
            </select>
          </label>
          <div style={{ alignSelf: 'end' }}><button type="submit" disabled={busy}>{busy ? 'Adding…' : 'Add'}</button></div>
        </form>
        {error ? <p className="error">{error}</p> : null}
      </div>
      <div className="card">
        <h3>Memberships</h3>
        <table>
          <thead><tr><th>Repository</th><th>Role</th><th>Category</th><th>Lifecycle</th><th>Visibility</th><th></th></tr></thead>
          <tbody>
            {items.length === 0 ? <tr><td colSpan={6} className="muted">No repositories yet.</td></tr> : items.map(m => (
              <tr key={m.membershipId}>
                <td>{m.ownerLogin}/{m.name}</td>
                <td>{m.role}</td>
                <td><input defaultValue={m.category ?? ''} onBlur={e => updateCategory(m, e.target.value)} /></td>
                <td>
                  <select defaultValue={m.lifecycle} onChange={e => updateLifecycle(m, e.target.value)}>
                    {LIFECYCLES.map(l => <option key={l} value={l}>{l}</option>)}
                  </select>
                </td>
                <td>{m.visibility}</td>
                <td><button className="btn-secondary" onClick={() => remove(m.repositoryId)}>Remove</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
