using Azure.Core;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>#1506 (Task 9; Review Focus 1 and 2): the record's rows. The same assertions run on the real store over an in-memory table and on the fake.</summary>
public abstract class RecordRowStoreContract
{
    private const string Job = "job-1";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private protected abstract IRecordRowStore Create();

    private static RecordRow Row(string key, string json = "{\"a\":1}", string section = "node") => new(section, key, json);

    [Fact]
    public async Task ARow_IsWrittenOnce()
    {
        var store = Create();
        var first = Row("node.a:1");

        Assert.Equal(RecordWrite.Written, Assert.Single(await store.WriteAsync(Job, [first], Ct)).Result);
        Assert.Equal(RecordWrite.AlreadyThere, Assert.Single(await store.WriteAsync(Job, [Row("node.a:1")], Ct)).Result);

        var results = await store.WriteAsync(Job, [Row("node.a:1", "{\"a\":2}"), Row("node.b:1")], Ct);
        Assert.Equal([("node.a:1", RecordWrite.Conflict), ("node.b:1", RecordWrite.Written)], results);
        Assert.Equal(first, await store.ReadAsync(Job, "node.a:1", Ct));
    }

    [Fact]
    public async Task AKeyWithASlashAndAHash_RoundTrips()
    {
        var store = Create();
        var row = Row("node.a.b#2:3/1/x");
        var other = Row("node.a.b!232:3!2F1!2Fx"); // what the escape would give: a different key must not share the row

        await store.WriteAsync(Job, [row, other], Ct);

        Assert.Equal(row, await store.ReadAsync(Job, row.Key, Ct));
        Assert.Equal(other, await store.ReadAsync(Job, other.Key, Ct));
        Assert.Equal(2, (await store.ListAsync(Job, Ct)).Count);
    }

    [Fact]
    public async Task ALongKey_Stores()
    {
        var store = Create();
        var a = Row("deliverable.x:" + new string('a', 3000));
        var b = Row("deliverable.x:" + new string('a', 2999) + "b");

        Assert.All(await store.WriteAsync(Job, [a, b], Ct), r => Assert.Equal(RecordWrite.Written, r.Result));

        Assert.Equal(a, await store.ReadAsync(Job, a.Key, Ct));
        Assert.Equal(b, await store.ReadAsync(Job, b.Key, Ct));
    }

    [Fact]
    public async Task ALongRow_IsSplitAndRejoined()
    {
        var store = Create();
        // 70,000 characters, an emoji straddling the first cut so a naive split would break the pair
        var text = new string('x', 29_999) + "\U0001F600" + new string('y', 40_000);
        var json = "{\"t\":\"" + text + "\"}";
        var row = Row("node.long", json);

        await store.WriteAsync(Job, [row], Ct);

        Assert.True(json.Length > 70_000);
        Assert.Equal(row, await store.ReadAsync(Job, "node.long", Ct));
        Assert.Equal(RecordWrite.AlreadyThere, Assert.Single(await store.WriteAsync(Job, [row], Ct)).Result);
    }

    [Fact]
    public async Task ARowOver1MiB_IsAnEngineFault_NamingTheKeyAndSizeButNotTheContent()
    {
        var store = Create();
        var json = "{\"secret\":\"" + new string('z', 600_000) + "\"}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(Job, [Row("node.ok"), Row("node.huge", json)], Ct));

        Assert.Contains("node.huge", ex.Message);
        Assert.Contains(json.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message);
        Assert.DoesNotContain("zzzz", ex.Message);
        Assert.Null(await store.ReadAsync(Job, "node.ok", Ct)); // nothing was written
    }

    [Fact]
    public async Task List_GivesEveryRowOfTheJob_AndNoOthers()
    {
        var store = Create();
        await store.WriteAsync(Job, [Row("node.b"), Row("check.a", "{}", "check")], Ct);
        await store.WriteAsync("job-2", [Row("node.other")], Ct);

        var rows = await store.ListAsync(Job, Ct);

        Assert.Equal(["check.a", "node.b"], rows.Select(r => r.Key));
        Assert.Equal("check", rows[0].Section);
        Assert.Empty(await store.ListAsync("job-3", Ct));
        Assert.Null(await store.ReadAsync("job-3", "node.b", Ct));
    }

    [Fact]
    public async Task DeleteJob_RemovesThePartition()
    {
        var store = Create();
        await store.WriteAsync(Job, Enumerable.Range(0, 250).Select(i => Row($"node.n{i:D3}")).ToList(), Ct);
        await store.WriteAsync("job-2", [Row("node.other")], Ct);

        await store.DeleteJobAsync(Job, Ct);

        Assert.Empty(await store.ListAsync(Job, Ct));
        Assert.Single(await store.ListAsync("job-2", Ct));
    }

    [Fact]
    public async Task AWriteOfNothing_ResultsInNothing()
    {
        Assert.Empty(await Create().WriteAsync(Job, [], Ct));
    }
}

public sealed class TableRecordRowStoreTests : RecordRowStoreContract
{
    private protected override IRecordRowStore Create() => new TableRecordRowStore(new InMemoryRecordTable(), NullLogger.Instance);

    [Fact]
    public async Task NothingIsCreated_UntilA31JobWrites()
    {
        var table = new InMemoryRecordTable();
        var store = new TableRecordRowStore(table, NullLogger.Instance);

        Assert.Null(await store.ReadAsync("job-1", "node.a", CancellationToken.None));
        Assert.Empty(await store.ListAsync("job-1", CancellationToken.None));
        await store.DeleteJobAsync("job-1", CancellationToken.None);
        Assert.False(table.Created);

        await store.WriteAsync("job-1", [new RecordRow("node", "node.a", "{}")], CancellationToken.None);
        await store.WriteAsync("job-1", [new RecordRow("node", "node.b", "{}")], CancellationToken.None);
        Assert.Equal(1, table.Creates);
    }

    [Fact]
    public void TheRealConstructors_TouchNoStorage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true" })
            .Build();
        var credential = new NoCredential();

        _ = new TableRecordRowStore(configuration, credential, NullLogger<TableRecordRowStore>.Instance);
        _ = new BlobRecordStore(configuration, credential, NullLogger<BlobRecordStore>.Instance);
    }

    [Fact]
    public async Task DeleteJob_GoesInBatchesOfAtMost100_AndEveryRowGoes()
    {
        var table = new InMemoryRecordTable();
        var store = new TableRecordRowStore(table, NullLogger.Instance);
        await store.WriteAsync("job-1", Enumerable.Range(0, 250).Select(i => new RecordRow("node", $"node.n{i:D3}", "{}")).ToList(), CancellationToken.None);
        await store.WriteAsync("job-2", [new RecordRow("node", "node.other", "{}")], CancellationToken.None);

        await store.DeleteJobAsync("job-1", CancellationToken.None);

        Assert.Equal([100, 100, 50], table.DeleteBatches);
        Assert.Empty(await store.ListAsync("job-1", CancellationToken.None));
        Assert.Single(await store.ListAsync("job-2", CancellationToken.None));
    }

    [Fact]
    public async Task TheStoredEntity_HasTheNamedProperties_AndAHashOfTheRowsJson()
    {
        var table = new InMemoryRecordTable();
        var store = new TableRecordRowStore(table, NullLogger.Instance);
        var row = new RecordRow("decision", "node.a/b#1", "{\"r\":true}");

        await store.WriteAsync("job-1", [row], CancellationToken.None);

        var entity = Assert.Single(table.All());
        Assert.Equal("job-1", entity.PartitionKey);
        Assert.Equal(RecordKeys.Escape(row.Key), entity.RowKey);
        Assert.Equal(row.Key, entity.GetString("key"));
        Assert.Equal("decision", entity.GetString("section"));
        Assert.Equal(row.Json, entity.GetString("row"));
        Assert.Equal(CanonicalJson.HashText(row.Json), entity.GetString("hash"));
    }

    [Fact]
    public async Task ALongRow_IsCutAt30000Characters_AcrossRowRow1Row2()
    {
        var table = new InMemoryRecordTable();
        var store = new TableRecordRowStore(table, NullLogger.Instance);

        await store.WriteAsync("job-1", [new RecordRow("node", "node.long", new string('q', 70_000))], CancellationToken.None);

        var entity = Assert.Single(table.All());
        Assert.Equal(30_000, entity.GetString("row").Length);
        Assert.Equal(30_000, entity.GetString("row1").Length);
        Assert.Equal(10_000, entity.GetString("row2").Length);
        Assert.Null(entity.GetString("row3"));
    }

    [Fact]
    public async Task AConflict_IsLogged_WithTheKeyAndHashesOnly_AndLeavesTheStoredRow()
    {
        var logger = new CapturingLogger();
        var store = new TableRecordRowStore(new InMemoryRecordTable(), logger);
        await store.WriteAsync("job-1", [new RecordRow("node", "node.a", "{\"v\":\"FIRST-SECRET\"}")], CancellationToken.None);

        await store.WriteAsync("job-1", [new RecordRow("node", "node.a", "{\"v\":\"SECOND-SECRET\"}")], CancellationToken.None);

        var line = Assert.Single(logger.Lines);
        Assert.Contains("V31 record conflict", line);
        Assert.Contains("node.a", line);
        Assert.Contains("sha256:", line);
        Assert.DoesNotContain("SECRET", line);
        Assert.Contains("FIRST-SECRET", (await store.ReadAsync("job-1", "node.a", CancellationToken.None))!.Json);
    }

    private sealed class NoCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw new InvalidOperationException("touched");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw new InvalidOperationException("touched");
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}

public sealed class FakeRecordRowStoreTests : RecordRowStoreContract
{
    private protected override IRecordRowStore Create() => new FakeRecordRowStore();
}
