import { useEffect, useState } from 'react';
import { Link, NavLink, Route, Routes, useParams } from 'react-router-dom';
import { api } from './api/client';
import type { PortfolioDto } from './api/types';
import { Overview } from './pages/Overview';
import { Benchmarks } from './pages/Benchmarks';
import { ScoreConfiguration } from './pages/ScoreConfiguration';
import { Repositories } from './pages/Repositories';
import { NewPortfolio } from './pages/NewPortfolio';
import { RepositoryMetricsPage } from './pages/RepositoryMetrics';
import { PackageAdoptionPage } from './pages/PackageAdoption';
import { Decisions } from './pages/Decisions';
import { Alerts } from './pages/Alerts';

export default function App() {
  const [portfolios, setPortfolios] = useState<PortfolioDto[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    try {
      const list = await api.listPortfolios();
      setPortfolios(list);
      if (list.length > 0 && selectedId == null) {
        setSelectedId(list[0].id);
      }
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  useEffect(() => {
    refresh();
  }, []);

  return (
    <div className="layout">
      <header className="top">
        <h1><Link to="/" style={{ color: 'inherit', textDecoration: 'none' }}>Argoscope</Link></h1>
        <nav>
          {selectedId ? (
            <>
              <NavLink to={`/portfolios/${selectedId}/overview`} className={({ isActive }) => isActive ? 'active' : ''}>Overview</NavLink>
              <NavLink to={`/portfolios/${selectedId}/repositories`} className={({ isActive }) => isActive ? 'active' : ''}>Repositories</NavLink>
              <NavLink to={`/portfolios/${selectedId}/decisions`} className={({ isActive }) => isActive ? 'active' : ''}>Decisions</NavLink>
              <NavLink to={`/portfolios/${selectedId}/alerts`} className={({ isActive }) => isActive ? 'active' : ''}>Alerts</NavLink>
              <NavLink to={`/portfolios/${selectedId}/benchmarks`} className={({ isActive }) => isActive ? 'active' : ''}>Benchmarks</NavLink>
              <NavLink to={`/portfolios/${selectedId}/score`} className={({ isActive }) => isActive ? 'active' : ''}>Score</NavLink>
            </>
          ) : null}
          <NavLink to="/new" className={({ isActive }) => isActive ? 'active' : ''}>New portfolio</NavLink>
        </nav>
      </header>
      {error ? <div className="card error">Failed to load portfolios: {error}</div> : null}
      <Routes>
        <Route path="/" element={<Home portfolios={portfolios} onSelect={setSelectedId} onRefresh={refresh} />} />
        <Route path="/new" element={<NewPortfolio onCreated={(p) => { setSelectedId(p.id); refresh(); }} />} />
        <Route path="/portfolios/:portfolioId/overview" element={<PortfolioShell><Overview /></PortfolioShell>} />
        <Route path="/portfolios/:portfolioId/repositories" element={<PortfolioShell><Repositories /></PortfolioShell>} />
        <Route path="/portfolios/:portfolioId/decisions" element={<PortfolioShell><Decisions /></PortfolioShell>} />
        <Route path="/portfolios/:portfolioId/alerts" element={<PortfolioShell><Alerts /></PortfolioShell>} />
        <Route path="/portfolios/:portfolioId/benchmarks" element={<PortfolioShell><Benchmarks /></PortfolioShell>} />
        <Route path="/portfolios/:portfolioId/score" element={<PortfolioShell><ScoreConfiguration /></PortfolioShell>} />
        <Route path="/repositories/:repositoryId" element={<RepositoryMetricsPage />} />
        <Route path="/repositories/:repositoryId/adoption" element={<RepositoryAdoptionRoute />} />
      </Routes>
    </div>
  );
}

function RepositoryAdoptionRoute() {
  const { repositoryId } = useParams();
  if (!repositoryId) return <div className="card error">Missing repository id.</div>;
  return <PackageAdoptionPage repositoryId={repositoryId} />;
}

function Home({ portfolios, onSelect, onRefresh }: { portfolios: PortfolioDto[]; onSelect: (id: string) => void; onRefresh: () => void }) {
  if (portfolios.length === 0) {
    return (
      <div className="card">
        <p>No portfolios yet. Create one to start collecting GitHub metrics.</p>
        <Link to="/new" className="btn">Create portfolio</Link>
      </div>
    );
  }
  return (
    <div className="card">
      <h2>Portfolios</h2>
      <table>
        <thead><tr><th>Name</th><th>Created (UTC)</th><th></th></tr></thead>
        <tbody>
          {portfolios.map(p => (
            <tr key={p.id}>
              <td>{p.name}</td>
              <td>{new Date(p.createdAtUtc).toUTCString()}</td>
              <td><button onClick={() => { onSelect(p.id); onRefresh(); }}>Open</button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PortfolioShell({ children }: { children: React.ReactNode }) {
  const { portfolioId } = useParams();
  if (!portfolioId) return <div className="card error">Missing portfolio id.</div>;
  return <PortfolioContext.Provider value={portfolioId}>{children}</PortfolioContext.Provider>;
}

import { createContext, useContext } from 'react';
export const PortfolioContext = createContext<string>('');
export function usePortfolioId(): string {
  return useContext(PortfolioContext);
}
