import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { CommercialSignalDto, MembershipDto, SignalReviewDto } from '../api/types';
import { usePortfolioId } from '../App';

const STATES = ['', 'pending', 'reviewed'] as const;
const DECISIONS = ['Accept', 'Reject', 'Correct'] as const;
const CATEGORIES = [
  'HostedRequest',
  'PaidSupport',
  'EnterpriseCapability',
  'ProcurementQuestion',
  'NotCommercial',
  'Unclear',
] as const;

export function Signals() {
  const portfolioId = usePortfolioId();
  const [repos, setRepos] = useState<MembershipDto[]>([]);
  const [repoId, setRepoId] = useState('');
  const [state, setState] = useState<string>('');
  const [items, setItems] = useState<CommercialSignalDto[]>([]);
  const [reviews, setReviews] = useState<Record<string, SignalReviewDto[]>>({});
  const [openId, setOpenId] = useState<string | null>(null);
  const [decision, setDecision] = useState<string>('Accept');
  const [corrected, setCorrected] = useState<string>('PaidSupport');
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [lastRun, setLastRun] = useState<string | null>(null);

  async function loadRepos() {
    try {
      const list = await api.listRepositories(portfolioId);
      setRepos(list);
      if (!repoId && list.length > 0) setRepoId(list[0].repositoryId);
      setError(null);
    } catch (e) { setError((e as Error).message); }
  }

  async function loadSignals(id: string, s: string) {
    if (!id) { setItems([]); return; }
    try {
      setItems(await api.listSignals(id, s || undefined));
      setError(null);
    } catch (e) { setError((e as Error).message); }
  }

  useEffect(() => { loadRepos(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [portfolioId]);
  useEffect(() => { if (repoId) loadSignals(repoId, state); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [repoId, state]);

  async function collect() {
    if (!repoId) return;
    setBusy(true); setError(null);
    try {
      const run = await api.collectSignals(repoId);
      setLastRun(`created ${run.created} · duplicates ${run.duplicates} · retryable ${run.retried} · unavailable ${run.markedUnavailable}`);
      await loadSignals(repoId, state);
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  async function toggleReviews(s: CommercialSignalDto) {
    if (openId === s.signalId) { setOpenId(null); return; }
    setOpenId(s.signalId);
    try {
      const list = await api.getSignalReviews(s.repositoryId, s.signalId);
      setReviews(prev => ({ ...prev, [s.signalId]: list }));
    } catch (e) { setError((e as Error).message); }
  }

  async function submitReview(s: CommercialSignalDto) {
    setBusy(true); setError(null);
    try {
      await api.reviewSignal(s.repositoryId, s.signalId, {
        expectedVersion: s.version,
        decision,
        correctedCategory: decision === 'Correct' ? corrected : null,
        note: note.trim() || null,
      });
      setNote('');
      await loadSignals(repoId, state);
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  return (
    <>
      <div className="card">
        <h2>Commercial signals</h2>
        <p className="muted">
          Advisory only: issue/PR title and body are projected into a redacted excerpt
          (4,000 chars, no emails or tokens), classified into a category with confidence,
          and confirmed only by owner review. Nothing here changes scores, ranks,
          lifecycle state, or GitHub. Low confidence (&lt; 0.65) defaults to Unclear;
          provider failures are retryable, never confirmed.
        </p>
        <div className="form-row">
          <label>Repository<br/>
            <select value={repoId} onChange={e => setRepoId(e.target.value)}>
              {repos.map(r => <option key={r.repositoryId} value={r.repositoryId}>{r.ownerLogin}/{r.name}</option>)}
            </select>
          </label>
          <label>State<br/>
            <select value={state} onChange={e => setState(e.target.value)}>
              {STATES.map(s => <option key={s} value={s}>{s === '' ? 'All' : s}</option>)}
            </select>
          </label>
          <div style={{ alignSelf: 'end' }}>
            <button onClick={collect} disabled={busy || !repoId}>{busy ? 'Collecting…' : 'Collect now'}</button>
          </div>
          {lastRun ? <span className="muted" style={{ alignSelf: 'end' }}>Last run: {lastRun}</span> : null}
        </div>
        {error ? <p className="error">{error}</p> : null}
      </div>

      <div className="card">
        <h3>Review queue ({items.length})</h3>
        {items.length === 0 ? <p className="muted">No signals for this filter. Collect to classify eligible issue/PR text.</p> : (
          <table>
            <thead><tr><th>Source</th><th>Suggestion</th><th>Status</th><th>Review</th></tr></thead>
            <tbody>
              {items.map(s => (
                <tr key={s.signalId} style={s.sourceAvailable ? undefined : { opacity: 0.6 }}>
                  <td>
                    <a href={s.sourceUrl} target="_blank" rel="noreferrer">{s.sourceType} #{s.sourceNumber}</a>
                    {!s.sourceAvailable ? <> &nbsp;<span className="status stale">Source unavailable</span></> : null}
                    <div className="muted">v{s.suggestionVersion} · {s.classifierVersion} · {new Date(s.sourceUpdatedAtUtc).toUTCString()}</div>
                    <details><summary className="muted">Excerpt</summary><pre className="muted">{s.excerpt}</pre></details>
                  </td>
                  <td>
                    <strong>{s.category}</strong> <span className="muted">({s.confidence.toFixed(2)})</span>
                    {s.correctedCategory ? <div className="muted">corrected → {s.correctedCategory}</div> : null}
                    <div className="muted">{s.rationale}</div>
                  </td>
                  <td>
                    {s.status === 'Pending' || s.status === 'NeedsRetry'
                      ? <span className="status stale">{s.status}</span>
                      : <span className="status available">{s.status}</span>}
                    {s.reviewer ? <div className="muted">by {s.reviewer}</div> : null}
                  </td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {(s.status === 'Pending' || s.status === 'NeedsRetry') ? (
                      <span>
                        <select value={decision} onChange={e => setDecision(e.target.value)}>
                          {DECISIONS.map(d => <option key={d} value={d}>{d}</option>)}
                        </select>
                        {decision === 'Correct' ? (
                          <select value={corrected} onChange={e => setCorrected(e.target.value)} style={{ marginLeft: 4 }}>
                            {CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
                          </select>
                        ) : null}
                        <input value={note} onChange={e => setNote(e.target.value)} placeholder="note (optional)" style={{ marginLeft: 4, width: 140 }} />
                        <button onClick={() => submitReview(s)} disabled={busy} style={{ marginLeft: 4 }}>Review</button>
                      </span>
                    ) : <span className="muted">decided</span>}
                    <button className="btn-secondary" onClick={() => toggleReviews(s)} style={{ marginLeft: 6 }}>
                      {openId === s.signalId ? 'Hide audit' : 'Audit'}
                    </button>
                    {openId === s.signalId ? (
                      <div className="muted">
                        {(reviews[s.signalId] ?? []).map(r => (
                          <div key={r.revisionNumber}>v{r.revisionNumber} · {r.decision} (was {r.priorCategory}/{r.priorStatus}) · {r.reviewer}{r.note ? ` · ${r.note}` : ''}</div>
                        ))}
                      </div>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}
