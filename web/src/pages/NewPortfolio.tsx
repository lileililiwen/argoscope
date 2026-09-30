import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { PortfolioDto } from '../api/types';

export function NewPortfolio({ onCreated }: { onCreated: (p: PortfolioDto) => void }) {
  const [name, setName] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nav = useNavigate();

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    setSubmitting(true);
    setError(null);
    try {
      const p = await api.createPortfolio(name.trim());
      onCreated(p);
      nav(`/portfolios/${p.id}/overview`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="card">
      <h2>New portfolio</h2>
      <form onSubmit={submit}>
        <label>
          Name
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Open-source observability portfolio"
            disabled={submitting}
          />
        </label>
        <p className="muted">The owner controls this portfolio and the lifecycle labels of its repositories.</p>
        {error ? <p className="error">{error}</p> : null}
        <button type="submit" disabled={submitting || !name.trim()}>Create</button>
      </form>
    </div>
  );
}
