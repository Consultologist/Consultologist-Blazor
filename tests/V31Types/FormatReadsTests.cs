using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>#1500 (specVersion 31, area 03): <see cref="FormatReads"/>, typed reads (part 02 § 2.1, § 2.2, § 5.3, § 9).</summary>
public class FormatReadsTests
{
    private static readonly FormatSet Formats = FormatSet.ForPackage(TypeFixture.Published(), []);

    private static readonly JsonNode SectionItem = JsonNode.Parse("""
        { "type": "object", "required": ["title", "kind"], "additionalProperties": false, "properties": {
            "title": { "$ref": "plaintext" },
            "kind": { "type": "string", "oneOf": [
              { "const": "narrative", "title": "Narrative" },
              { "const": "table", "title": "Table" },
              { "const": "omit", "title": "Leave out" } ] } } }
        """)!;

    private static readonly JsonNode VisitType = JsonNode.Parse("""
        { "type": "string", "oneOf": [
          { "const": "new_patient", "title": "New patient" },
          { "const": "follow_up", "title": "Follow-up" } ] }
        """)!;

    private static ReadResult Read(JsonNode? format, params string[] steps) => FormatReads.Read(format, steps, Formats);

    private static string Print(ReadResult r) => TypeStrings.Print(r.Format, Formats);

    [Fact]
    public void Value_GivesTheFormat_ContentItsText()
    {
        var value = Read("markdown_block", "value");
        Assert.Equal(ReadFault.None, value.Fault);
        Assert.Equal("markdown_block", Print(value));
        Assert.Equal("markdown_block", Print(Read("markdown_block", "content")));
        Assert.Equal("plaintext", Print(Read("judgement", "content")));
        Assert.Equal(ReadFault.NoContent, Read("number", "content").Fault);
        Assert.Equal(ReadFault.NoContent, Read(VisitType, "content").Fault);
    }

    [Fact]
    public void AFieldPath_IsTypedAtAnyDepth()
    {
        Assert.Equal("plaintext", Print(Read("judgement", "value", "reason")));
        Assert.Equal("boolean", Print(Read("judgement", "value", "answer")));
        var concepts = Read("concept_list", "value", "concepts");
        Assert.StartsWith("list<{ term: string", Print(concepts));
        Assert.Equal(ReadFault.NoSuchField, Read("judgement", "value", "nope").Fault);
        Assert.Equal(ReadFault.NotAnObject, Read("judgement", "value", "reason", "x").Fault);
        Assert.Equal(ReadFault.NotAnObject, Read("number", "value", "x").Fault);
    }

    [Fact]
    public void Title_ReadsALabelledEnum_AtAnyDepth()
    {
        var r = Read(SectionItem, "value", "kind", "title");
        Assert.Equal(ReadFault.None, r.Fault);
        Assert.Equal("string", Print(r));
        Assert.Equal(["Narrative", "Table", "Leave out"], r.Titles);
        var direct = Read(VisitType, "title");
        Assert.Equal(["New patient", "Follow-up"], direct.Titles);
        Assert.Equal(["New patient", "Follow-up"], Read(VisitType, "value", "title").Titles);
    }

    [Fact]
    public void Title_OfAPlainEnum_IsRefused()
    {
        var plain = JsonNode.Parse("""{ "type": "string", "enum": ["a", "b"] }""");
        Assert.Equal(ReadFault.TitleOfPlainEnum, Read(plain, "title").Fault);
        Assert.Equal(ReadFault.TitleOfNonEnum, Read("number", "title").Fault);
        Assert.Equal(ReadFault.TitleOfNonEnum, Read("judgement", "value", "title").Fault);
        var refusal = FormatReads.TitleOfPlainEnum("item.value.kind", "workflow.json");
        Assert.Equal("S02-019", refusal.Id);
        Assert.Equal("'item.value.kind.title' reads the title of a plain enum, which has none; use a labelled oneOf, or read .value.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void AFieldCalledTitle_IsTheField()
    {
        var r = Read(SectionItem, "value", "title");
        Assert.Equal(ReadFault.None, r.Fault);
        Assert.Equal("plaintext", Print(r));
        Assert.Null(r.Titles);
    }

    [Fact]
    public void ABareRead_IsRefused()
    {
        Assert.Equal(ReadFault.Bare, Read("plaintext").Fault);
        var refusal = FormatReads.BareInput("visit_type", "workflow.json");
        Assert.Equal("S02-018", refusal.Id);
        Assert.Equal("'input.visit_type' is read bare; read input.visit_type.value, or input.visit_type.content for text.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
        Assert.Equal(ReadFault.UnknownMember, Read("plaintext", "state").Fault);
        Assert.Equal(ReadFault.UnknownMember, Read("plaintext", "items").Fault);
        Assert.Equal(ReadFault.UnknownMember, Read("plaintext", "content", "x").Fault);
    }

    [Fact]
    public void ATitleLiteral_MustBeOneOfTheTitles()
    {
        var read = Read(VisitType, "title");
        var refusal = FormatReads.TitleLiteral("Node 'route' if", "input.visit_type", "Followup", read, "workflow.json");
        Assert.NotNull(refusal);
        Assert.Equal("S02-020", refusal!.Id);
        Assert.Equal("Node 'route' if compares input.visit_type.title with 'Followup', which is not one of its titles (New patient, Follow-up).", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
        Assert.Null(FormatReads.TitleLiteral("Node 'route' if", "input.visit_type", "Follow-up", read, "workflow.json"));
        Assert.Null(FormatReads.TitleLiteral("c", "x", "y", Read("number", "value"), "workflow.json"));
    }

    [Fact]
    public void AUnion_ReadsContentAlways_AndAFieldOnlyWhenEveryMemberAgrees()
    {
        var union = JsonNode.Parse("""{ "anyOf": [ { "$ref": "judgement" }, { "$ref": "check_result" } ] }""");
        Assert.Equal("plaintext", Print(Read(union, "content")));
        Assert.Equal(ReadFault.None, Read(union, "value").Fault);
        Assert.Equal(ReadFault.NotEveryMember, Read(union, "value", "answer").Fault);

        var agree = JsonNode.Parse("""
            { "anyOf": [
              { "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": { "$ref": "plaintext" } } },
              { "type": "object", "required": ["a", "b"], "additionalProperties": false, "properties": { "a": { "$ref": "plaintext" }, "b": { "type": "number" } } } ] }
            """);
        Assert.Equal("plaintext", Print(Read(agree, "value", "a")));
        Assert.Equal(ReadFault.NotEveryMember, Read(agree, "value", "b").Fault);

        var differ = JsonNode.Parse("""
            { "anyOf": [
              { "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": { "$ref": "plaintext" } } },
              { "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": { "$ref": "markdown_block" } } } ] }
            """);
        Assert.Equal(ReadFault.NotEveryMember, Read(differ, "value", "a").Fault);
    }

    [Fact]
    public void ANullableField_MakesTheReadNullable()
    {
        var obj = JsonNode.Parse("""
            { "type": "object", "required": ["note", "n"], "additionalProperties": false, "properties": {
                "note": { "anyOf": [ { "$ref": "plaintext" }, { "type": "null" } ] },
                "n": { "type": "number" } } }
            """);
        var note = Read(obj, "value", "note");
        Assert.True(note.Nullable);
        Assert.False(Read(obj, "value", "n").Nullable);

        var nullableObject = JsonNode.Parse("""
            { "anyOf": [ { "$ref": "judgement" }, { "type": "null" } ] }
            """);
        var reason = Read(nullableObject, "value", "reason");
        Assert.Equal(ReadFault.None, reason.Fault);
        Assert.True(reason.Nullable);
        Assert.Equal("plaintext", Print(reason));
        Assert.True(Read(nullableObject, "value").Nullable);

        var typeArray = JsonNode.Parse("""
            { "type": ["object", "null"], "required": ["x"], "additionalProperties": false, "properties": { "x": { "type": "number" } } }
            """);
        Assert.True(Read(typeArray, "value", "x").Nullable);
    }

    [Fact]
    public void AFieldReachedThroughDefs_IsFollowed()
    {
        var format = JsonNode.Parse("""
            { "$defs": { "inner": { "type": "object", "required": ["q"], "additionalProperties": false, "properties": { "q": { "type": "number" } } } },
              "type": "object", "required": ["i"], "additionalProperties": false, "properties": { "i": { "$ref": "#/$defs/inner" } } }
            """);
        Assert.Equal("number", Print(Read(format, "value", "i", "q")));
        Assert.Equal("{ q: number }", Print(Read(format, "value", "i")));
    }

    [Fact]
    public void ANameField_InAFileWithDefs_StaysExactlyTheName()
    {
        var format = JsonNode.Parse("""
            { "$defs": { "inner": { "type": "object", "required": ["q"], "additionalProperties": false, "properties": { "q": { "type": "number" } } },
                         "alias": { "$ref": "judgement", "description": "An alias." } },
              "type": "object", "required": ["j", "a", "d", "n"], "additionalProperties": false, "properties": {
                "j": { "$ref": "judgement", "description": "Why." },
                "a": { "$ref": "#/$defs/alias" },
                "d": { "$ref": "#/$defs/inner" },
                "n": { "type": "string" } } }
            """);
        var j = Read(format, "value", "j");
        Assert.Equal("judgement", Print(j));
        Assert.True(FormatTypes.Same(j.Format, "judgement", Formats));
        Assert.Equal("plaintext", Print(Read(format, "value", "j", "reason")));
        Assert.Equal("{ \"$ref\": \"judgement\", \"description\": \"Why.\" }".Replace(" ", ""), j.Format!.ToJsonString().Replace(" ", ""));

        var a = Read(format, "value", "a");
        Assert.Equal("judgement", Print(a));
        Assert.True(FormatTypes.Same(a.Format, "judgement", Formats));
        Assert.Null(a.Format!["$defs"]);
        Assert.Equal("plaintext", Print(Read(format, "value", "a", "reason")));
        Assert.Null(Read(format, "value", "n").Format!["$defs"]);
    }

    [Fact]
    public void ANullableLabelledEnum_ExcludesTheNullArm_AndReadsNullable()
    {
        var nullableEnum = JsonNode.Parse("""
            { "type": ["string", "null"], "oneOf": [
              { "const": "a", "title": "Alpha" }, { "const": "b", "title": "Beta" }, { "const": null } ] }
            """);
        var r = Read(nullableEnum, "title");
        Assert.Equal(["Alpha", "Beta"], r.Titles);
        Assert.True(r.Nullable);

        var inObject = JsonNode.Parse("""
            { "anyOf": [ { "type": "object", "required": ["k"], "additionalProperties": false, "properties": { "k": { "type": "string", "oneOf": [ { "const": "a", "title": "Alpha" }, { "const": "b", "title": "Beta" } ] } } }, { "type": "null" } ] }
            """);
        var through = Read(inObject, "value", "k", "title");
        Assert.Equal(["Alpha", "Beta"], through.Titles);
        Assert.True(through.Nullable);
    }

    [Fact]
    public void AUnionMembersOwnNullability_MakesTheFieldReadNullable()
    {
        var union = JsonNode.Parse("""
            { "anyOf": [
              { "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": { "$ref": "plaintext" } } },
              { "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": { "$ref": "plaintext" } } },
              { "type": "null" } ] }
            """);
        Assert.True(Read(union, "value", "a").Nullable);
        var nested = JsonNode.Parse("""
            { "anyOf": [
              { "anyOf": [ { "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": { "$ref": "plaintext" } } }, { "type": "null" } ] },
              { "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": { "$ref": "plaintext" } } } ] }
            """);
        var r = Read(nested, "value", "a");
        Assert.Equal(ReadFault.None, r.Fault);
        Assert.True(r.Nullable);
    }

    [Fact]
    public void ATitleLiteral_IsNotRefused_WhenThereAreNoTitles()
    {
        Assert.Null(FormatReads.TitleLiteral("c", "x", "y", new ReadResult(null, ReadFault.None, false, null), "w.json"));
        Assert.Null(FormatReads.TitleLiteral("c", "x", "y", new ReadResult(null, ReadFault.None, false, []), "w.json"));
    }

    [Fact]
    public void NeverThrows_OnOddFormatsOrSteps()
    {
        var cyclic = JsonNode.Parse("""{ "$defs": { "a": { "$ref": "#/$defs/a" } }, "$ref": "#/$defs/a" }""");
        var odd = new JsonNode?[] { null, JsonValue.Create(5), JsonValue.Create(""), new JsonArray(), "nope", cyclic, JsonNode.Parse("""{ "anyOf": [] }"""), JsonNode.Parse("""{ "anyOf": [ { "$ref": "#/$defs/z" }, null, 3 ] }""") };
        foreach (var format in odd)
        {
            foreach (var steps in new[] { Array.Empty<string>(), ["value"], ["content"], ["title"], ["value", "x"], ["value", ""], ["value", "title"], [""], ["value", null!] })
            {
                Assert.NotNull(FormatReads.Read(format, steps, Formats));
            }
        }

        foreach (var format in new JsonNode?[] { null, "nope", cyclic, JsonValue.Create(5) })
        {
            Assert.NotEqual(ReadFault.None, FormatReads.Read(format, ["value", "x"], Formats).Fault);
            Assert.NotEqual(ReadFault.None, FormatReads.Read(format, ["content"], Formats).Fault);
            Assert.NotEqual(ReadFault.None, FormatReads.Read(format, ["title"], Formats).Fault);
        }
    }
}
