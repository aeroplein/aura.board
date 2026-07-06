# Security Release Checklist

Use this before deploying a release. The checklist is based on OWASP Top 10 and OWASP ASVS Level 1 expectations for a small web application.

## Configuration And Secrets

- [ ] `ASPNETCORE_ENVIRONMENT=Production` is set for production.
- [ ] `ASPNETCORE_ALLOWEDHOSTS` lists the deployed host names and does not use `*`.
- [ ] `JWT_SECRET` is unique, at least 32 characters, and not stored in Git.
- [ ] `DATABASE_URL` or `DEFAULT_CONNECTION` points to the production database and does not use placeholder/local credentials.
- [ ] `.env` is not committed.
- [ ] Optional provider keys (`GEMINI_API_KEY`, `UNSPLASH_ACCESS_KEY`, SMTP credentials) are real only in the target environment and are rotated if exposed.

## Transport Security

- [ ] HTTPS is enabled for the public site.
- [ ] HSTS is enabled in production.
- [ ] If TLS terminates at a reverse proxy, forwarded headers are configured and direct app access is restricted.
- [ ] Auth cookies are `HttpOnly`, `Secure` in production, and `SameSite=Lax`.

## Authentication And Authorization

- [ ] Login and registration return generic failures where account enumeration matters.
- [ ] JWTs are stored only in the HttpOnly cookie, not in `localStorage` or `sessionStorage`.
- [ ] Board reads and writes require the current user to be the owner or an invited collaborator.
- [ ] Collaborators cannot change board settings or collaborator lists.
- [ ] Uploaded images require authorization: uploader access or access to a board item referencing the image.
- [ ] Admin MFA is marked not applicable unless admin roles/accounts are introduced.

## Input Validation And Output Handling

- [ ] Board title, description, category, collaborator list, and item counts respect server-side limits.
- [ ] Board item IDs, titles, captions, content, positions, sizes, and z-index respect server-side limits.
- [ ] Avatar URLs are HTTPS, local HTTP for development, or uploaded `/api/images/{id}` paths.
- [ ] Uploads are limited to supported image MIME types and 15 MB decoded size.
- [ ] User-generated titles/content are HTML-escaped before rendering.

## Dependency And Supply Chain Review

- [ ] Run backend dependency audit:

```powershell
dotnet list package --vulnerable --include-transitive
```

- [ ] Run frontend dependency audit:

```powershell
npm audit --audit-level=moderate
```

- [ ] Review Dependabot PRs before release.
- [ ] Do not deploy with known high or critical vulnerabilities unless a documented false positive or compensating control exists.

## Logging And Privacy

- [ ] Logs do not include passwords, JWTs, cookies, verification tokens, API keys, private keys, or session values.
- [ ] Logs prefer user IDs over emails.
- [ ] Activity logs avoid raw board/card titles and long user-provided content.
- [ ] Third-party API failures do not log full response bodies.

## Backups And Recovery

- [ ] Database backup schedule and retention are current in `docs/OPERATIONS.md`.
- [ ] Backup storage location is filled in for the deployment.
- [ ] A restore test was performed for the release or the current deployment cycle.
- [ ] Restore test included auth, board access, collaboration, and image access checks.

## Manual/ZAP Review

- [ ] Run a basic OWASP ZAP baseline scan against a staging URL.
- [ ] Manually test private board access with two different users.
- [ ] Manually test collaborator board access and settings restrictions.
- [ ] Manually test image access with uploader, collaborator, unrelated authenticated user, and anonymous browser.
- [ ] Manually test CORS from the deployed frontend origin only.

## Release Decision

- [ ] All required checklist items are complete, or each exception has a tracked issue and accepted risk.
- [ ] README/project documentation states the app is portfolio-grade and not a fully production-hardened SaaS.
