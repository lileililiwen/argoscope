import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { OverviewRow, PortfolioOverview, ProviderResultStatus } from '../api/types';
import { usePortfolioId } from '../App';

export function Overview() {
  const portfolioId = usePortfolioId();
  const [window, setWindow] = useState<'7d' | '30d'>('30d');
  const [data, setData] = useState<PortfolioOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [collecting, setCollecting] = useState(false);

  async function load() {
    try {
      setData(await api.getOverview(portfolioId, window));
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  useEffect(() => {
    load();
  }, [portfolioId, window]);

  async function collectAll() {
    if (!data) return;
    setCollecting(true);
    try {
      for (const row of data.rows) {
        try { await api.collectNow(portfolioId, row.repositoryId); } catch { /* tolerate single-row failure */ }
      }
      await load();
    } finally {
      setCollecting(false);
    }
  }

  if (error) return <div className="card error">{error}</div>;
  if (!data) return <div className="card">Loading…</div>;

  return (
    <>
      <div className="card">
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, justifyContent: 'space-between' }}>
          <div>
            <h2>Overview · {data.window}</h2>
            <p className="muted">As of {new Date(data.asOfUtc).toUTCString()}</p>
          </div>
          <div>
            <select value={window} onChange={(e) => setWindow(e.target.value as '7d' | '30d')}>
              <option value="7d">7-day window</option>
              <option value="30d">30-day window</option>
            </select>
            <button onClick={collectAll} disabled={collecting} style={{ marginLeft: 8 }}>
              {collecting ? 'Collecting…' : 'Run collection now'}
            </button>
          </div>
        </div>
      </div>
      <div className="card">
        <h3>Repositories</h3>
        <table>
          <thead>
            <tr>
              <th>Repository</th>
              <th>Role</th>
              <th>Lifecycle</th>
              <th>7d stars</th>
              <th>30d stars</th>
              <th>30d forks</th>
              <th>Acceleration</th>
              <th>Status</th>
              <th>Last collection</th>
            </tr>
          </thead>
          <tbody>
            {data.rows.length === 0 ? (
              <tr><td colSpan={9} className="muted">No repositories yet. <Link to={`/portfolios/${portfolioId}/repositories`}>Add one</Link>.</td></tr>
            ) : data.rows.map(r => <Row key={r.repositoryId} row={r} />)}
          </tbody>
        </table>
      </div>
    </>
  );
}

function Row({ row }: { row: OverviewRow }) {
  const v7 = row.velocities['7d'];
  const v30 = row.velocities['30d'];
  const vForks = row.velocities['30d_forks'];
  const accel = row.acceleration;
  return (
    <tr>
      <td>
        <Link to={`/repositories/${row.repositoryId}`}>{row.ownerLogin}/{row.name}</Link>
        {row.category ? <span className="muted"> · {row.category}</span> : null}
      </td>
      <td>{row.role}</td>
      <td>{row.lifecycle}</td>
      <td>{formatVelocity(v7)}</td>
      <td>{formatVelocity(v30)}</td>
      <td>{formatVelocity(vForks)}</td>
      <td>{formatAcceleration(accel)}</td>
      <td><StatusPill status={row.latestStatus} code={row.diagnosticCode} /></td>
      <td>{row.lastSnapshotAtUtc ? new Date(row.lastSnapshotAtUtc).toUTCString() : <span className="muted">never</span>}</td>
    </tr>
  );
}

export function formatVelocity(v: { absoluteChange: number | null; perDay: number | null; insufficientReason: string | null; isNewSignal: boolean; } | undefined): string {
  if (!v) return <span className="unavailable">—</span> as unknown as string;
  if (v.insufficientReason) return <span className="unavailable" title={v.insufficientReason}>unavailable</span> as unknown as string;
  const main = v.absoluteChange == null ? '—' : v.absoluteChange.toString();
  const sub = v.isNewSignal ? ' · new signal' : (v.perDay != null ? ` · ${v.perDay.toFixed(2)}/day` : '');
  return `${main}${sub}`;
}

export function formatAcceleration(a: { acceleration: number | null; insufficientReason: string | null; } | null): string {
  if (!a) return <span className="unavailable">—</span> as unknown as string;
  if (a.insufficientReason) return <span className="unavailable" title={a.insufficientReason}>unavailable</span> as unknown as string;
  if (a.acceleration == null) return <span className="unavailable">—</span> as unknown as string;
  const v = a.acceleration;
  const cls = v > 0 ? 'ok' : v < 0 ? 'warn' : '';
  return <span className={cls}>{v > 0 ? '+' : ''}{v.toFixed(1)}</span> as unknown as string;
}

export function StatusPill({ status, code }: { status: ProviderResultStatus; code: string | null }) {
  return (
    <span className={`status ${status.toLowerCase()}`} title={code ?? ''}>
      {toHumanStatus(status)}
    </span>
  );
}

function toHumanStatus(status: ProviderResultStatus): string {
  switch (status) {
    case 'Available': return 'ok';
    case 'Partial': return 'partial';
    case 'Stale': return 'stale';
    case 'RateLimited': return 'rate-limited';
    case 'Unauthorized': return 'unauthorized';
    case 'Forbidden': return 'forbidden';
    case 'NotFound': return 'not found';
    case 'Unavailable': return 'unavailable';
    case 'Malformed': return 'malformed';
    default: return status;
  }
}
