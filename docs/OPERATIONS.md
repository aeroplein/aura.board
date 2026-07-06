# Operations Runbook

This runbook is for a student/portfolio deployment of Digital Vision Board. Replace placeholders before using it for a real production environment.

## Database Backups

- Database: PostgreSQL.
- Schedule: daily logical backup with `pg_dump`; increase to hourly snapshots if real users depend on the app.
- Retention: keep 7 daily backups, 4 weekly backups, and 3 monthly backups.
- Storage location: `[REPLACE_WITH_BACKUP_BUCKET_OR_SECURE_STORAGE_LOCATION]`.
- Storage controls: encrypt backups at rest, restrict access to the app owner/maintainer, and do not commit backups to Git.
- Naming convention: `digital-vision-board_yyyy-mm-dd_hhmmss.sql.gz`.

Example backup command:

```powershell
pg_dump "$env:DATABASE_URL" | gzip > "digital-vision-board_$(Get-Date -Format yyyy-MM-dd_HHmmss).sql.gz"
```

## Restore Steps

1. Confirm the restore target database is the intended environment.
2. Pause application writes or put the app in maintenance mode.
3. Download the selected backup from secure storage.
4. Restore into a clean database first, not directly over production.
5. Run the application migrations against the restored database.
6. Smoke test login, board loading, collaboration access, and uploaded image access.
7. Promote the restored database only after the smoke test passes.

Example restore command:

```powershell
gunzip -c .\digital-vision-board_yyyy-mm-dd_hhmmss.sql.gz | psql "$env:DATABASE_URL"
```

## Restore-Test Checklist

- Backup file can be downloaded by an authorized maintainer.
- Backup decrypts/decompresses successfully.
- Restore completes without SQL errors.
- `dotnet build` succeeds after restore.
- A test user can sign in.
- A private board is not visible to another user.
- A shared board is visible to an invited collaborator.
- Uploaded `/api/images/{id}` content is visible only to the uploader or users with access to a board referencing the image.
- Activity logs do not expose raw email addresses or user card content.

## TLS And Reverse Proxy Notes

Production should serve public traffic over HTTPS. If TLS terminates at a reverse proxy or hosting platform, set forwarded headers correctly and restrict direct access to the app process so clients cannot spoof `X-Forwarded-Proto`.

## Incident Notes

If a secret leaks, rotate it immediately:

- `JWT_SECRET`
- `DATABASE_URL` / database user password
- `GEMINI_API_KEY`
- `UNSPLASH_ACCESS_KEY`
- SMTP credentials
