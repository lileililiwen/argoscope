import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { RepositoryMetrics } from '../api/types';
import { StatusPill, formatVelocity, formatAcceleration } from './Overview';

export function RepositoryMetricsPage() {
  const { repositoryId } = useParams();
  const [data, setData] = useState<RepositoryMetrics | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!repositoryId) return;
    api.getRepositoryMetrics(repositoryId).then(setData).catch(e => setError((e as Error).message));
  }, [repositoryId]);

  if (error) return <div className="card error">{error}</div>;
  if (!data) return <div className="card">Loading…</div>;

  return (
    <>
      <div className="card">
        <h2>{data.ownerLogin}/{data.name}</h2>
        <p className="muted">Node id: <span className="kbd">{data.nodeId}</span> · visibility: {data.visibility}</p>
        <p>Latest collection status: <StatusPill status={data.latestStatus} code={data.diagnosticCode} /> · {data.lastSnapshotAtUtc ? new Date(data.lastSnapshotAtUtc).toUTCString() : 'never'}</p>
        <p>
          <Link to={`/repositories/${data.repositoryId}/adoption`} className="btn">View package adoption</Link>
        </p>
      </div>
      <div className="layout-row">
        <div className="card">
          <h3>30-day velocity</h3>
          <table>
            <thead><tr><th>Metric</th><th>Change</th><th>Per day</th><th>Status</th></tr></thead>
            <tbody>
              {Object.entries(data.velocities).map(([k, v]) => (
                <tr key={k}>
                  <td>{v.metricName}</td>
                  <td>{formatVelocity(v)}</td>
                  <td>{v.perDay == null ? '—' : v.perDay.toFixed(2)}</td>
                  <td>{v.insufficientReason ?? <span className="ok">ok</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="card">
          <h3>Acceleration</h3>
          <p>30-day acceleration: {formatAcceleration(data.acceleration)}</p>
          <p className="muted">Descriptive only; not a causal signal.</p>
          {data.engagement ? (
            <>
              <h3>Engagement · last {data.engagement.coveredDays}d</h3>
              <table>
                <thead><tr><th>Attribution</th><th>Issues</th><th>PRs</th><th>Contributors</th></tr></thead>
                <tbody>
                  <tr><td>External</td><td>{data.engagement.externalIssues}</td><td>{data.engagement.externalPullRequests}</td><td>{data.engagement.externalContributors}</td></tr>
                  <tr><td>Owner</td><td>{data.engagement.ownerIssues}</td><td>{data.engagement.ownerPullRequests}</td><td>{data.engagement.ownerContributors}</td></tr>
                  <tr><td>Unknown</td><td>{data.engagement.unknownIssues}</td><td>{data.engagement.unknownPullRequests}</td><td>{data.engagement.unknownContributors}</td></tr>
                </tbody>
              </table>
            </>
          ) : <p className="muted">No engagement buckets in the window.</p>}
        </div>
      </div>
    </>
  );
}
