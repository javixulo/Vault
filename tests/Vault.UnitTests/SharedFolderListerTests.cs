using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Vault.Core.Shares;
using Xunit;

namespace Vault.UnitTests;

public sealed class SharedFolderListerTests
{
    private const string ThreeFolders = """
        {"total":3,"data":[
          {"uuid":"u1","name":"media","reldirpath":"media/","comment":"Movies"},
          {"uuid":"u2","name":"photos","reldirpath":"photos/","description":"Library"},
          {"uuid":"u3","name":"backups","reldirpath":"backups/"}]}
        """;

    private static SharedFolderLister Lister(FakeRpcClient omv) => new(omv, NullLogger<SharedFolderLister>.Instance);

    [Fact]
    [Trait("Criteria", "UC-03.AC1, UC-07.AC1")]
    public async Task Without_arguments_it_asks_for_the_first_page_and_calls_only_ShareMgmt_getList()
    {
        var omv = FakeRpcClient.Answering(ThreeFolders);

        var result = await Lister(omv).ListAsync(null, null);

        Assert.True(result.Ok);
        Assert.Equal(["media", "photos", "backups"], result.Folders.Select(f => f.Name));
        Assert.Equal(3, result.Total);
        Assert.False(result.HasMore);
        Assert.Null(result.Message);
        var call = Assert.Single(omv.Calls);
        Assert.Equal(OmvRpcCall.SharedFolders, call.Call);
        Assert.Equal(0, call.Parameters.GetProperty("start").GetInt32());
        Assert.Equal(SharedFolderLister.DefaultLimit, call.Parameters.GetProperty("limit").GetInt32());
    }

    [Fact]
    [Trait("Criteria", "UC-03.AC2")]
    public async Task Limit_and_offset_are_passed_to_OMV_and_each_folder_is_mapped()
    {
        var omv = FakeRpcClient.Answering(ThreeFolders);

        var result = await Lister(omv).ListAsync(2, 1);

        var parameters = Assert.Single(omv.Calls).Parameters;
        Assert.Equal(1, parameters.GetProperty("start").GetInt32());
        Assert.Equal(2, parameters.GetProperty("limit").GetInt32());
        Assert.Equal(new SharedFolder("media", "u1", "media/", "Movies"), result.Folders[0]);
        Assert.Equal("Library", result.Folders[1].Description);
        Assert.Null(result.Folders[2].Description);
    }

    [Theory]
    [InlineData(0, 2, 5, true)]
    [InlineData(3, 2, 5, false)]
    [InlineData(0, 5, 5, false)]
    [Trait("Criteria", "UC-03.AC3")]
    public async Task HasMore_tells_whether_another_page_exists(int offset, int returned, int total, bool hasMore)
    {
        var entries = string.Join(",", Enumerable.Range(0, returned).Select(i => $$"""{"name":"f{{i}}"}"""));
        var omv = FakeRpcClient.Answering($$"""{"total":{{total}},"data":[{{entries}}]}""");

        var result = await Lister(omv).ListAsync(null, offset);

        Assert.Equal(hasMore, result.HasMore);
        Assert.Equal(total, result.Total);
    }

    [Fact]
    [Trait("Criteria", "UC-03.AC4")]
    public async Task An_entry_without_a_name_is_left_out()
    {
        var omv = FakeRpcClient.Answering("""{"total":2,"data":[{"uuid":"x"},{"name":"media"}]}""");

        var result = await Lister(omv).ListAsync(null, null);

        Assert.Equal(["media"], result.Folders.Select(f => f.Name));
    }

    [Fact]
    [Trait("Criteria", "UC-04.AC1")]
    public async Task No_folders_is_an_empty_result_with_a_message_not_an_error()
    {
        var omv = FakeRpcClient.Answering("""{"total":0,"data":[]}""");

        var result = await Lister(omv).ListAsync(null, null);

        Assert.True(result.Ok);
        Assert.Empty(result.Folders);
        Assert.Contains("no shared folders", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-04.AC2")]
    public async Task An_offset_past_the_end_is_an_empty_result_that_says_how_many_there_are()
    {
        var omv = FakeRpcClient.Answering("""{"total":3,"data":[]}""");

        var result = await Lister(omv).ListAsync(null, 10);

        Assert.True(result.Ok);
        Assert.Equal(3, result.Total);
        Assert.Contains("past the end", result.Message, StringComparison.Ordinal);
        Assert.Contains("3", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(201, null)]
    [InlineData(null, -1)]
    [InlineData(0, -1)]
    [Trait("Criteria", "UC-05.AC1, UC-05.AC2, UC-05.AC3, UC-05.AC4")]
    public async Task Invalid_arguments_are_rejected_before_OMV_is_called(int? limit, int? offset)
    {
        var omv = FakeRpcClient.Answering(ThreeFolders);

        var result = await Lister(omv).ListAsync(limit, offset);

        Assert.False(result.Ok);
        Assert.Equal("invalid_arguments", result.Error!.Code);
        Assert.Empty(omv.Calls);
        Assert.Empty(result.Folders);
        // Only the first broken rule is told, limit before offset.
        Assert.Contains(limit is null or (>= 1 and <= 200) ? "offset" : "limit", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"total":1}""")]
    [InlineData("""{"data":"nope"}""")]
    [InlineData("[]")]
    [Trait("Criteria", "UC-02.AC4")]
    public async Task An_answer_without_a_data_array_is_a_bad_response(string answer)
    {
        var omv = FakeRpcClient.Answering(answer);

        var result = await Lister(omv).ListAsync(null, null);

        Assert.Equal("omv_bad_response", result.Error!.Code);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC1, UC-02.AC5")]
    public async Task OMV_failures_and_unexpected_failures_become_errors()
    {
        var unreachable = await Lister(FakeRpcClient.Failing(new OmvRpcException(OmvRpcErrorKind.Unreachable, "down"))).ListAsync(null, null);
        var unexpected = await Lister(FakeRpcClient.Failing(new InvalidOperationException("boom"))).ListAsync(null, null);

        Assert.Equal("omv_unreachable", unreachable.Error!.Code);
        Assert.Equal("failed", unexpected.Error!.Code);
        Assert.DoesNotContain("boom", unexpected.Error.Message, StringComparison.Ordinal);
    }
}
