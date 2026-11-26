using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1508 (area 11, Task 29; ruled by the owner, comment 5 item 6 of 2026-11-25; R18; part 10 §§ 1.4, 1.5, 1.9, 1.10):
/// the migration's choices live in a file. Each package's <c>options.json</c>, beside its <c>hand-edits/</c>, is
/// <c>{ "renames", "whenEmpty", "folderFormats" }</c>, every member required (<c>{}</c> for none) and no other;
/// <see cref="Migration31Options.FromJson"/> reads it, and refuses what it cannot read with a sentence (A10-070 to
/// A10-072), never an exception. The tests (<see cref="ExampleChoices"/>) and the operator read the same files.
/// </summary>
public class MigrationOptionsTests
{
    private static (Migration31Options? Options, string? Refusal) Read(string json) =>
        Migration31Options.FromJson(JsonNode.Parse(json)!.AsObject(), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, TestFormats.Catalog);

    [Fact]
    public void ExampleChoices_ReadTheFiles()
    {
        // No choice is held in code: ExampleChoices reads hand-edits/<package>/options.json, and holds no dictionary of its own.
        var source = File.ReadAllText(Path.Combine(V31Contract.RepoRoot(), "tests", "V31Migration", "ExampleChoices.cs"), Encoding.UTF8);
        Assert.DoesNotContain("new Dictionary", source, StringComparison.Ordinal);
        Assert.DoesNotContain("] = \"", source, StringComparison.Ordinal);

        // Every package has its file, in R18's shape, and MigrationFixture.Options gives what it says.
        foreach (var package in MigratedPackagesTests.Names)
        {
            var json = ExampleChoices.Json(package);
            Assert.Equal(["renames", "whenEmpty", "folderFormats"], json.Select(p => p.Key));
            var options = MigrationFixture.Options(package);
            Assert.True(JsonNode.DeepEquals(json, options.ToJson()), $"{package}: {options.ToJson().ToJsonString()}");
        }

        // The hand choices part 10 §§ 1.9 and 1.10 and part 01 § 3 make.
        Assert.Equal(new Dictionary<string, string> { ["consult"] = "hold" }, MigrationFixture.Options("test/general").WhenEmpty);
        Assert.Equal(new Dictionary<string, string> { ["standards"] = "standard_section" }, MigrationFixture.Options("test/general").FolderFormats);
        Assert.Empty(MigrationFixture.Options("test/general").Renames!);
        Assert.Equal(new Dictionary<string, string> { ["parts"] = "sections" }, MigrationFixture.Options("example-nested-note").Renames);
        Assert.Equal(new Dictionary<string, string> { ["note"] = "fail" }, MigrationFixture.Options("example-nested-note").WhenEmpty);
        Assert.Empty(MigrationFixture.Options("example-nested-note").FolderFormats!);
        Assert.Empty(MigrationFixture.Options("example-two-documents").WhenEmpty!);
    }

    [Fact]
    public void APackageWithNoOptionsFile_FailsLoudly()
    {
        var failure = Assert.ThrowsAny<Exception>(() => MigrationFixture.Options("no-such-package"));
        Assert.Contains("no-such-package/options.json", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFile_IsRead_AndWrittenBack()
    {
        var (options, refusal) = Read("""{ "renames": { "parts": "sections" }, "whenEmpty": { "note": "fail", "letter": "hold" }, "folderFormats": {} }""");

        Assert.Null(refusal);
        Assert.Equal(new Dictionary<string, string> { ["parts"] = "sections" }, options!.Renames);
        Assert.Equal(new Dictionary<string, string> { ["note"] = "fail", ["letter"] = "hold" }, options.WhenEmpty);
        Assert.Empty(options.FolderFormats!);
        Assert.Same(CompiledGoldenTests.Fixed, options.Toolchain);
        Assert.True(options.WriteSnapshot);
        Assert.Null(options.ResolveContract);

        // ToJson writes the three members in R18's order, each map's keys in ordinal order, {} for none (or null).
        Assert.Equal("""{"renames":{"parts":"sections"},"whenEmpty":{"letter":"hold","note":"fail"},"folderFormats":{}}""", options.ToJson().ToJsonString());
        Assert.Equal("""{"renames":{},"whenEmpty":{},"folderFormats":{}}""", MigrationFixture.NoChoices.ToJson().ToJsonString());
    }

    public static TheoryData<string, string, string> Refused() => new()
    {
        { """{ "renames": {}, "whenEmpty": {}, "folderFormats": {}, "toolchain": {} }""", "A10-070",
            "options.json has the member 'toolchain', which is not an option of the migration; its members are renames, whenEmpty and folderFormats." },
        { """{ "rename": {}, "whenEmpty": {}, "folderFormats": {} }""", "A10-070",
            "options.json has the member 'rename', which is not an option of the migration; its members are renames, whenEmpty and folderFormats." },
        { """{ "whenEmpty": {}, "folderFormats": {} }""", "A10-071",
            "options.json must give renames as an object whose values are text; write {} for none." },
        { """{ "renames": null, "whenEmpty": {}, "folderFormats": {} }""", "A10-071",
            "options.json must give renames as an object whose values are text; write {} for none." },
        { """{ "renames": {}, "whenEmpty": [], "folderFormats": {} }""", "A10-071",
            "options.json must give whenEmpty as an object whose values are text; write {} for none." },
        { """{ "renames": {}, "whenEmpty": {}, "folderFormats": { "standards": 1 } }""", "A10-071",
            "options.json must give folderFormats as an object whose values are text; write {} for none." },
        { """{ "renames": { "parts": { "to": "sections" } }, "whenEmpty": {}, "folderFormats": {} }""", "A10-071",
            "options.json must give renames as an object whose values are text; write {} for none." },
        { """{ "renames": {}, "whenEmpty": { "note": null }, "folderFormats": {} }""", "A10-071",
            "options.json must give whenEmpty as an object whose values are text; write {} for none." },
        { """{ "renames": {}, "whenEmpty": { "letter": "hold", "note": "maybe" }, "folderFormats": {} }""", "A10-072",
            "options.json gives deliverable 'note' a whenEmpty that is not release, hold or fail." },
        { """{ "renames": {}, "whenEmpty": { "note": "Hold" }, "folderFormats": {} }""", "A10-072",
            "options.json gives deliverable 'note' a whenEmpty that is not release, hold or fail." },
        // #1508 (Task 36, carried from Task 29's review): an empty id or choice, which the flags refuse, is refused here too.
        { """{ "renames": { "": "sections" }, "whenEmpty": {}, "folderFormats": {} }""", "A10-082",
            "options.json gives renames an empty id or an empty choice; name each id and its choice." },
        { """{ "renames": {}, "whenEmpty": {}, "folderFormats": { "standards": "" } }""", "A10-082",
            "options.json gives folderFormats an empty id or an empty choice; name each id and its choice." },
    };

    [Fact]
    public void TheFileText_IsReadStrictly_ItsByteOrderMarkDropped()
    {
        // #1508 (Task 36): the text of an options.json, as the script and the bulk publish read it.
        var (json, fault) = MigrationOptionsFile.Parse("﻿{ \"renames\": {}, \"whenEmpty\": {}, \"folderFormats\": {} }");
        Assert.Null(fault);
        Assert.Equal(["renames", "whenEmpty", "folderFormats"], json!.Select(p => p.Key));

        Assert.Equal("The file 'options.json' does not read: it names the member 'renames' twice; fix it, then migrate.",
            MigrationOptionsFile.Parse("""{ "renames": {}, "renames": {} }""").Fault);
        Assert.Equal("The file 'options.json' does not read: it is not JSON; fix it, then migrate.", MigrationOptionsFile.Parse("{").Fault);
        Assert.Equal(MigrationOptionsFile.NotAnObject, MigrationOptionsFile.Parse("[]").Fault);
        Assert.Equal(MigrationOptionsFile.NotAnObject, MigrationOptionsFile.Parse("null").Fault);
        Assert.Throws<ArgumentNullException>(() => MigrationOptionsFile.Parse(null!));
    }

    [Fact]
    public void TheDefaults_AreSaid()
    {
        // #1508 (Task 36; part 10 §§ 1.9, 1.10, part 01 § 3): a package with no options.json takes the defaults, and the
        // bulk publish says so.
        V31Contract.AssertSentence(new SourceRefusal("A10-083", MigrationOptionsFile.Defaults, ""));
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void WhatTheFileCannotSay_IsRefusedWithASentence(string json, string id, string sentence)
    {
        var (options, refusal) = Read(json);

        Assert.Null(options);
        Assert.Equal(sentence, refusal);
        V31Contract.AssertSentence(new SourceRefusal(id, refusal!, ""));
    }

    [Fact]
    public void FromJson_RefusesANullArgumentOnly()
    {
        Assert.Throws<ArgumentNullException>(() => Migration31Options.FromJson(null!, V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, TestFormats.Catalog));
        Assert.Throws<ArgumentNullException>(() => Migration31Options.FromJson(new JsonObject(), null!, CompiledGoldenTests.Fixed, TestFormats.Catalog));
        Assert.Throws<ArgumentNullException>(() => Migration31Options.FromJson(new JsonObject(), V31Contract.PublishedTypes(), null!, TestFormats.Catalog));
        Assert.Throws<ArgumentNullException>(() => Migration31Options.FromJson(new JsonObject(), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, null!));
        Assert.Equal("options.json", MigrationOptionsFile.FileName);
    }
}
