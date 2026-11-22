using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.V31;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>#1506 (Task 9): the record's text and document. The same assertions run on the real store over in-memory blobs and on the fake.</summary>
public abstract class RecordBlobStoreContract
{
    private const string User = "user-1";
    private const string Job = "job-1";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private protected abstract IRecordBlobStore Create();

    [Fact]
    public async Task Text_MergesItsTwoParts()
    {
        var store = Create();
        Assert.Null(await store.ReadTextAsync("organisation", User, Job, "node.a", Ct));

        await store.WriteTextAsync("organisation", User, Job, "node.a", "produced", new JsonObject { ["sent"] = "S", ["reply"] = "from produced" }, Ct);
        var onlyProduced = await store.ReadTextAsync("organisation", User, Job, "node.a", Ct);
        Assert.Equal("S", (string?)onlyProduced!["sent"]);

        await store.WriteTextAsync("organisation", User, Job, "node.a", "settled", new JsonObject { ["reply"] = "from settled", ["output"] = "O" }, Ct);

        var merged = await store.ReadTextAsync("organisation", User, Job, "node.a", Ct);
        Assert.Equal("S", (string?)merged!["sent"]);
        Assert.Equal("from settled", (string?)merged["reply"]);
        Assert.Equal("O", (string?)merged["output"]);
        Assert.Null(await store.ReadTextAsync("organisation", User, Job, "node.b", Ct));
        Assert.Null(await store.ReadTextAsync("personal", User, Job, "node.a", Ct)); // the other kind's container
    }

    [Fact]
    public async Task OnlyASettledPart_Reads()
    {
        var store = Create();
        await store.WriteTextAsync(null, User, Job, "node.a", "settled", new JsonObject { ["x"] = 1 }, Ct);

        Assert.Equal(1, (int?)(await store.ReadTextAsync(null, User, Job, "node.a", Ct))!["x"]);
    }

    [Fact]
    public async Task APartThatIsNeitherProducedNorSettled_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Create().WriteTextAsync(null, User, Job, "node.a", "other", new JsonObject(), Ct));
    }

    [Fact]
    public async Task DeleteText_LeavesTheRecord()
    {
        var store = Create();
        await store.WriteTextAsync(null, User, Job, "node.a", "produced", new JsonObject { ["x"] = 1 }, Ct);
        await store.WriteTextAsync(null, User, Job, "node.a", "settled", new JsonObject { ["y"] = 2 }, Ct);
        await store.WriteTextAsync(null, User, "job-2", "node.a", "settled", new JsonObject { ["y"] = 2 }, Ct);
        await store.WriteRecordAsync(null, User, Job, "{\"record\":true}", Ct);

        await store.DeleteTextAsync(null, User, Job, Ct);

        Assert.Null(await store.ReadTextAsync(null, User, Job, "node.a", Ct));
        Assert.Equal("{\"record\":true}", await store.ReadRecordAsync(null, User, Job, Ct));
        Assert.NotNull(await store.ReadTextAsync(null, User, "job-2", "node.a", Ct));
    }

    [Fact]
    public async Task TheRecord_IsWrittenOnce()
    {
        var store = Create();
        Assert.Null(await store.ReadRecordAsync(null, User, Job, Ct));

        Assert.True(await store.WriteRecordAsync(null, User, Job, "{\"first\":1}", Ct));
        Assert.False(await store.WriteRecordAsync(null, User, Job, "{\"second\":2}", Ct));

        Assert.Equal("{\"first\":1}", await store.ReadRecordAsync(null, User, Job, Ct));
    }

    [Fact]
    public async Task TheRecord_IsKeptAsTheExactTextGiven()
    {
        var store = Create();
        var document = "{ \"b\" : 1,\n  \"a\" : \"é\U0001F600\" }\n";

        await store.WriteRecordAsync(null, User, Job, document, Ct);

        Assert.Equal(document, await store.ReadRecordAsync(null, User, Job, Ct));
    }
}

public sealed class BlobRecordStoreTests : RecordBlobStoreContract
{
    private protected override IRecordBlobStore Create() => new BlobRecordStore(new InMemoryRecordBlobs());

    [Fact]
    public async Task TheNames_AreD10sAndTheContainerIsTheKinds()
    {
        var blobs = new InMemoryRecordBlobs();
        var store = new BlobRecordStore(blobs);
        var key = "node.a/b#1";
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

        await store.WriteTextAsync("organisation", "u", "j", key, "produced", new JsonObject(), CancellationToken.None);
        await store.WriteTextAsync(null, "u", "j", key, "settled", new JsonObject(), CancellationToken.None);
        await store.WriteRecordAsync("organisation", "u", "j", "{}", CancellationToken.None);

        Assert.Equal(
            [("org-job-outputs", $"u/j/record.json"), ("org-job-outputs", $"u/j/text/{hex}.produced.json"), ("personal-job-outputs", $"u/j/text/{hex}.settled.json")],
            blobs.Names());
    }

    [Fact]
    public async Task TheRecord_HasNoByteOrderMark()
    {
        var blobs = new InMemoryRecordBlobs();
        await new BlobRecordStore(blobs).WriteRecordAsync(null, "u", "j", "{}", CancellationToken.None);

        Assert.Equal("{}"u8.ToArray(), blobs.BytesOf("personal-job-outputs", "u/j/record.json"));
    }

    [Fact]
    public async Task NothingIsTouched_UntilTheStoreIsUsed()
    {
        var blobs = new InMemoryRecordBlobs();
        _ = new BlobRecordStore(blobs);
        Assert.Equal(0, blobs.Calls);
    }
}

public sealed class FakeRecordBlobStoreTests : RecordBlobStoreContract
{
    private protected override IRecordBlobStore Create() => new FakeRecordBlobStore();
}
