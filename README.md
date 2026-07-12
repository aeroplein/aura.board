# Aura Board

Aura Board is a student full-stack project for creating personal vision boards. Users can arrange draggable notes, quotes, images, and Spotify tracks on a canvas, share boards with collaborators, and optionally generate ideas with Gemini.

It is intentionally portfolio-scale rather than a production SaaS. The project focuses on a polished board-creation experience, secure server boundaries, and an honest explanation of its limits.

**Live demo:** [digital-vision-board-o5r4.onrender.com](https://digital-vision-board-o5r4.onrender.com/)

## Feature Tour

- Create, edit, and share personal vision boards.
- Arrange movable quote, note, text, image, and music cards on a canvas.
- Use optional Gemini-assisted ideas and image prompts; local starter suggestions remain available without an API key.
- Register with email verification and use HttpOnly cookie sessions.

## Tech Stack

- Frontend: Vite, vanilla JavaScript, CSS
- Backend: ASP.NET Core MVC/API on .NET 9
- Database: PostgreSQL with Entity Framework Core
- AI integration: Google Gemini API, with local fallback responses when no API key is configured
- Email: Optional SMTP invite and verification emails

## Architecture

```text
Frontend (Vite + Vanilla JavaScript)
                |
                v
      ASP.NET Core API (.NET 9)
                |
                v
       Entity Framework Core
                |
                v
            PostgreSQL

Optional Integrations:
- Google Gemini API (AI recommendations)
- SMTP Email Service (collaboration invites and email verification)
```

## Current Architecture

ASP.NET Core and PostgreSQL are the active backend. The old Express prototype has been archived at `docs/archive/legacy-express-server.js` for reference only.

The backend is organized around:

- `Controllers/`: API endpoints and request/response flow
- `Models/`: EF Core entities and DTOs
- `Services/AuthService.cs`: password hashing, login/register, and signed JWT creation/validation
- `Services/BoardService.cs`: board mapping and simple board permission helpers
- `Services/ImageStorageService.cs`: base64 image validation and database-backed storage
- `data/keys/`: local ASP.NET data-protection keys ignored by Git

## Technical Highlights

- Signed JWT authentication stored in an HttpOnly cookie with token expiration.
- PBKDF2-SHA512 password hashing with automatic upgrade for older hashes after successful login.
- Basic DTO validation for authentication, board data, collaborators, item layout bounds, and uploads.
- Simple service-layer separation for authentication, board permissions/mapping, and image storage.
- Clear collaboration permission model: owners manage board settings and invites; collaborators edit board items.
- PostgreSQL persistence through Entity Framework Core migrations.
- Safer project cleanup script that preserves local project data.
- Archived legacy Express prototype so the active ASP.NET Core backend is clear to reviewers.

## Authentication

Login/register return the user response and set a signed JWT in an HttpOnly cookie named `vision_board_auth`. The API reads this cookie on protected requests, so the frontend does not store bearer tokens in `localStorage`. Tokens expire after 8 hours.

Passwords use PBKDF2-SHA512 with 210,000 iterations for new hashes. Older 1,000-iteration hashes are still accepted on login and are upgraded after a successful login, so existing users are not intentionally locked out.

Invalid login attempts return the same generic error for unknown emails and wrong passwords. This keeps the API simple while avoiding account enumeration in a student-project-friendly way.

Registration normalizes email addresses, checks basic email shape and domain structure, optionally requires MX records, blocks configured typo/disposable domains, creates an email verification token, and sends a verification link when SMTP is configured.

The app does not pretend that signup-form validation proves a mailbox exists. DNS checks only show whether a domain is plausible for email. The verification link is the proof that the user controls the address.

Local development keeps advanced email validation enabled but disables MX checks in `appsettings.Development.json`, so test accounts are not blocked by DNS/network issues. Production-style configuration keeps `AdvancedEmailValidation:RequireMxRecord` enabled in `appsettings.json`.

## Sync Engine

Canvas item changes are persisted through `POST /api/sync`. The frontend keeps a short in-memory queue for the current browser session, sends queued actions to the backend, and updates the local board list from the server response.

The sync response includes diagnostic fields: `appliedCount`, `skippedCount`, `warnings`, and `skippedActions`. These make stale item updates, unsupported actions, missing payloads, malformed payloads, and unauthorized board actions visible instead of silently disappearing.

This is intentionally a lightweight sync model for a portfolio project. It is not a durable offline-first engine; pending actions survive only while the current tab/session remains open.

## Database Schema

Core EF Core entities:

- `Users`: account identity, password hash/salt, email verification fields, and preferences.
- `Boards`: owner-scoped vision board metadata and share status.
- `BoardItems`: draggable quote, note, image, and text cards with layout coordinates and z-index.
- `BoardCollaborators`: normalized email-based board sharing.
- `ImageFiles`: database-backed base64 image uploads served through `/api/images/{id}`.
- `ActivityLogs`: recent collaboration feed entries for board/item activity.

Important constraints include unique user email, composite collaborator key, board/item cascade deletes, item type checks, positive item sizes, and indexed board/activity lookups.

## AI Integration

Gemini powers `/api/board/recommendations` and `/api/inspiration` when `GEMINI_API_KEY` is configured. If Gemini is unavailable or not configured, the backend returns local fallback ideas so the app remains demoable.

The frontend validates AI-generated items before adding them to a board: unsupported item shapes are skipped, item types are normalized, titles/captions/colors are bounded, and card dimensions are clamped to safe canvas ranges.

## Security Honesty

The app uses real password hashing and HttpOnly cookie sessions, but it does not claim enterprise security. The note "shield" feature is reversible local obfuscation for a portfolio demo, not production-grade encryption. The README and UI describe it as obfuscation to avoid overstating the privacy guarantees.

## Portfolio Highlights

### Architecture Highlights

- Vanilla JavaScript/Vite frontend served by an ASP.NET Core backend.
- Controllers, DTOs, EF Core models, and small services are separated for readability.
- Legacy Express code is archived under `docs/archive/` so reviewers can identify the active backend quickly.

### Security Highlights

- HttpOnly cookie JWT authentication avoids browser-side token storage.
- Passwords use PBKDF2-SHA512 with stronger hashes upgraded after login.
- Auth, board, collaborator, upload, and sync inputs use scoped validation suitable for the project size.

### Frontend Highlights

- Feature-complete canvas experience without a frontend framework dependency.
- Modular JavaScript files under `src/` keep auth, boards, collaboration, sync, UI, and utilities easier to review.
- Existing visual identity and palette are preserved across dashboard, canvas, AI, and collaboration flows.

### Database Highlights

- PostgreSQL persistence is managed through Entity Framework Core migrations.
- Boards, items, collaborators, activity logs, users, and uploaded images are represented as typed entities.
- Pagination on board lists caps `pageSize` at 50 to keep small-project queries predictable.

## API Notes

- `POST /api/auth/register` creates an unverified user, sends a verification email, and returns `201 Created` without setting an auth cookie.
- Duplicate registration emails return `409 Conflict`.
- `GET /api/auth/verify-email?email={email}&token={token}` verifies an email verification token.
- `POST /api/auth/login` sets the HttpOnly JWT auth cookie only after the account email has been verified.
- `GET /api/auth/session` returns the current authenticated user from the cookie.
- `POST /api/auth/logout` clears the auth cookie.
- `POST /api/boards` returns `201 Created` and includes a `Location` header for the new board.
- `GET /api/boards?page=1&pageSize=20` supports simple pagination. `page` is normalized to at least 1, `pageSize` is capped from 1 to 50, and pagination metadata is included in `X-Total-Count`, `X-Page`, and `X-Page-Size` headers.
- `GET /api/boards/{id}` returns a board the current user owns or collaborates on.
- `PUT /api/boards/{id}` updates board settings for owners and board items for owners/collaborators.
- `DELETE /api/boards/{id}` returns `204 No Content` on success.
- `POST /api/boards/{id}/invite` sends optional collaborator invite emails when SMTP is configured.
- `GET /api/boards/activity?boardId={id}` returns recent activity for accessible boards.
- `POST /api/upload` stores a validated base64 image record and returns its image URL.
- `GET /api/images/{id}` returns the stored image bytes by generated image ID.
- `POST /api/board/recommendations` and `POST /api/inspiration` return Gemini-backed or local fallback AI suggestions.

## Permission Model

- Board owners can update board settings, share status, collaborators, and invitations.
- Collaborators can access shared boards and edit board items.
- Collaborators cannot invite other collaborators or change board settings.
- Only owners can delete boards.

This is a simple collaboration model suitable for the course project scope.

## Setup

Install frontend dependencies:

```bash
npm install
```

Restore/build the backend:

```bash
dotnet restore
dotnet build
```

Create a PostgreSQL database named `aura_board`, then update the connection string in `appsettings.json` or use environment-specific configuration:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Database=aura_board;Username=YOUR_DATABASE_USER;Password=YOUR_DATABASE_PASSWORD"
}
```

Run the backend:

```bash
npm run dev:backend
```

Local development uses the `Development` launch profile, which keeps advanced email validation enabled but skips DNS MX checks so demo/test registrations are not blocked by offline DNS or non-deliverable test domains. The base `appsettings.json` keeps `AdvancedEmailValidation:RequireMxRecord` enabled for production-style runs.

Run the frontend dev server separately if needed:

```bash
npm run dev
```

The ASP.NET app serves the built frontend from `wwwroot` when running through the backend.

## Environment Variables

Copy `.env.example` to `.env` for local development values. `.env` is ignored by Git and should not be committed.

- `JWT_SECRET`: Required for signed auth cookies. Use at least 32 characters.
- `GEMINI_API_KEY`: Optional. Enables Gemini-generated board recommendations.
- `APP_URL`: Optional. Used for invite links and as a fallback base URL for verification links.
- `APP_BASE_URL`: Optional. Used for email verification links when it differs from `APP_URL`.
- `SMTP_HOST`, `SMTP_PORT`, `SMTP_USE_SSL`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM`: Required for first-time account verification emails; also used for optional collaborator invite emails.

`appsettings.json` includes local-development placeholders so the project can run for coursework. Replace them outside local development and keep real secrets in environment variables or an untracked `.env` file.

## Main Features

- Register and log in
- Create, edit, delete, and share vision boards
- Add movable quote, note, text, and image items
- Upload image content
- Ask for AI board recommendations and inspiration prompts
- Send optional collaborator invite and email verification messages
- View lightweight board activity in the app

## Screenshots

Add only screenshots captured from this version of the app. Recommended portfolio views are:

- Dashboard / board list
- Vision board canvas
- AI recommendations panel
- Collaboration invite flow

Do not present a screenshot as a live feature if it uses sample or fallback data.

## Submission Packaging

For coursework/demo ZIP submission:

- Do not commit or submit `.env`.
- Exclude generated and local runtime folders: `node_modules`, `bin`, `obj`, `data/uploads`, and `data/keys`.
- `wwwroot` may be included when the submission needs to be ready to run without rebuilding the frontend.
- Include `.env.example`, `Migrations/`, and this README.

## Useful Scripts

```bash
npm run dev
npm run dev:backend
npm run build
npm run clean
npm run lint
npm run test:professionalization
```

`npm run clean` removes generated build folders only: `wwwroot`, `dist`, `bin`, and `obj`. It deliberately preserves `data/` and `data/uploads/`.

`npm run test:professionalization` runs a lightweight .NET console check project covering password hashing behavior, board permission helpers, upload rejection paths, strict email validation, and sync response diagnostics without requiring external test packages. GitHub Actions runs the frontend checks, backend build, and this harness on pushes and pull requests to `main`.

## Known Limitations

- JWT login is simple and does not include refresh tokens.
- The project does not use ASP.NET Identity.
- Collaboration is intentionally lightweight.
- Sync retry state is in-memory for the current browser session; it is not durable offline storage.
- Uploaded images are stored in PostgreSQL as base64 records and served through `/api/images/{id}`.
- The legacy `/data/uploads` static-file path may exist for compatibility, but uploaded user files are not required for the current database-backed image flow.
- Image storage is intentionally simple for the course project. At larger scale, image metadata should stay in the database while binary files move to disk or object storage.
- Activity logs power the board activity feed; they are not intended to be a durable production audit log.
- AI output uses Gemini when `GEMINI_API_KEY` is configured and falls back to local sample suggestions when it is missing.
- The memo shield feature is reversible local obfuscation, not production encryption.
- This repository has basic CI checks, but it does not include full production observability or deployment automation.

These limits are intentional for a student portfolio/course project and keep the code understandable at small scale.
