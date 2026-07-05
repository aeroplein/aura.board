using System.Reflection;
using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;
using Microsoft.EntityFrameworkCore;

var checks = new List<(string Name, Action Check)>
{
    ("AuthService hashes and verifies passwords", CheckPasswordHashing),
    ("BoardService enforces owner and collaborator access", CheckBoardServiceAccess),
    ("BoardService normalizes collaborators and item types", CheckBoardServiceNormalization),
    ("ImageStorageService rejects invalid upload payloads before database writes", CheckImageUploadValidation),
    ("Strict email validation rejects malformed domains", CheckStrictEmailValidation),
    ("SyncResponse exposes diagnostics without breaking board payloads", CheckSyncResponseDiagnostics)
};

var failures = new List<string>();
foreach (var (name, check) in checks)
{
    try
    {
        check();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Professionalization checks failed:");
    foreach (var failure in failures)
    {
        Console.WriteLine($"- {failure}");
    }

    Environment.ExitCode = 1;
}

static void CheckPasswordHashing()
{
    var hashMethod = typeof(AuthService).GetMethod("HashPassword", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("HashPassword method not found.");
    var verifyMethod = typeof(AuthService).GetMethod("VerifyPassword", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("VerifyPassword method not found.");

    var hashResult = hashMethod.Invoke(null, new object[] { "portfolio-pass-123" })
        ?? throw new InvalidOperationException("HashPassword returned null.");

    var salt = (string)(hashResult.GetType().GetField("Salt")?.GetValue(hashResult)
        ?? hashResult.GetType().GetProperty("Salt")?.GetValue(hashResult)
        ?? hashResult.GetType().GetField("Item1")?.GetValue(hashResult)
        ?? throw new InvalidOperationException("Salt missing from hash result."));
    var passwordHash = (string)(hashResult.GetType().GetField("PasswordHash")?.GetValue(hashResult)
        ?? hashResult.GetType().GetProperty("PasswordHash")?.GetValue(hashResult)
        ?? hashResult.GetType().GetField("Item2")?.GetValue(hashResult)
        ?? throw new InvalidOperationException("PasswordHash missing from hash result."));

    var user = new User
    {
        Email = "test@example.com",
        Name = "Test User",
        Salt = salt,
        PasswordHash = passwordHash
    };

    var args = new object[] { "portfolio-pass-123", user, false };
    var verified = (bool)(verifyMethod.Invoke(null, args) ?? false);
    Assert(verified, "Expected hashed password to verify.");
    Assert(passwordHash.StartsWith("pbkdf2_sha512$210000$", StringComparison.Ordinal), "Expected current PBKDF2 format.");
    Assert(args[2] is false, "Fresh hash should not need rehash.");

    var wrongArgs = new object[] { "wrong-password", user, false };
    var rejected = (bool)(verifyMethod.Invoke(null, wrongArgs) ?? true);
    Assert(!rejected, "Wrong password should not verify.");
}

static void CheckBoardServiceAccess()
{
    var service = new BoardService();
    var owner = NewUser("owner@example.com");
    var collaborator = NewUser("collab@example.com");
    var outsider = NewUser("outsider@example.com");
    var board = new Board
    {
        Title = "Portfolio Board",
        OwnerId = owner.Id,
        Collaborators = new List<BoardCollaborator>
        {
            new() { BoardId = Guid.Empty, CollaboratorEmail = "collab@example.com" }
        }
    };

    Assert(service.CanAccess(board, owner), "Owner should access board.");
    Assert(service.CanAccess(board, collaborator), "Collaborator should access board.");
    Assert(!service.CanAccess(board, outsider), "Outsider should not access board.");
}

static void CheckBoardServiceNormalization()
{
    var service = new BoardService();
    var boardId = Guid.NewGuid();
    var collaborators = service.BuildCollaborators(boardId, new[] { " A@Example.com ", "a@example.com", "b@example.com" });

    Assert(collaborators.Count == 2, "Duplicate collaborator emails should be collapsed.");
    Assert(collaborators.All(c => c.CollaboratorEmail == c.CollaboratorEmail.ToLowerInvariant()), "Collaborator emails should be lowercase.");
    Assert(service.NormalizeBoardItemType("IMAGE") == "image", "Known item type should normalize.");
    Assert(service.NormalizeBoardItemType("unknown") == "note", "Unknown item type should fall back to note.");
}

static void CheckImageUploadValidation()
{
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=aura_board;Username=postgres;Password=postgres")
        .Options;
    using var context = new AppDbContext(options);
    var service = new ImageStorageService(context);

    AssertThrows<InvalidDataException>(() => service.SaveBase64ImageAsync(new UploadRequest("not-base64", "image/png", "bad.png")).GetAwaiter().GetResult(), "Invalid base64 should be rejected.");
    AssertThrows<InvalidDataException>(() => service.SaveBase64ImageAsync(new UploadRequest(Convert.ToBase64String(new byte[] { 1, 2, 3 }), "image/svg+xml", "bad.svg")).GetAwaiter().GetResult(), "Unsupported MIME should be rejected.");

    var oversized = Convert.ToBase64String(new byte[(15 * 1024 * 1024) + 1]);
    AssertThrows<InvalidDataException>(() => service.SaveBase64ImageAsync(new UploadRequest(oversized, "image/png", "too-large.png")).GetAwaiter().GetResult(), "Oversized image should be rejected.");
}

static void CheckStrictEmailValidation()
{
    var validator = new StrictEmailAddressAttribute();

    Assert(validator.IsValid("person@example.com"), "Normal email should validate.");
    Assert(!validator.IsValid("person@example"), "Email without TLD should fail.");
    Assert(!validator.IsValid("person@x.com"), "Too-short domain label should fail.");
    Assert(!validator.IsValid("person@example.c1"), "Non-letter TLD should fail.");
}

static void CheckSyncResponseDiagnostics()
{
    var skipped = new SyncSkippedActionDto("upsert_item", Guid.NewGuid(), "item-1", "Item update is stale and was not applied.");
    var response = new SyncResponse(
        true,
        new List<BoardResponse>(),
        DateTime.UtcNow,
        AppliedCount: 2,
        SkippedCount: 1,
        Warnings: new List<string> { skipped.Reason },
        SkippedActions: new List<SyncSkippedActionDto> { skipped });

    Assert(response.Success, "Sync response should preserve success flag.");
    Assert(response.AppliedCount == 2, "Applied count should be exposed.");
    Assert(response.SkippedCount == 1, "Skipped count should be exposed.");
    Assert(response.Warnings is { Count: 1 }, "Warnings should be exposed.");
    Assert(response.SkippedActions is { Count: 1 }, "Skipped actions should be exposed.");
    var skippedActions = response.SkippedActions ?? throw new InvalidOperationException("Skipped actions were not exposed.");
    Assert(skippedActions[0].Reason.Contains("stale", StringComparison.OrdinalIgnoreCase), "Skipped action reason should be exposed.");
}

static User NewUser(string email)
{
    return new User
    {
        Email = email,
        Name = email.Split('@')[0],
        Salt = "salt",
        PasswordHash = "hash"
    };
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
