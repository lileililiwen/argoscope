import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type {
  PackageAdoptionReport,
  PackageAssociationDto,
  PackageProvider,
} from '../api/types';
import { StatusPill } from './Overview';

const PROVIDERS: PackageProvider[] = ['DockerHub', 'Npm', 'NuGet', 'PyPI', 'CratesIo'];

const COORDINATE_HINT: Record<PackageProvider, string> = {
  DockerHub: 'namespace/name',
  Npm: '@scope/name or unscoped name',
  NuGet: 'PackageId (case insensitive)',
  PyPI: 'project-name (lowercase)',
  CratesIo: 'crate-name (case preserved)',
};

export function PackageAdoptionPage({ repositoryId }: { repositoryId: string }) {
  const [associations, setAssociations] = useState<PackageAssociationDto[]>([]);
  const [report, setReport] = useState<PackageAdoptionReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [working, setWorking] = useState(false);

  const [draftProvider, setDraftProvider] = useState<PackageProvider>('DockerHub');
  const [draftCoordinate, setDraftCoordinate] = useState('');

  async function refresh() {
    try {
      const [list, rep] = await Promise.all([
        api.listPackageAssociations(repositoryId),
        api.getPackageAdoption(repositoryId),
      ]);
      setAssociations(list);
      setReport(rep);
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  useEffect(() => {
    refresh();
  }, [repositoryId]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!draftCoordinate.trim()) return;
    setWorking(true);
    setError(null);
    try {
      await api.createPackageAssociation(repositoryId, {
        provider: draftProvider,
        coordinate: draftCoordinate.trim(),
      });
      setDraftCoordinate('');
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorking(false);
    }
  }

  async function handleRemove(associationId: string) {
    setWorking(true);
    setError(null);
    try {
      await api.removePackageAssociation(repositoryId, associationId);
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorking(false);
    }
  }

  async function handleCollect(associationId: string) {
    setWorking(true);
    setError(null);
    try {
      await api.collectPackageNow(repositoryId, associationId);
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorking(false);
    }
  }

  return (
    <>
      <div className="card">
        <h2>Package adoption</h2>
        <p className="muted">
          Link the package registries that ship this repository. Each association
          collects on a daily schedule; missing coverage is reported as insufficient
          and never as zero.
        </p>
        {error ? <div className="error">{error}</div> : null}
        <form onSubmit={handleCreate} className="form-row">
          <label>
            Provider
            <select value={draftProvider} onChange={e => setDraftProvider(e.target.value as PackageProvider)}>
              {PROVIDERS.map(p => <option key={p} value={p}>{p}</option>)}
            </select>
          </label>
          <label style={{ flex: 1 }}>
            Coordinate <span className="muted">({COORDINATE_HINT[draftProvider]})</span>
            <input
              type="text"
              value={draftCoordinate}
              onChange={e => setDraftCoordinate(e.target.value)}
              placeholder="argoscope/sample"
              required
            />
          </label>
          <button type="submit" disabled={working || !draftCoordinate.trim()}>Link package</button>
        </form>
      </div>

      <div className="card">
        <h3>Linked packages</h3>
        {associations.length === 0 ? (
          <p className="muted">No packages linked yet.</p>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Provider</th>
                <th>Coordinate</th>
                <th>Unit</th>
                <th>Window</th>
                <th>Status</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {associations.map(a => (
                <tr key={a.associationId}>
                  <td>{a.provider}</td>
                  <td><span className="kbd">{a.coordinate}</span></td>
                  <td>{a.defaultUnit}</td>
                  <td>{a.defaultWindow}</td>
                  <td>
                    {a.status === 'AttentionRequired' && a.attentionReason
                      ? <StatusPill status="Unavailable" code={a.attentionReason} />
                      : <span className="ok">{a.status}</span>}
                  </td>
                  <td>
                    <button onClick={() => handleCollect(a.associationId)} disabled={working}>Collect</button>{' '}
                    <button onClick={() => handleRemove(a.associationId)} disabled={working}>Remove</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="card">
        <h3>Adoption series</h3>
        {report?.insufficientReason ? (
          <p className="muted">Insufficient: {report.insufficientReason}.</p>
        ) : null}
        {report && report.series.length > 0 ? (
          <table>
            <thead>
              <tr>
                <th>Provider</th>
                <th>Coordinate</th>
                <th>Unit</th>
                <th>Window</th>
                <th>Coverage</th>
                <th>Points</th>
                <th>Last observation</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {report.series.map(s => (
                <tr key={`${s.associationId}-${s.unit}-${s.window}`}>
                  <td>{s.provider}</td>
                  <td><span className="kbd">{s.coordinate}</span></td>
                  <td>{s.unit}</td>
                  <td>{s.window}</td>
                  <td>{s.expectedPoints === 0 ? '—' : `${(s.coverage * 100).toFixed(0)}% (${s.actualPoints}/${s.expectedPoints})`}</td>
                  <td>{s.actualPoints}</td>
                  <td>{new Date(s.lastObservedAtUtc).toUTCString()}</td>
                  <td>
                    {s.status === 'ok'
                      ? <span className="ok">ok</span>
                      : <span className="muted">{s.status}</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          !report?.insufficientReason && <p className="muted">No series yet.</p>
        )}
      </div>
    </>
  );
}
