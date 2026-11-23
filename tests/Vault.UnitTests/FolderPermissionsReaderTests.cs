using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Vault.Core.Shares;
using Xunit;

namespace Vault.UnitTests;

public sealed class FolderPermissionsReaderTests
{
    private const string Folders = """{"total":2,"data":[{"uuid":"u1","name":"media"},{"uuid":"u2","name":"photos"}]}""";

    private static FolderPermissionsReader Reader(FakeRpcClient omv) => new(omv, NullLogger<FolderPermissionsReader>.Instance);

    /// <summary>A double that answers the folder lookup and then the privileges with the given JSON.</summary>
    private static FakeRpcClient Omv(string privileges, string folders = Folders) => new()
    {
        Handler = call => call == OmvRpcCall.SharedFolders ? FakeRpcClient.Json(folders) : FakeRpcClient.Json(privileges),
    };

    [Fact]
    [Trait("Criteria", "UC-10.AC1, UC-07.AC1")]
    public async Task Each_privilege_is_mapped_and_only_the_two_ShareMgmt_calls_are_made()
    {
        var omv = Omv("""[{"type":"group","name":"family","perms":7},{"type":"user","name":"kid","perms":"5"},{"type":"user","name":"guest","perms":0}]""");

        var result = await Reader(omv).ReadAsync("media");

        Assert.True(result.Ok);
        Assert.Equal("media", result.Folder);
        Assert.Equal(
            [new Privilege("group", "family", "read_write"), new Privilege("user", "kid", "read_only"), new Privilege("user", "guest", "deny")],
            result.Privileges);
        Assert.Null(result.Message);
        Assert.Equal([OmvRpcCall.SharedFolders, OmvRpcCall.SharedFolderPrivileges], omv.Calls.Select(c => c.Call));
        Assert.Equal("u1", omv.Calls[1].Parameters.GetProperty("uuid").GetString());
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC1")]
    public async Task Users_and_groups_with_null_perms_have_no_explicit_privilege_and_are_not_reported()
    {
        // What a real OMV 8.5.12 sends: every user and group, perms null unless one is set.
        var omv = Omv("""[{"type":"user","id":1000,"name":"ana","perms":null},{"type":"user","id":1001,"name":"bob"},{"type":"group","id":1000,"name":"admins","perms":7}]""");

        var result = await Reader(omv).ReadAsync("media");

        Assert.Equal([new Privilege("group", "admins", "read_write")], result.Privileges);
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC2")]
    public async Task A_folder_with_no_privilege_is_an_empty_result_with_a_message_not_an_error()
    {
        var result = await Reader(Omv("""[{"type":"user","name":"ana","perms":null}]""")).ReadAsync("photos");

        Assert.True(result.Ok);
        Assert.Empty(result.Privileges);
        Assert.Contains("no explicit privilege", result.Message, StringComparison.Ordinal);
        Assert.Contains("default", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Media")]
    [InlineData("nope")]
    [Trait("Criteria", "UC-11.AC1")]
    public async Task A_folder_OMV_does_not_have_is_not_found_and_the_message_names_it(string folder)
    {
        var omv = Omv("[]");

        var result = await Reader(omv).ReadAsync(folder);

        Assert.Equal("not_found", result.Error!.Code);
        Assert.Contains($"'{folder}'", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("list_shared_folders", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(omv.Calls, c => c.Call == OmvRpcCall.SharedFolderPrivileges);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Criteria", "UC-12.AC1")]
    public async Task A_blank_folder_is_rejected_before_OMV_is_called(string? folder)
    {
        var omv = Omv("[]");

        var result = await Reader(omv).ReadAsync(folder);

        Assert.Equal("invalid_arguments", result.Error!.Code);
        Assert.Contains("folder", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(omv.Calls);
    }

    [Theory]
    [InlineData("""{"type":"other","name":"x","perms":7}""")]
    [InlineData("""{"type":"user","perms":7}""")]
    [InlineData("""{"type":"user","name":"x","perms":3}""")]
    [InlineData("""{"type":"user","name":"x","perms":"read"}""")]
    [Trait("Criteria", "UC-02.AC4")]
    public async Task A_privilege_Vault_cannot_read_is_a_bad_response_not_a_hidden_line(string entry)
    {
        var result = await Reader(Omv($"[{entry}]")).ReadAsync("media");

        Assert.Equal("omv_bad_response", result.Error!.Code);
        Assert.Empty(result.Privileges);
    }

    [Theory]
    [InlineData("""{"nope":1}""", Folders)]
    [InlineData("""{"nope":1}""", """{"total":0}""")]
    [InlineData("[]", """{"total":1,"data":[{"name":"media"}]}""")]
    [Trait("Criteria", "UC-02.AC4")]
    public async Task An_answer_of_the_wrong_shape_is_a_bad_response(string privileges, string folders)
    {
        var result = await Reader(Omv(privileges, folders)).ReadAsync("media");

        Assert.Equal("omv_bad_response", result.Error!.Code);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC1, UC-02.AC5, UC-02.AC6")]
    public async Task OMV_failures_and_unexpected_failures_become_errors_without_their_cause()
    {
        var unreachable = await Reader(FakeRpcClient.Failing(new OmvRpcException(OmvRpcErrorKind.Unreachable, "down"))).ReadAsync("media");
        var unexpected = await Reader(FakeRpcClient.Failing(new InvalidOperationException("boom"))).ReadAsync("media");

        Assert.Equal("omv_unreachable", unreachable.Error!.Code);
        Assert.Equal("failed", unexpected.Error!.Code);
        Assert.DoesNotContain("boom", unexpected.Error.Message, StringComparison.Ordinal);
    }
}
