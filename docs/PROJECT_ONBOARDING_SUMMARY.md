# Project Onboarding Summary: Digital Vision Board

Use this document to onboard a new AI assistant or context window to the current status of the Digital Vision Board project.

The active backend is ASP.NET Core with PostgreSQL and Entity Framework Core. Older Express/prototype notes should be treated as historical only; archived legacy material belongs under `docs/archive/`.

---

## 1. Project Overview & Concept

*   **Project Name:** Digital Vision Board
*   **Concept:** A student portfolio/course project where users create interactive vision boards with notes, quotes, text blocks, uploaded images, AI-assisted inspiration, and lightweight collaboration.

---

## 2. Technical Stack

*   **Backend:** ASP.NET Core 9 MVC/API.
*   **Database:** PostgreSQL through Entity Framework Core migrations.
*   **Frontend:** Vanilla JavaScript modules, Vite, HTML, and CSS.
*   **AI Integration:** Google Gemini API when `GEMINI_API_KEY` is configured, with local fallback responses when it is not.
*   **Email:** Optional SMTP invite emails for board collaborators.

---

## 3. Database Schema (EF Core Models)

*   [User.cs](file:///c:/Users/pelin/antigravity/Digital-Vision-Board/Models/User.cs): Stores user identity, password hashes, PBKDF2 salts, and workspace layout preferences.
*   [Board.cs](file:///c:/Users/pelin/antigravity/Digital-Vision-Board/Models/Board.cs): Represents a visual canvas linked to an owner.
*   [BoardItem.cs](file:///c:/Users/pelin/antigravity/Digital-Vision-Board/Models/BoardItem.cs): Represents individual board elements such as `note`, `quote`, `image`, and `text`, including content, layout, captions, and color metadata.
*   [BoardCollaborator.cs](file:///c:/Users/pelin/antigravity/Digital-Vision-Board/Models/BoardCollaborator.cs): Composite primary key table mapping shared boards to normalized collaborator email strings.
*   [ImageFile.cs](file:///c:/Users/pelin/antigravity/Digital-Vision-Board/Models/ImageFile.cs): Stores base64-encoded image payloads and MIME types.
*   [ActivityLog.cs](file:///c:/Users/pelin/antigravity/Digital-Vision-Board/Models/ActivityLog.cs): Activity-feed records linked to boards and users.

---

## 4. Current Progress Status

*   **Progress Level:** Submission-ready student project.
*   **Completed Milestones:**
    *   **User Registry & Login:** Custom authentication using signed JWTs stored in an HttpOnly cookie named `vision_board_auth`. Passwords use PBKDF2-SHA512 with per-user salts.
    *   **Board CRUD:** Create, read, update, and delete capability protected by owner/collaborator permission checks.
    *   **Board Canvas:** Movable notes, quotes, text, and image items are persisted through the ASP.NET Core API.
    *   **Collaboration Activity Feed:** Shared board access and recent activity are database-backed.
    *   **Image Uploads:** Validated image uploads are stored as base64 image records and served through `/api/images/{id}`.
    *   **AI Suggestions:** AI endpoints use Gemini when configured and local fallback content otherwise.

---

## 5. Next Steps for Submission/Portfolio

1.  Add real screenshots to the README before public portfolio publishing.
2.  For local demo setup, configure PostgreSQL and copy `.env.example` to an untracked `.env`.
3.  Keep generated/local runtime folders out of submission ZIPs unless `wwwroot` is intentionally included for ready-to-run coursework delivery.

---

## 6. How We Learn

*   **Study Guide:** Detailed notes are maintained chronologically in [docs/LEARNING_JOURNAL.md](file:///c:/Users/pelin/antigravity/Digital-Vision-Board/docs/LEARNING_JOURNAL.md).
*   **Instruction Rules:** Keep explanations concrete and tied to actual files, controllers, services, models, migrations, and README claims.
