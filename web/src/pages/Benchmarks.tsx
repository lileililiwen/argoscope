import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { BenchmarkReport } from '../api/types';
import { usePortfolioId } from '../App';

export function Benchmarks() {
  const portfolioId = usePortfolioId();
  const [data, setData] = useState<BenchmarkReport | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.getBenchmarks(portfolioId).then(setData).catch(e => setError((e as Error).message));
  }, [portfolioId]);

  if (error) return <div className="card error">{error}</div>;
  if (!data) return <div className="card">Loading…</div>;

  return (
    <div className="card">
      <h2>Peer benchmarks · 30-day stars</h2>
      <p className="muted">Category median is reported only when at least 5 repositories have complete comparable windows. As of {new Date(data.asOfUtc).toUTCString()}.</p>
      <table>
        <thead><tr><th>Category</th><th>Cohort</th><th>Eligible</th><th>Median Δ30d</th><th>Status</th></tr></thead>
        <tbody>
          {data.results.length === 0 ? <tr><td colSpan={5} className="muted">No comparable repositories yet.</td></tr> : data.results.map(r => (
            <tr key={r.category}>
              <td>{r.category}</td>
              <td>{r.cohortSize}</td>
              <td>{r.eligibleSize}</td>
              <td>{r.median == null ? <span className="unavailable" title={r.insufficientReason ?? ''}>unavailable</span> : r.median.toFixed(1)}</td>
              <td>{r.insufficientReason ?? <span className="ok">ok</span>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
