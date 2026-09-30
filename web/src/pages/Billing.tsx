import { useState } from 'react';
import { api } from '../api/client';
import type { BillingEventDto, BillingStatusDto, ReconciliationReportDto } from '../api/types';

export function Billing() {
  const [tenantId, setTenantId] = useState('');
  const [status, setStatus] = useState<BillingStatusDto | null>(null);
  const [events, setEvents] = useState<BillingEventDto[]>([]);
  const [report, setReport] = useState<ReconciliationReportDto | null>(null);
  const [checkoutUrl, setCheckoutUrl] = useState<string | null>(null);
  const [planId, setPlanId] = useState('pro');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function load(id: string) {
    try {
      setNotice(null);
      const s = await api.getBillingStatus(id);
      setStatus(s);
      setError(null);
      try {
        setEvents(await api.listBillingEvents(id));
      } catch {
        // Event audit is owner-only; the status surface stays visible.
        setEvents([]);
      }
    } catch (e) {
      setStatus(null);
      setError((e as Error).message);
    }
  }

  async function checkout() {
    if (!tenantId) return;
    try {
      setNotice(null);
      const c = await api.startCheckout(tenantId, planId);
      setCheckoutUrl(c.checkoutUrl);
      setNotice(`Provider-hosted checkout ready (customer ${c.providerCustomerId}). Payment entry happens on the provider page; Argoscope stores references only.`);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function cancel() {
    if (!tenantId || !status) return;
    try {
      setNotice(null);
      const s = await api.cancelSubscription(tenantId);
      setStatus(s);
      setNotice('Cancellation recorded: the subscription stays active through the paid-through date. Data is retained.');
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function reconcile() {
    if (!tenantId) return;
    try {
      setNotice(null);
      setReport(await api.reconcileBilling(tenantId));
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <div className="card">
      <h2>Billing</h2>
      <p>Provider-backed subscriptions with tenant-scoped entitlements. Plan changes never delete data.</p>
      <div className="row">
        <input
          placeholder="Tenant id"
          value={tenantId}
          onChange={(e) => setTenantId(e.target.value)}
          style={{ width: '20rem' }}
        />
        <button onClick={() => load(tenantId)} disabled={!tenantId}>Load status</button>
      </div>
      {error ? <div className="card error">{error}</div> : null}
      {notice ? <div className="card notice">{notice}</div> : null}
      {status ? (
        <div>
          <table>
            <tbody>
              <tr><th>Plan</th><td>{status.planName} ({status.planId})</td></tr>
              <tr><th>Status</th><td>{status.status} / {status.effectiveAccess}</td></tr>
              <tr><th>Period end (UTC)</th><td>{new Date(status.currentPeriodEndUtc).toUTCString()}</td></tr>
              <tr><th>Cancel at period end</th><td>{status.cancelAtPeriodEnd ? 'yes' : 'no'}</td></tr>
              <tr><th>Entitlement version</th><td>{status.version}</td></tr>
              <tr><th>Mutations</th><td>{status.entitlements.canMutate ? 'allowed' : `read-only: ${status.entitlements.readOnlyReason ?? ''}`}</td></tr>
              <tr><th>Portfolio limit</th><td>{status.entitlements.maxPortfolios}</td></tr>
            </tbody>
          </table>
          <div className="row">
            <input placeholder="Plan id" value={planId} onChange={(e) => setPlanId(e.target.value)} style={{ width: '10rem' }} />
            <button onClick={checkout}>Start checkout</button>
            <button onClick={cancel}>Cancel at period end</button>
            <button onClick={reconcile}>Reconcile</button>
          </div>
          {checkoutUrl ? <p>Checkout: <a href={checkoutUrl}>{checkoutUrl}</a></p> : null}
          {report ? (
            <div>
              <h3>Reconciliation</h3>
              <p>Checked {report.checked} subscriptions ({report.appliedEvents} applied, {report.ignoredEvents} ignored, {report.attention} need attention).</p>
              {report.findings.length > 0 ? (
                <ul>{report.findings.map((f, i) => <li key={i}>{f}</li>)}</ul>
              ) : <p>No mismatches.</p>}
            </div>
          ) : null}
          {events.length > 0 ? (
            <div>
              <h3>Provider events</h3>
              <table>
                <thead><tr><th>Event</th><th>Type</th><th>State</th><th>Received (UTC)</th><th>Note</th></tr></thead>
                <tbody>
                  {events.map((e) => (
                    <tr key={e.providerEventId}>
                      <td>{e.providerEventId}</td>
                      <td>{e.eventType}</td>
                      <td>{e.state}</td>
                      <td>{new Date(e.receivedAtUtc).toUTCString()}</td>
                      <td>{e.note ?? ''}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
