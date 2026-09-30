import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { ScoreConfigurationDto, ScoreFactor } from '../api/types';
import { usePortfolioId } from '../App';

const FACTOR_NAMES = ['momentum', 'engagement', 'adoption', 'external_users', 'commercial'];

export function ScoreConfiguration() {
  const portfolioId = usePortfolioId();
  const [cfg, setCfg] = useState<ScoreConfigurationDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [draft, setDraft] = useState<ScoreFactor[]>([]);

  useEffect(() => {
    api.getScoreConfiguration(portfolioId).then(c => {
      setCfg(c);
      setDraft(c.factors);
    }).catch(e => setError((e as Error).message));
  }, [portfolioId]);

  function update(name: string, field: 'weight' | 'enabled', value: number | boolean) {
    setDraft(prev => prev.map(f => f.name === name ? { ...f, [field]: value } : f));
  }

  async function save() {
    setSaving(true);
    setError(null);
    try {
      const updated = await api.putScoreConfiguration(portfolioId, draft);
      setCfg(updated);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  if (!cfg) return <div className="card">Loading…</div>;

  const enabledSum = draft.filter(f => f.enabled).reduce((s, f) => s + f.weight, 0);

  return (
    <div className="card">
      <h2>Priority score · version {cfg.version}{cfg.isDefault ? ' (default)' : ''}</h2>
      <p className="muted">Configured weights are renormalized to the sum of enabled factors when computing each row. Missing factors are reported as unavailable, never zero.</p>
      <table>
        <thead><tr><th>Factor</th><th>Weight</th><th>Enabled</th></tr></thead>
        <tbody>
          {FACTOR_NAMES.map(name => {
            const f = draft.find(x => x.name === name) ?? { name, weight: 0, enabled: false };
            return (
              <tr key={name}>
                <td>{name}</td>
                <td><input type="number" min="0" max="1" step="0.05" value={f.weight} onChange={e => update(name, 'weight', Number(e.target.value))} /></td>
                <td><input type="checkbox" checked={f.enabled} onChange={e => update(name, 'enabled', e.target.checked)} /></td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <p>Sum of enabled weights: <strong>{enabledSum.toFixed(2)}</strong></p>
      {error ? <p className="error">{error}</p> : null}
      <button onClick={save} disabled={saving || enabledSum === 0}>{saving ? 'Saving…' : 'Save'}</button>
    </div>
  );
}
