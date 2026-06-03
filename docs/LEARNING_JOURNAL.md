# Step 1.1 — Sync Conflict Resolution

## What We Built
We fixed a critical timeline bug in the backend synchronization engine (`SyncController.cs`). The engine processes local changes queued from client devices and merges them into the PostgreSQL cloud database.

## Concepts Learned

### 1. Controller & Route
*   **Controller:** A C# class (inheriting from `ControllerBase`) that groups methods to handle incoming HTTP requests (like GET, POST, PUT, DELETE). Without `ControllerBase`, our classes wouldn't have access to context properties like `Request` or helper response methods like `Ok()`, `BadRequest()`, and `Unauthorized()`.
*   **Route:** An attribute (`[Route("api/sync")]`) that configures ASP.NET Core to map incoming HTTP request paths to a controller.
*   **Attributes:** In C#, attributes are metadata tags enclosed in square brackets (e.g., `[Route]`, `[ApiController]`, `[HttpPost]`) placed above classes or methods. The framework reads these tags at runtime to decide how to handle request routing and validation.

### 2. Dependency Injection & Service Container
*   **Service Container:** The startup configuration registry that manages classes (like database connections).
*   **Dependency Injection (DI) & Constructor Injection:** The mechanism where ASP.NET Core automatically resolves dependencies and passes the database connection (`AppDbContext`) to the controller's constructor when instantiated, rather than us manually calling `new AppDbContext()`.

### 3. Model Binding
*   **Model Binding:** The automatic deserialization of the incoming HTTP request payload (JSON string) into structured C# class/record objects (e.g., `SyncRequest`).

### 4. Fields, Readonly, and Access Modifiers
*   **Fields:** Variables declared directly within a class (e.g., `_context`) rather than inside a method. By C# convention, class-level fields are prefixed with an underscore (`_`) to distinguish them from parameters and local variables.
*   **Access Modifiers:** Keywords controlling visibility:
    *   `public`: Accessible by any other class or framework module.
    *   `private`: Only accessible inside the defining class itself.
    *   `protected`: Accessible within the defining class and any subclass inheriting from it (e.g., `SyncController` inheriting `_context` from `BaseApiController`).
*   **Readonly:** A C# keyword indicating that a field can only be assigned during its declaration or inside the constructor. This guarantees the reference cannot be accidentally reassigned or set to `null` during request execution.

### 5. DbContext & EF Core Tracking
*   **DbContext:** The class representing a database session (`AppDbContext`), mapping database models directly to PostgreSQL tables.
*   **Tracking:** Entity Framework tracks modifications in-memory. Calling `await _context.SaveChangesAsync()` translates all of our tracked modifications into SQL statements and writes them to the database.

---

## Code We Added

We corrected the timestamp assignment in `SyncController.cs` for the canvas item edit/delete actions:

```csharp
// Inside upsert_item action
board.UpdatedAt = actionItem.Timestamp;

// Inside delete_item action
board.UpdatedAt = actionItem.Timestamp;
```

---

## Why We Added It
Previously, the backend used the server's local processing time (`DateTime.UtcNow`) as the board's update timestamp. In a multi-device setup, this broke sync merges because a server update would push the board's timestamp into the future. Other client devices trying to sync valid, older changes would have their updates rejected. By using the client's `actionItem.Timestamp`, we preserve the real-time chronological timeline across all devices.

---

## Request Flow

```
Browser (app.js)
       ↓
  POST /api/sync   (JSON queue of actions)
       ↓
 ASP.NET Core Router
       ↓
 Model Binding     (JSON → C# SyncRequest)
       ↓
 SyncController.Sync()
       ↓
 AppDbContext      (Validates timeline)
       ↓
 SaveChangesAsync() (Writes to PostgreSQL)
       ↓
   Response        (JSON representation of updated boards)
```

---

## Common Mistakes
*   **Timezone Discrepancy:** Mixing Local Time and UTC. Always standardise timestamps to UTC (`DateTime.UtcNow` or ISO-8601 UTC strings) on both front-end and back-end to avoid chronological calculation errors.
*   **Untracked Entity Errors:** Modifying properties of database entities that were not loaded into the active DbContext tracker.

---

## Key Takeaways
1.  **LWW (Last-Write-Wins):** Always rely on the client event timestamp rather than server clock time when merging distributed offline-first client actions.
2.  **State Tracking:** EF Core only tracks objects that are loaded or added. Changes are saved atomically when `SaveChangesAsync()` is called.

---

## Personal Notes
*(Use this section to write down your own observations, queries, or interview study points!)*


# Step 1.2 — Board Collaboration (Real-Time Activity Feed)

## What We Built
We upgraded the collaboration feed from using static client-side timer mock prompts to a live database-backed activity log feed. Every card action (creation, updates, deletion) now gets batch-saved to PostgreSQL, and the client polls the server to update the collaboration sidebar.

## Concepts Learned

### 1. GUID Primary Keys
*   **Guid (Globally Unique Identifier):** A 128-bit integer guaranteed to be unique across all databases. Standard auto-incrementing IDs (1, 2, 3...) fail in offline-first systems because two devices offline might both generate ID `5` for different items, causing collisions during sync. GUIDs can be generated on the client or server without coordinate checks, eliminating conflicts.

### 2. Optional Route Parameters and FromQuery
*   **[FromQuery]:** An attribute directing ASP.NET Core model binding to extract variables from the URL query string (e.g., `?boardId=xxxx`) rather than the URL path route segment.
*   **Nullable Types (`Guid?`):** Declares that a variable can either hold a valid Guid or be `null`.

### 3. LINQ Batch Queries & Select Projections
*   **`.Where(al => accessibleBoardIds.Contains(al.BoardId))`:** Generates a database-side `IN` query.
*   **`.Select(...)`:** Custom query projections. Instead of loading the entire `ActivityLog` object from SQL, we project only the required subset of properties (like `Id`, `UserEmail`, etc.), reducing RAM usage and HTTP payload overhead.
*   **`.Take(5)`:** Limits database fetch boundaries (SQL `LIMIT 5`).

---

## Code We Added

1.  **ActivityLog Model:** Created `ActivityLog.cs` database blueprint.
2.  **AppDbContext Registration:** Added `DbSet<ActivityLog>` and configured cascade deletes.
3.  **SyncController Logs Hook:** Batch-accumulated and saved activity logs inside `SyncController.cs`.
4.  **Collaboration Endpoint:** Built `CollaborationController.cs` exposing `api/boards/activity`.
5.  **Frontend Live Ticker:** Replaced mock array updates with live HTTP fetch logic in `app.js`.

---

## Why We Added It
A real collaboration system needs accurate visibility. Writing edit logs to the database ensures that when multiple collaborators edit different areas of a board offline or online, the dashboard updates immediately to reflect who made changes and what cards were modified.

---

## Request Flow

```
   Browser (app.js Ticker)
              ↓ 
   GET /api/boards/activity   (Polled every 12s)
              ↓
   CollaborationController
              ↓
  Queries DbContext (LINQ Projection)
              ↓
     PostgreSQL Database
              ↓
   Response (Latest 5 activity logs as JSON)
```

---

## Common Mistakes
*   **Saving Database Calls in Loops:** Running save updates inside iteration scopes causes massive network overhead. Always queue data edits in memory and run one batch insert using `AddRange()` and `SaveChangesAsync()`.

---

## Key Takeaways
1.  **GUIDs for Offline-First:** Always choose GUIDs over integers for identifiers when building sync engines.
2.  **Query Projections:** Keep payload sizes small by selecting only the data fields the UI actually renders.

---

## Personal Notes
*(Use this section to write down your own observations, queries, or interview study points!)*
