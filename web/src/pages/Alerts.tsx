import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { AlertEvaluationDto, AlertRuleDto, MembershipDto } from '../api/types';
import { usePortfolioId } from '../App';

const METRIC_KEYS = [
  'stars_7d',
  'stars_30d',
  'external_engagement_30d',
  'momentum_score',
  'snapshot_staleness_hours',
] as const;

const OPERATORS = ['GreaterThanOrEqual', 'GreaterThan', 'LessThanOrEqual', 'LessThan'] as const;

export function Alerts() {
  const portfolioId = usePortfolioId();
  const [rules, setRules] = useState<AlertRuleDto[]>([]);
  const [history, setHistory] = useState<AlertEvaluationDto[]>([]);
  const [repos, setRepos] = useState<MembershipDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({
    repositoryId: '',
    name: '',
    metricKey: 'stars_30d',
    operator: 'GreaterThanOrEqual',
    threshold: '10',
    minimumCoverage: '0.5',
    cooldownHours: '24',
    enabled: true,
    channel: 'Email',
    destination: '',
    secret: '',
  });

  async function load() {
    try {
      const [ruleList, alertHistory, membershipList] = await Promise.all([
        api.listAlertRules(portfolioId),
        api.listAlerts(portfolioId, 50),
        api.listRepositories(portfolioId),
      ]);
      setRules(ruleList);
      setHistory(alertHistory);
      setRepos(membershipList);
      setError(null);
    } catch (e) { setError((e as Error).message); }
  }

  useEffect(() => { load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [portfolioId]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!form.name.trim() || !form.destination.trim()) return;
    setBusy(true); setError(null);
    try {
      await api.createAlertRule(portfolioId, {
        repositoryId: form.repositoryId || null,
        name: form.name.trim(),
        metricKey: form.metricKey,
        operator: form.operator,
        threshold: Number(form.threshold),
        minimumCoverage: Number(form.minimumCoverage),
        cooldownHours: Number(form.cooldownHours),
        enabled: form.enabled,
        channel: form.channel,
        destination: form.destination.trim(),
        secret: form.secret.trim() || null,
      });
      setForm({ ...form, name: '', destination: '', secret: '' });
      await load();
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  async function evaluate(ruleId: string) {
    try {
      await api.evaluateAlertRule(portfolioId, ruleId);
      await load();
    } catch (e) { setError((e as Error).message); }
  }

  async function remove(rule: AlertRuleDto) {
    if (!confirm(`Disable rule "${rule.name}"? History is preserved.`)) return;
    try {
      await api.deleteAlertRule(portfolioId, rule.ruleId, rule.version);
      await load();
    } catch (e) { setError((e as Error).message); }
  }

  return (
    <>
      <div className="card">
        <h2>Attention alerts</h2>
        <p className="muted">
          Rules watch deterministic snapshot metrics and staleness. Missing or under-covered
          metrics never fire. Webhook targets must be HTTPS and public; secrets are write-only.
        </p>
        <form onSubmit={submit} className="form-row">
          <label style={{ flex: '1 1 100%' }}>Rule name<br/>
            <input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required />
          </label>
          <label>Repository (optional)<br/>
            <select value={form.repositoryId} onChange={e => setForm({ ...form, repositoryId: e.target.value })}>
              <option value="">— All portfolio repos —</option>
              {repos.map(r => <option key={r.repositoryId} value={r.repositoryId}>{r.ownerLogin}/{r.name}</option>)}
            </select>
          </label>
          <label>Metric<br/>
            <select value={form.metricKey} onChange={e => setForm({ ...form, metricKey: e.target.value })}>
              {METRIC_KEYS.map(k => <option key={k} value={k}>{k}</option>)}
            </select>
          </label>
          <label>Operator<br/>
            <select value={form.operator} onChange={e => setForm({ ...form, operator: e.target.value })}>
              {OPERATORS.map(o => <option key={o} value={o}>{o}</option>)}
            </select>
          </label>
          <label>Threshold<br/><input type="number" step="any" value={form.threshold} onChange={e => setForm({ ...form, threshold: e.target.value })} required /></label>
          <label>Min coverage (0–1)<br/><input type="number" step="0.05" min="0" max="1" value={form.minimumCoverage} onChange={e => setForm({ ...form, minimumCoverage: e.target.value })} required /></label>
          <label>Cooldown (hours)<br/><input type="number" min="0" max="720" value={form.cooldownHours} onChange={e => setForm({ ...form, cooldownHours: e.target.value })} required /></label>
          <label>Channel<br/>
            <select value={form.channel} onChange={e => setForm({ ...form, channel: e.target.value })}>
              <option value="Email">Email</option>
              <option value="Webhook">Webhook</option>
            </select>
          </label>
          <label>Destination<br/><input value={form.destination} onChange={e => setForm({ ...form, destination: e.target.value })} placeholder={form.channel === 'Email' ? 'owner@example.com' : 'https://…'} required /></label>
          <label>Secret (webhook HMAC, write-only)<br/><input value={form.secret} onChange={e => setForm({ ...form, secret: e.target.value })} placeholder="optional" /></label>
          <label className="muted"><input type="checkbox" checked={form.enabled} onChange={e => setForm({ ...form, enabled: e.target.checked })} /> Enabled</label>
          <div style={{ alignSelf: 'end' }}><button type="submit" disabled={busy}>{busy ? 'Saving…' : 'Create rule'}</button></div>
        </form>
        {error ? <p className="error">{error}</p> : null}
      </div>

      <div className="card">
        <h3>Rules ({rules.length})</h3>
        {rules.length === 0 ? <p className="muted">No alert rules yet.</p> : (
          <table>
            <thead><tr><th>Rule</th><th>Metric</th><th>Condition</th><th>Channel</th><th></th></tr></thead>
            <tbody>
              {rules.map(r => (
                <tr key={r.ruleId} style={r.deletedAtUtc ? { opacity: 0.55 } : undefined}>
                  <td>
                    <strong>{r.name}</strong>
                    {r.deletedAtUtc ? <> &nbsp;<span className="status stale">Disabled</span></> : null}
                    {!r.enabled && !r.deletedAtUtc ? <> &nbsp;<span className="status stale">Paused</span></> : null}
                    <div className="muted">v{r.version} · cooldown {r.cooldownHours}h · coverage ≥ {r.minimumCoverage}</div>
                  </td>
                  <td><span className="kbd">{r.metricKey}</span></td>
                  <td><span className="kbd">{r.operator} {r.threshold}</span></td>
                  <td className="muted">{r.channel} · {r.destinationMasked}{r.hasSecret ? ' · secret set' : ''}</td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <button onClick={() => evaluate(r.ruleId)}>Evaluate now</button>
                    {!r.deletedAtUtc ? <button className="btn-secondary" onClick={() => remove(r)} style={{ marginLeft: 6 }}>Disable</button> : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="card">
        <h3>Evaluation &amp; delivery history</h3>
        {history.length === 0 ? <p className="muted">No evaluations yet.</p> : (
          <table>
            <thead><tr><th>Rule</th><th>Metric value</th><th>Status</th><th>Delivery</th><th>Evaluated (UTC)</th></tr></thead>
            <tbody>
              {history.map(h => (
                <tr key={h.evaluationId}>
                  <td><strong>{h.ruleName}</strong><div className="muted">{h.metricKey}</div></td>
                  <td>{h.metricValue ?? '—'} <span className="muted">(coverage {h.coverage.toFixed(2)})</span></td>
                  <td>
                    {h.status === 'Fired'
                      ? <span className="status available">Fired</span>
                      : <span className="status stale">{h.status}{h.reason ? ` · ${h.reason}` : ''}</span>}
                  </td>
                  <td className="muted">
                    {h.attempts.length === 0 ? '—' : h.attempts.map(a => (
                      <div key={a.attemptId}>{a.state}{a.responseCode ? ` · ${a.responseCode}` : ''}{a.error ? ` · ${a.error}` : ''}{a.nextRetryAtUtc ? ` · retry ${new Date(a.nextRetryAtUtc).toUTCString()}` : ''}</div>
                    ))}
                  </td>
                  <td className="muted">{new Date(h.evaluatedAtUtc).toUTCString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}
