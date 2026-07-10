using Broker.Services;
using BrokerCore.Models;
using Unit.Tests.Helpers;

namespace Unit.Tests.Portal;

/// <summary>
/// Service-level unit tests for <see cref="PortalLineVerificationService"/>,
/// closing the open checkboxes in docs/superpowers/plans/2026-07-01-line-portal-verification.md
/// (Task 1: code generation/verification; Task 2: unverified rejection / wrong code / correct code).
/// Deterministic: temp SQLite via TestDb, no network, no clock mocking needed
/// (expiry is exercised by rewriting the stored expiry timestamp).
/// </summary>
public class PortalLineVerificationTests
{
    private static readonly PortalAuthOptions Options = new() { LineVerificationCodeMinutes = 10 };

    private static PortalUserCredential NewCredential(string userId, bool disabled = false) => new()
    {
        UserId = userId,
        DisplayName = userId,
        Disabled = disabled,
    };

    private static (PortalLineVerificationService Svc, BrokerCore.Data.BrokerDb Db) NewService()
    {
        var db = TestDb.CreateInMemory();
        return (new PortalLineVerificationService(db, Options), db);
    }

    // ── Task 1:code generation / hashing / expiration / binding ──

    [Fact]
    public void IssueCode_ReturnsSixDigitCode_FutureExpiry_AndVerifyCommand()
    {
        var (svc, db) = NewService();
        var cred = NewCredential("alice");
        db.Insert(cred);

        var issue = svc.IssueCode(cred);

        issue.UserId.Should().Be("alice");
        issue.Code.Should().MatchRegex("^[0-9]{6}$");
        issue.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        issue.Command.Should().Be($"/verify alice {issue.Code}");
    }

    [Fact]
    public void IssueCode_StoresSha256Hash_NotTheClearCode()
    {
        var (svc, db) = NewService();
        var cred = NewCredential("bob");
        db.Insert(cred);

        var issue = svc.IssueCode(cred);

        var stored = db.Get<PortalUserCredential>("bob")!;
        stored.LineVerificationCodeHash.Should().NotBeNullOrWhiteSpace();
        stored.LineVerificationCodeHash.Should().NotBe(issue.Code);
        stored.LineVerificationCodeHash.Should().MatchRegex("^[0-9a-f]{64}$");
        stored.LineVerificationCodeExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public void GetStatus_UnknownUser_IsNotVerified()
    {
        var (svc, _) = NewService();

        var status = svc.GetStatus("nobody");

        status.Verified.Should().BeFalse();
        status.UserId.Should().Be("nobody");
    }

    // ── Task 2:unverified rejection(gate 的判定輸入)──

    [Fact]
    public void Resolve_UnverifiedLineUser_ReturnsNull()
    {
        var (svc, db) = NewService();
        db.Insert(NewCredential("carol"));

        svc.ResolvePortalUserIdForLineUser("U-line-carol").Should().BeNull();
    }

    [Fact]
    public void Verify_BeforeAnyCodeIssued_Fails()
    {
        var (svc, db) = NewService();
        db.Insert(NewCredential("dave"));

        var result = svc.Verify("U-line-dave", "dave", "123456");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("line_verification_failed");
    }

    [Fact]
    public void Verify_BlankArguments_FailsWithUsage()
    {
        var (svc, _) = NewService();

        var result = svc.Verify("U-line-x", " ", "");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("/verify");
    }

    [Fact]
    public void Verify_UnknownPortalAccount_Fails()
    {
        var (svc, _) = NewService();

        var result = svc.Verify("U-line-x", "ghost", "123456");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("line_verification_failed");
    }

    [Fact]
    public void Verify_DisabledAccount_Fails()
    {
        var (svc, db) = NewService();
        var cred = NewCredential("mallory", disabled: true);
        db.Insert(cred);
        var issue = svc.IssueCode(cred);

        var result = svc.Verify("U-line-mallory", "mallory", issue.Code);

        result.Success.Should().BeFalse();
    }

    // ── Task 2:wrong-code rejection ──

    [Fact]
    public void Verify_WrongCode_Fails_AndDoesNotBind()
    {
        var (svc, db) = NewService();
        var cred = NewCredential("erin");
        db.Insert(cred);
        var issue = svc.IssueCode(cred);
        var wrong = issue.Code == "000000" ? "000001" : "000000";

        var result = svc.Verify("U-line-erin", "erin", wrong);

        result.Success.Should().BeFalse();
        db.Get<PortalUserCredential>("erin")!.LineVerifiedAt.Should().BeNull();
        svc.ResolvePortalUserIdForLineUser("U-line-erin").Should().BeNull();
    }

    [Fact]
    public void Verify_ExpiredCode_Fails()
    {
        var (svc, db) = NewService();
        var cred = NewCredential("frank");
        db.Insert(cred);
        var issue = svc.IssueCode(cred);
        var stored = db.Get<PortalUserCredential>("frank")!;
        stored.LineVerificationCodeExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        db.Update(stored);

        var result = svc.Verify("U-line-frank", "frank", issue.Code);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("expired");
    }

    // ── Task 2:correct-code success + binding semantics ──

    [Fact]
    public void Verify_CorrectCode_Succeeds_BindsLine_ClearsCode_AndResolves()
    {
        var (svc, db) = NewService();
        var cred = NewCredential("grace");
        db.Insert(cred);
        var issue = svc.IssueCode(cred);

        var result = svc.Verify("U-line-grace", "grace", issue.Code);

        result.Success.Should().BeTrue();
        result.EffectiveUserId.Should().Be("grace");

        var stored = db.Get<PortalUserCredential>("grace")!;
        stored.LineUserId.Should().Be("U-line-grace");
        stored.LineVerifiedAt.Should().NotBeNull();
        stored.LineVerificationCodeHash.Should().BeEmpty();
        stored.LineVerificationCodeExpiresAt.Should().BeNull();

        svc.GetStatus("grace").Verified.Should().BeTrue();
        svc.ResolvePortalUserIdForLineUser("U-line-grace").Should().Be("grace");
    }

    [Fact]
    public void Verify_AlreadyVerifiedPair_IsIdempotentOk()
    {
        var (svc, db) = NewService();
        var cred = NewCredential("heidi");
        db.Insert(cred);
        var issue = svc.IssueCode(cred);
        svc.Verify("U-line-heidi", "heidi", issue.Code).Success.Should().BeTrue();

        // 同一 (LINE, Portal) 再驗證 — 即使碼已清掉也應回 Ok(冪等)。
        var again = svc.Verify("U-line-heidi", "heidi", "999999");

        again.Success.Should().BeTrue();
        again.EffectiveUserId.Should().Be("heidi");
    }

    [Fact]
    public void Verify_LineAlreadyLinkedToAnotherAccount_Fails()
    {
        var (svc, db) = NewService();
        var a = NewCredential("ivy");
        var b = NewCredential("judy");
        db.Insert(a);
        db.Insert(b);
        var issueA = svc.IssueCode(a);
        svc.Verify("U-line-shared", "ivy", issueA.Code).Success.Should().BeTrue();
        var issueB = svc.IssueCode(b);

        var result = svc.Verify("U-line-shared", "judy", issueB.Code);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("another Portal account");
    }

    [Fact]
    public void Verify_AccountAlreadyLinkedToAnotherLine_Fails()
    {
        var (svc, db) = NewService();
        var cred = NewCredential("kate");
        db.Insert(cred);
        var issue1 = svc.IssueCode(cred);
        svc.Verify("U-line-kate-1", "kate", issue1.Code).Success.Should().BeTrue();
        var issue2 = svc.IssueCode(db.Get<PortalUserCredential>("kate")!);

        var result = svc.Verify("U-line-kate-2", "kate", issue2.Code);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("another LINE account");
    }
}
