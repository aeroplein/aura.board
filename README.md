# Digital Vision Board

Digital Vision Board is a student portfolio/course project for creating personal vision boards with movable notes, quotes, images, AI-assisted inspiration, and lightweight collaboration.

This project is intentionally small-scale. It is designed to look professional, maintainable, and credible for coursework without pretending to be enterprise production software.

## Tech Stack

- Frontend: Vite, vanilla JavaScript, CSS
- Backend: ASP.NET Core MVC/API on .NET 9
- Database: PostgreSQL with Entity Framework Core
- AI integration: Google Gemini API, with local fallback responses when no API key is configured
- Email: Optional SMTP invite emails for board collaborators

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
- SMTP Email Service (collaboration invites)
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

- `POST /api/auth/register` returns `201 Created` when a user is registered.
- Duplicate registration emails return `409 Conflict`.
- `POST /api/auth/login` sets the HttpOnly JWT auth cookie.
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

Run the frontend dev server separately if needed:

```bash
npm run dev
```

The ASP.NET app serves the built frontend from `wwwroot` when running through the backend.

## Environment Variables

Copy `.env.example` to `.env` for local development values. `.env` is ignored by Git and should not be committed.

- `JWT_SECRET`: Required for signed auth cookies. Use at least 32 characters.
- `GEMINI_API_KEY`: Optional. Enables Gemini-generated board recommendations.
- `APP_URL`: Optional. Used for invite links.
- `SMTP_HOST`, `SMTP_PORT`, `SMTP_USE_SSL`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM`: Optional invite email settings.

`appsettings.json` includes local-development placeholders so the project can run for coursework. Replace them outside local development and keep real secrets in environment variables or an untracked `.env` file.

## Main Features

- Register and log in
- Create, edit, delete, and share vision boards
- Add movable quote, note, text, and image items
- Upload image content
- Ask for AI board recommendations and inspiration prompts
- Send optional collaborator invite emails
- View recent board activity

## Screenshots

No final portfolio screenshots are included in this repository yet. Keep this placeholder section for coursework submission, then add real image references before publishing the project publicly as a portfolio piece.

Recommended screenshots to add before portfolio publishing:

- Dashboard / board list
- Vision board canvas
- AI recommendations panel
- Collaboration invite flow

Only reference screenshot files here after they actually exist in the repository.

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
```

`npm run clean` removes generated build folders only: `wwwroot`, `dist`, `bin`, and `obj`. It deliberately preserves `data/` and `data/uploads/`.

## Known Limitations

- JWT login is simple and does not include refresh tokens.
- The project does not use ASP.NET Identity.
- Collaboration is intentionally lightweight.
- Uploaded images are stored in PostgreSQL as base64 records and served through `/api/images/{id}`.
- The legacy `/data/uploads` static-file path may exist for compatibility, but uploaded user files are not required for the current database-backed image flow.
- Image storage is intentionally simple for the course project. At larger scale, image metadata should stay in the database while binary files move to disk or object storage.
- Activity logs power the board activity feed; they are not intended to be a durable production audit log.
- AI output uses Gemini when `GEMINI_API_KEY` is configured and falls back to local sample suggestions when it is missing.
- There is no full production observability or CI/CD pipeline.

These limits are intentional for a student portfolio/course project and keep the code understandable at small scale.
