# Design: Hosted multi-user identity

## Implementation boundary
Argoscope .NET 10 API/Web and PostgreSQL. Add OIDC authentication middleware, `Tenant`, `TenantMembership`, tenant-scoped resource keys, authorization policies and migration mapping existing single-owner rows to a designated initial tenant. Inspect all repositories, background jobs, exports and caches. No changes to dotnet-platform-libs; consume compatible published packages only after review.

## Language and runtime
C# / ASP.NET Core 10, EF Core/PostgreSQL, React/TypeScript. Verify format/build/tests, migration integration tests, frontend build/tests and strict OpenSpec validation.

## Ownership and shared code
Argoscope owns tenant membership and resource authorization rules. OIDC protocol libraries may be adopted as dependencies; product data policies stay local. Tenant ID is mandatory through Application query context and persistence filters, not only controller checks.

## Behavioral model
OIDC subject maps to immutable external identity. First authenticated owner creates a tenant; invites are single-use, expiring (7 days) and role-bound. Membership state `Invited|Active|Revoked`; role `Owner|Editor|Viewer`. At least one active Owner per tenant invariant. Every request resolves tenant membership before resource lookup; cross-tenant resource returns 404. Background jobs carry explicit tenant ID and verify resource ownership. Migration runs transactionally, assigns all existing rows to a migration tenant, verifies zero null tenant IDs, then enables NOT NULL/foreign keys. Cache keys include tenant ID.

## Contract and compatibility
OIDC issuer/client ID/secret/redirect URI in deployment config; secrets never in DB/logs. API returns 401 unauthenticated, 404 missing or foreign resource, 403 insufficient role, 409 last-owner/revoked invite conflict. Membership endpoints are owner-managed; editor can modify portfolio data; viewer read-only. Self-hosted single-owner mode remains a deployment profile mapping local owner identity to a tenant without external OIDC. Session uses secure, HttpOnly, SameSite cookies with CSRF protection. New tenant IDs are additive in schemas but migration is mandatory before hosted rollout.

## Failure and boundary policy
OIDC outage fails closed for new login while valid bounded sessions continue until expiry; invalid issuer/audience rejects token; invite replay/expiry denied; role downgrade cannot remove last owner; migration failure rolls back; missing tenant context is internal error and never falls back to global query. No account data is returned cross-tenant.

## Verification oracle
Integration tests create two tenants and prove list/get/update/job/export isolation for every resource type; migration test verifies preexisting owner data assigned and null-free; auth tests cover issuer, expiry, CSRF, invite replay, role matrix and last-owner invariant. Build/frontend/strict validation required before staged rollout.

## Decision ledger
- Use external OIDC; no local passwords.
- Tenant scoping is enforced in application and database query layers.
- Hosted rollout is blocked until complete resource inventory and migration test pass; hosted operations is a dependent package.
- Public sharing and organization federation deferred.
