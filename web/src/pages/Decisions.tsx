import { Fragment, useEffect, useState } from 'react';
import { api } from '../api/client';
import type { DecisionEntryDto, DecisionRevisionDto, DecisionType, MembershipDto } from '../api/types';
import { usePortfolioId } from '../App';

const DECISION_TYPES: DecisionType[] = ['Continue', 'Invest', 'Pause', 'Archive', 'Revisit'];

const todayIso = () => new Date().toISOString().slice(0, 10);
const oneYearIso = () => {
  const d = new Date();
  d.setFullYear(d.getFullYear() + 1);
  return d.toISOString().slice(0, 10);
};

export function Decisions() {
  const portfolioId = usePortfolioId();
  const [items, setItems] = useState<DecisionEntryDto[]>([]);
  const [repos, setRepos] = useState<MembershipDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [showDeleted, setShowDeleted] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const [revisions, setRevisions] = useState<DecisionRevisionDto[]>([]);
  const [edit, setEdit] = useState<{
    id: string;
    type: DecisionType;
    date: string;
    rationale: string;
    reviewDate: string;
    note: string;
    expectedRevision: number;
  } | null>(null);
  const [form, setForm] = useState({
    repositoryId: '',
    decisionType: 'Continue' as DecisionType,
    decisionDate: todayIso(),
    rationale: '',
    reviewDate: oneYearIso(),
    idempotencyKey: '',
    note: '',
  });

  async function load() {
    try {
      const [list, membershipList] = await Promise.all([
        api.listDecisions(portfolioId, showDeleted),
        api.listRepositories(portfolioId),
      ]);
      setItems(list);
      setRepos(membershipList);
      setError(null);
    } catch (e) { setError((e as Error).message); }
  }

  useEffect(() => { load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [portfolioId, showDeleted]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!form.rationale.trim() || !form.decisionDate) return;
    setBusy(true); setError(null);
    try {
      await api.createDecision(portfolioId, {
        repositoryId: form.repositoryId || null,
        decisionType: form.decisionType,
        decisionDate: form.decisionDate,
        rationale: form.rationale.trim(),
        reviewDate: form.reviewDate || null,
        idempotencyKey: form.idempotencyKey.trim() || null,
        note: form.note.trim() || null,
        evidence: [],
      });
      setForm({ ...form, rationale: '', note: '', idempotencyKey: '' });
      await load();
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  async function openDetail(id: string) {
    if (openId === id) { setOpenId(null); setRevisions([]); setEdit(null); return; }
    setOpenId(id);
    setEdit(null);
    try {
      const revs = await api.getDecisionRevisions(portfolioId, id);
      setRevisions(revs);
    } catch (e) { setError((e as Error).message); }
  }

  function startEdit(d: DecisionEntryDto) {
    setEdit({
      id: d.decisionEntryId,
      type: d.decisionType,
      date: d.decisionDate,
      rationale: d.rationale,
      reviewDate: d.reviewDate ?? '',
      note: '',
      expectedRevision: d.revisionNumber,
    });
  }

  async function applyEdit() {
    if (!edit) return;
    setBusy(true); setError(null);
    try {
      await api.updateDecision(portfolioId, edit.id, {
        expectedRevision: edit.expectedRevision,
        decisionType: edit.type,
        decisionDate: edit.date,
        rationale: edit.rationale.trim(),
        reviewDate: edit.reviewDate || null,
        note: edit.note.trim() || null,
        evidence: [],
      });
      setEdit(null);
      await load();
      const revs = await api.getDecisionRevisions(portfolioId, edit.id);
      setRevisions(revs);
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  async function softDelete(d: DecisionEntryDto) {
    if (!confirm('Soft-delete this decision? History is preserved.')) return;
    try {
      await api.deleteDecision(portfolioId, d.decisionEntryId, d.revisionNumber, 'no longer active');
      await load();
    } catch (e) { setError((e as Error).message); }
  }

  async function restore(d: DecisionEntryDto) {
    try {
      await api.restoreDecision(portfolioId, d.decisionEntryId, d.revisionNumber, 'came back');
      await load();
    } catch (e) { setError((e as Error).message); }
  }

  return (
    <>
      <div className="card">
        <h2>Record a decision</h2>
        <p className="muted">
          Decisions are advisory records. They never change repository scores or lifecycle.
        </p>
        <form onSubmit={submit} className="form-row">
          <label>Repository (optional)<br/>
            <select value={form.repositoryId} onChange={e => setForm({ ...form, repositoryId: e.target.value })}>
              <option value="">— Portfolio level —</option>
              {repos.map(r => <option key={r.repositoryId} value={r.repositoryId}>{r.ownerLogin}/{r.name}</option>)}
            </select>
          </label>
          <label>Decision type<br/>
            <select value={form.decisionType} onChange={e => setForm({ ...form, decisionType: e.target.value as DecisionType })}>
              {DECISION_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
            </select>
          </label>
          <label>Decision date<br/><input type="date" value={form.decisionDate} onChange={e => setForm({ ...form, decisionDate: e.target.value })} required /></label>
          <label>Review date (optional)<br/><input type="date" value={form.reviewDate} onChange={e => setForm({ ...form, reviewDate: e.target.value })} /></label>
          <label style={{ flex: '1 1 100%' }}>Rationale (1–10,000 chars)<br/>
            <textarea value={form.rationale} rows={3} onChange={e => setForm({ ...form, rationale: e.target.value })} required />
          </label>
          <label>Idempotency key (optional)<br/><input value={form.idempotencyKey} onChange={e => setForm({ ...form, idempotencyKey: e.target.value })} placeholder="client-retry-…" /></label>
          <label>Note (optional)<br/><input value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} /></label>
          <div style={{ alignSelf: 'end' }}><button type="submit" disabled={busy}>{busy ? 'Saving…' : 'Record decision'}</button></div>
        </form>
        {error ? <p className="error">{error}</p> : null}
      </div>

      <div className="card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h3>Journal</h3>
          <label className="muted"><input type="checkbox" checked={showDeleted} onChange={e => setShowDeleted(e.target.checked)} /> Show deleted</label>
        </div>
        {items.length === 0 ? <p className="muted">No decisions yet.</p> : (
          <table>
            <thead><tr><th>Decision</th><th>Type</th><th>Date</th><th>Revision</th><th>Authored by</th><th></th></tr></thead>
            <tbody>
              {items.map(d => {
                const repo = d.repositoryId ? repos.find(r => r.repositoryId === d.repositoryId) : null;
                return (
                  <Fragment key={d.decisionEntryId}>
                    <tr style={d.deletedAtUtc ? { opacity: 0.55 } : undefined}>
                      <td>
                        <strong>{repo ? `${repo.ownerLogin}/${repo.name}` : 'Portfolio level'}</strong>
                        {d.deletedAtUtc ? <> &nbsp;<span className="status stale">Deleted</span></> : null}
                        <div className="muted">{d.rationale}</div>
                      </td>
                      <td>{d.decisionType}</td>
                      <td>{d.decisionDate}</td>
                      <td>r{d.revisionNumber}</td>
                      <td className="muted">{d.latestRevision.actorId} · {d.latestRevision.action}</td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        <button onClick={() => openDetail(d.decisionEntryId)}>{openId === d.decisionEntryId ? 'Close' : 'History'}</button>
                        {d.deletedAtUtc
                          ? <button className="btn-secondary" onClick={() => restore(d)} style={{ marginLeft: 6 }}>Restore</button>
                          : <button className="btn-secondary" onClick={() => softDelete(d)} style={{ marginLeft: 6 }}>Delete</button>}
                        {!d.deletedAtUtc
                          ? <button className="btn-secondary" onClick={() => startEdit(d)} style={{ marginLeft: 6 }}>Edit</button>
                          : null}
                      </td>
                    </tr>
                    {openId === d.decisionEntryId ? (
                      <tr>
                        <td colSpan={6} style={{ background: 'var(--bg-soft)' }}>
                          {edit && edit.id === d.decisionEntryId ? (
                            <div className="form-row">
                              <label>Type<br/>
                                <select value={edit.type} onChange={e => setEdit({ ...edit, type: e.target.value as DecisionType })}>
                                  {DECISION_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
                                </select>
                              </label>
                              <label>Date<br/><input type="date" value={edit.date} onChange={e => setEdit({ ...edit, date: e.target.value })} /></label>
                              <label>Review date<br/><input type="date" value={edit.reviewDate} onChange={e => setEdit({ ...edit, reviewDate: e.target.value })} /></label>
                              <label style={{ flex: '1 1 100%' }}>Rationale<br/>
                                <textarea rows={3} value={edit.rationale} onChange={e => setEdit({ ...edit, rationale: e.target.value })} />
                              </label>
                              <label>Note<br/><input value={edit.note} onChange={e => setEdit({ ...edit, note: e.target.value })} /></label>
                              <div style={{ alignSelf: 'end' }}>
                                <button onClick={applyEdit} disabled={busy}>Save (revision {edit.expectedRevision + 1})</button>
                                <button className="btn-secondary" onClick={() => setEdit(null)} style={{ marginLeft: 6 }}>Cancel</button>
                              </div>
                            </div>
                          ) : null}

                          <h4 style={{ marginTop: 12 }}>Evidence</h4>
                          {d.evidence.length === 0 ? <p className="muted">No evidence linked.</p> : (
                            <table>
                              <thead><tr><th>Kind</th><th>Reference</th><th>Resolution</th><th>Source</th></tr></thead>
                              <tbody>
                                {d.evidence.map(e => (
                                  <tr key={e.evidenceId}>
                                    <td>{e.kind}</td>
                                    <td><span className="kbd">{e.referenceId.slice(0, 8)}…</span></td>
                                    <td>
                                      {e.resolution === 'Resolved'
                                        ? <span className="status available">Resolved</span>
                                        : <span className="status stale" title={e.sourceDestination}>Unresolved</span>}
                                    </td>
                                    <td className="muted">{e.sourceDestination}{e.label ? ` · ${e.label}` : ''}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          )}

                          <h4 style={{ marginTop: 12 }}>Revision history</h4>
                          {revisions.length === 0 ? <p className="muted">Loading…</p> : (
                            <table>
                              <thead><tr><th>#</th><th>Action</th><th>Actor</th><th>Occurred (UTC)</th><th>Note</th></tr></thead>
                              <tbody>
                                {revisions.map(r => (
                                  <tr key={r.revisionNumber}>
                                    <td>r{r.revisionNumber}</td>
                                    <td>{r.action}</td>
                                    <td className="muted">{r.actorId}</td>
                                    <td className="muted">{new Date(r.occurredAtUtc).toUTCString()}</td>
                                    <td>{r.note ?? ''}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          )}
                        </td>
                      </tr>
                    ) : null}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}
