import type {
  BenchmarkReport,
  CollectionRunResult,
  MembershipDto,
  PackageAdoptionReport,
  PackageAssociationDto,
  PackageCollectionRunResult,
  PackageProvider,
  PackageUnit,
  PackageWindow,
  PortfolioDto,
  PortfolioOverview,
  RepositoryMetrics,
  ScoreConfigurationDto,
} from './types';

const base = '/api/v1';

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const init: RequestInit = {
    method,
    headers: { 'content-type': 'application/json' },
  };
  if (body !== undefined) init.body = JSON.stringify(body);
  const res = await fetch(`${base}${path}`, init);
  if (!res.ok) {
    const detail = await res.text();
    throw new Error(`${method} ${path} -> ${res.status} ${detail}`);
  }
  if (res.status === 204) return undefined as unknown as T;
  return (await res.json()) as T;
}

export const api = {
  listPortfolios: () => request<PortfolioDto[]>('GET', '/portfolios'),
  createPortfolio: (name: string) => request<PortfolioDto>('POST', '/portfolios', { name }),
  getOverview: (portfolioId: string, window: '7d' | '30d' = '30d') =>
    request<PortfolioOverview>('GET', `/portfolios/${portfolioId}/overview?window=${window}`),
  getBenchmarks: (portfolioId: string, window: '7d' | '30d' = '30d') =>
    request<BenchmarkReport>('GET', `/portfolios/${portfolioId}/benchmarks?window=${window}`),
  listRepositories: (portfolioId: string) =>
    request<MembershipDto[]>('GET', `/portfolios/${portfolioId}/repositories`),
  addRepository: (portfolioId: string, body: {
    nodeId: string;
    ownerLogin: string;
    name: string;
    visibility: string;
    role: string;
    category: string | null;
    lifecycle: string;
  }) => request<MembershipDto>('POST', `/portfolios/${portfolioId}/repositories`, body),
  removeRepository: (portfolioId: string, repositoryId: string) =>
    request<void>('DELETE', `/portfolios/${portfolioId}/repositories/${repositoryId}`),
  updateMembership: (portfolioId: string, repositoryId: string, body: {
    category: string | null;
    lifecycle: string;
  }) => request<MembershipDto>('PUT', `/portfolios/${portfolioId}/repositories/${repositoryId}`, body),
  getRepositoryMetrics: (repositoryId: string) =>
    request<RepositoryMetrics>('GET', `/repositories/${repositoryId}/metrics`),
  collectNow: (portfolioId: string, repositoryId: string) =>
    request<CollectionRunResult>('POST', `/portfolios/${portfolioId}/repositories/${repositoryId}/collect`),
  getScoreConfiguration: (portfolioId: string) =>
    request<ScoreConfigurationDto>('GET', `/portfolios/${portfolioId}/score-configuration`),
  putScoreConfiguration: (portfolioId: string, factors: Array<{ name: string; weight: number; enabled: boolean }>) =>
    request<ScoreConfigurationDto>('PUT', `/portfolios/${portfolioId}/score-configuration`, { factors }),
  listPackageAssociations: (repositoryId: string) =>
    request<PackageAssociationDto[]>('GET', `/repositories/${repositoryId}/packages`),
  createPackageAssociation: (repositoryId: string, body: {
    provider: PackageProvider;
    coordinate: string;
    defaultUnit?: PackageUnit | null;
    defaultWindow?: PackageWindow | null;
  }) => request<PackageAssociationDto>('POST', `/repositories/${repositoryId}/packages`, body),
  updatePackageAssociation: (repositoryId: string, associationId: string, body: {
    defaultUnit: PackageUnit;
    defaultWindow: PackageWindow;
  }) => request<PackageAssociationDto>('PUT', `/repositories/${repositoryId}/packages/${associationId}`, body),
  removePackageAssociation: (repositoryId: string, associationId: string) =>
    request<void>('DELETE', `/repositories/${repositoryId}/packages/${associationId}`),
  collectPackageNow: (repositoryId: string, associationId: string) =>
    request<PackageCollectionRunResult>('POST', `/repositories/${repositoryId}/packages/${associationId}/collect`),
  getPackageAdoption: (repositoryId: string) =>
    request<PackageAdoptionReport>('GET', `/repositories/${repositoryId}/adoption`),
};
