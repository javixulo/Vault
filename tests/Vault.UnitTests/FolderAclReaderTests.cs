using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Vault.Core.Shares;
using Xunit;

namespace Vault.UnitTests;

public sealed class FolderAclReaderTests
{
    private const string Folders = """{"total":2,"data":[{"uuid":"u1","name":"media"},{"uuid":"u2","name":"photos"}]}""";

    // The shape a real OMV 8.5.12 sends: every user and group of the NAS, perms null when there is no entry.
    private const string Acl = """
        {"file":"srv/dev-disk-by-uuid-x/media/","owner":"root","group":"users","acl":{"user":7,"group":5,"other":0,
        "users":[{"name":"root","perms":null,"uid":0,"system":true},{"name":"kid","perms":"5","uid":1001,"system":false}],
        "groups":[{"name":"family","perms":7,"gid":1000,"system":false},{"name":"admins","perms":null,"gid":1001,"system":false},{"name":"locked","perms":0,"gid":1002,"system":false}]}}
        """;

    private static FolderAclReader Reader(FakeRpcClient omv) => new(omv, NullLogger<FolderAclReader>.Instance);

    private static FakeRpcClient Omv(string acl, string folders = Folders) => new()
    {
        Handler = call => FakeRpcClient.Json(call == OmvRpcCall.SharedFolders ? folders : call == OmvRpcCall.FolderBrowser ? "[]" : acl),
    };

    /// <summary>An OMV whose <c>FolderBrowser.get</c> answers by path, and an empty list for any other path, as the real one does.</summary>
    private static FakeRpcClient Tree(params (string Path, string Names)[] tree)
    {
        var omv = new FakeRpcClient();
        omv.Handler = call => FakeRpcClient.Json(
            call == OmvRpcCall.SharedFolders ? Folders
            : call == OmvRpcCall.SharedFolderAcl ? Acl
            : tree.FirstOrDefault(t => t.Path == omv.Calls[^1].Parameters.GetProperty("path").GetString()).Names ?? "[]");
        return omv;
    }

    [Fact]
    [Trait("Criteria", "UC-27.AC1, UC-27.AC3, UC-07.AC1")]
    public async Task A_subfolder_is_checked_in_its_parent_and_then_read_with_its_own_subfolders()
    {
        var omv = Tree(("Manuales/", """["Timbre","Domótica"]"""), ("Manuales/Domótica/", """["Sensores"]"""));

        var result = await Reader(omv).ReadAsync("media", "Manuales/Domótica", includeSubfolders: true);

        Assert.True(result.Ok);
        Assert.Equal("Manuales/Domótica", result.RelativePath);
        Assert.Equal(["Sensores"], result.Subfolders);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal([OmvRpcCall.SharedFolders, OmvRpcCall.FolderBrowser, OmvRpcCall.SharedFolderAcl, OmvRpcCall.FolderBrowser], omv.Calls.Select(c => c.Call));
        Assert.Equal("Manuales/", omv.Calls[1].Parameters.GetProperty("path").GetString());
        Assert.Equal("sharedfolder", omv.Calls[1].Parameters.GetProperty("type").GetString());
        Assert.Equal("u1", omv.Calls[1].Parameters.GetProperty("uuid").GetString());
        Assert.Equal("Manuales/Domótica/", omv.Calls[2].Parameters.GetProperty("file").GetString());
        Assert.Equal("Manuales/Domótica/", omv.Calls[3].Parameters.GetProperty("path").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Manuales")]
    [Trait("Criteria", "UC-27.AC5")]
    public async Task Without_include_subfolders_the_folder_read_is_not_listed(string? asked)
    {
        var omv = Tree(("/", """["Manuales"]"""));

        var result = await Reader(omv).ReadAsync("media", asked);

        Assert.True(result.Ok);
        Assert.Null(result.Subfolders);
        Assert.Equal(asked is null ? 0 : 1, omv.Calls.Count(c => c.Call == OmvRpcCall.FolderBrowser));
    }

    [Theory]
    [InlineData("Manuales", "Manuales")]
    [InlineData("/Manuales/", "Manuales")]
    [InlineData("/", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    [Trait("Criteria", "UC-27.AC2")]
    public async Task Slashes_at_the_ends_are_ignored_and_nothing_means_the_root(string? asked, string? expected)
    {
        var result = await Reader(Tree(("/", """["Manuales"]"""))).ReadAsync("media", asked);

        Assert.True(result.Ok);
        Assert.Equal(expected, result.RelativePath);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("manuales")]
    [InlineData("Manuales/nope")]
    [InlineData("nope/Timbre")]
    [Trait("Criteria", "UC-28.AC1")]
    public async Task A_subfolder_its_parent_does_not_list_is_not_found_and_its_ACL_is_not_asked_for(string asked)
    {
        var omv = Tree(("/", """["Manuales"]"""), ("Manuales/", """["Timbre"]"""));

        var result = await Reader(omv).ReadAsync("media", asked);

        Assert.Equal("not_found", result.Error!.Code);
        Assert.Contains($"'{asked}'", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("subfolders", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(omv.Calls, c => c.Call == OmvRpcCall.SharedFolderAcl);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("Manuales/../..")]
    [InlineData("./Manuales")]
    [InlineData("Manuales//Timbre")]
    [InlineData("Manuales\n")]
    [InlineData("Manuales\0")]
    [Trait("Criteria", "UC-22.AC2")]
    public async Task A_relative_path_that_is_not_valid_is_rejected_before_OMV_is_called(string asked)
    {
        var omv = Tree();

        var result = await Reader(omv).ReadAsync("media", asked);

        Assert.Equal("invalid_arguments", result.Error!.Code);
        Assert.Contains("relative_path", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(omv.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("""[""]""")]
    [Trait("Criteria", "UC-27.AC4")]
    public async Task A_list_of_subfolders_Vault_cannot_read_is_a_bad_response(string names)
    {
        var result = await Reader(Tree(("/", names))).ReadAsync("media", includeSubfolders: true);

        Assert.Equal("omv_bad_response", result.Error!.Code);
        Assert.Empty(result.Entries);
        Assert.Null(result.Subfolders);
    }

    [Fact]
    [Trait("Criteria", "UC-20.AC1, UC-07.AC1")]
    public async Task The_base_permissions_and_each_named_entry_are_mapped_and_only_the_root_is_asked_for()
    {
        var omv = Omv(Acl);

        var result = await Reader(omv).ReadAsync("media");

        Assert.True(result.Ok);
        Assert.Equal("media", result.Folder);
        Assert.Equal(("root", "users"), (result.Owner, result.Group));
        Assert.Equal(("rwx", "r-x", "---"), (result.OwnerPermissions, result.GroupPermissions, result.OtherPermissions));
        Assert.Equal([new AclEntry("user", "kid", "r-x"), new AclEntry("group", "family", "rwx"), new AclEntry("group", "locked", "---")], result.Entries);
        Assert.Null(result.Message);
        Assert.Null(result.RelativePath);
        Assert.Null(result.Subfolders);
        Assert.Equal([OmvRpcCall.SharedFolders, OmvRpcCall.SharedFolderAcl], omv.Calls.Select(c => c.Call));
        Assert.Equal("u1", omv.Calls[1].Parameters.GetProperty("uuid").GetString());
        Assert.Equal("/", omv.Calls[1].Parameters.GetProperty("file").GetString());
    }

    [Fact]
    [Trait("Criteria", "UC-20.AC1, UC-02.AC6")]
    public async Task The_path_OMV_reports_is_not_in_the_result()
    {
        var result = await Reader(Omv(Acl)).ReadAsync("media");

        Assert.DoesNotContain("dev-disk-by-uuid", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-20.AC2")]
    public async Task A_folder_with_no_named_entry_still_gives_the_base_permissions_and_a_message()
    {
        var omv = Omv("""{"owner":"root","group":"users","acl":{"user":7,"group":0,"other":0,"users":[{"name":"root","perms":null}],"groups":[{"name":"users","perms":null}]}}""");

        var result = await Reader(omv).ReadAsync("photos");

        Assert.True(result.Ok);
        Assert.Empty(result.Entries);
        Assert.Equal("rwx", result.OwnerPermissions);
        Assert.Contains("no extended ACL entry", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Media")]
    [InlineData("nope")]
    [Trait("Criteria", "UC-21.AC1")]
    public async Task A_folder_OMV_does_not_have_is_not_found_and_the_message_names_it(string folder)
    {
        var omv = Omv("{}");

        var result = await Reader(omv).ReadAsync(folder);

        Assert.Equal("not_found", result.Error!.Code);
        Assert.Contains($"'{folder}'", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("list_shared_folders", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(omv.Calls, c => c.Call == OmvRpcCall.SharedFolderAcl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Criteria", "UC-22.AC1")]
    public async Task A_blank_folder_is_rejected_before_OMV_is_called(string? folder)
    {
        var omv = Omv("{}");

        var result = await Reader(omv).ReadAsync(folder);

        Assert.Equal("invalid_arguments", result.Error!.Code);
        Assert.Contains("folder", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(omv.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"owner":"root"}""")]
    [InlineData("""{"acl":{"user":7,"group":5,"other":0,"users":[]}}""")]
    [InlineData("""{"acl":{"user":7,"group":5,"users":[],"groups":[]}}""")]
    [InlineData("""{"acl":{"user":9,"group":5,"other":0,"users":[],"groups":[]}}""")]
    [InlineData("""{"acl":{"user":7,"group":5,"other":0,"users":[{"perms":7}],"groups":[]}}""")]
    [InlineData("""{"acl":{"user":7,"group":5,"other":0,"users":[{"name":"x","perms":8}],"groups":[]}}""")]
    [InlineData("""{"acl":{"user":7,"group":5,"other":0,"users":[{"name":"x","perms":"rw"}],"groups":[]}}""")]
    [Trait("Criteria", "UC-20.AC3, UC-02.AC4")]
    public async Task An_ACL_Vault_cannot_read_is_a_bad_response_not_a_hidden_line(string acl)
    {
        var result = await Reader(Omv(acl)).ReadAsync("media");

        Assert.Equal("omv_bad_response", result.Error!.Code);
        Assert.Empty(result.Entries);
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

    [Theory]
    [InlineData(0, "---")]
    [InlineData(1, "--x")]
    [InlineData(5, "r-x")]
    [InlineData(6, "rw-")]
    [InlineData(7, "rwx")]
    [InlineData(8, null)]
    [InlineData(-1, null)]
    [Trait("Criteria", "UC-20.AC1")]
    public void The_POSIX_bits_are_written_as_rwx(long bits, string? expected) =>
        Assert.Equal(expected, FolderAclReader.Format(bits));
}
