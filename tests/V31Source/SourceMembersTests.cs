using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>#1499 (specVersion 31, area 02): the every-member-is-written rules of part 01 § 5, one declaration at a time.</summary>
public class SourceMembersTests
{
    private const string Path = "nodes/plan.md";

    private static JsonObject Agent() => (JsonObject)JsonNode.Parse("""
        {
          "kind": "agent", "producer": "generated", "label": "Plan", "description": null, "format": "note",
          "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
          "if": null, "onInvalid": "retry", "failIfEmpty": null, "parts": null, "ordered": false
        }
        """)!;

    private static List<SourceRefusal> Run(JsonObject declaration, DeclarationShape? shape = null, string name = "Node 'plan'")
    {
        var refusals = new List<SourceRefusal>();
        var clean = SourceMembers.Check(declaration, shape ?? SourceShapes.NodeAgent, name, Path, refusals);
        Assert.Equal(refusals.Count == 0, clean);
        return refusals;
    }

    [Fact]
    public void ValidDeclaration_AddsNothing() => Assert.Empty(Run(Agent()));

    [Fact]
    public void Missing_IsNamed()
    {
        var declaration = Agent();
        declaration.Remove("if");
        var refusal = Assert.Single(Run(declaration));
        Assert.Equal("S01-021", refusal.Id);
        Assert.Equal("Node 'plan' is missing 'if'; every member is written, and null means none.", refusal.Sentence);
        Assert.Equal(Path, refusal.Path);
    }

    [Fact]
    public void Missing_ComesInTheShapesOrder()
    {
        var declaration = Agent();
        declaration.Remove("ordered");
        declaration.Remove("kind");
        Assert.Equal(["kind", "ordered"], Run(declaration).Select(r => r.Sentence.Split('\'')[3]));
    }

    [Fact]
    public void Null_WhereNever()
    {
        var declaration = Agent();
        declaration["label"] = null;
        var refusal = Assert.Single(Run(declaration));
        Assert.Equal("S01-022", refusal.Id);
        Assert.Equal("Node 'plan' sets 'label' to null, which it can never be.", refusal.Sentence);
    }

    [Fact]
    public void Null_WhereNullIsAValue_IsFine()
    {
        var declaration = Agent();
        declaration["failIfEmpty"] = null;
        declaration["parts"] = null;
        declaration["description"] = null;
        Assert.Empty(Run(declaration));
    }

    [Fact]
    public void Unknown_NamesTheClosest()
    {
        var declaration = Agent();
        declaration.Remove("label");
        declaration["lable"] = "Plan";
        var refusals = Run(declaration);
        Assert.Equal("S01-021", refusals[0].Id);
        var unknown = refusals[1];
        Assert.Equal("S01-023", unknown.Id);
        Assert.Equal("Node 'plan' in 'nodes/plan.md' declares unknown member 'lable'. Did you mean 'label'?", unknown.Sentence);
    }

    [Fact]
    public void Unknown_OnATie_TakesTheFirstInTableOrder()
    {
        // 'x' is one edit from every one-letter name and from none here; 'if' and 'id' tie at distance 2 from 'ix': 'if' comes first.
        var declaration = Agent();
        declaration["ix"] = 1;
        Assert.Contains("Did you mean 'if'?", Assert.Single(Run(declaration)).Sentence);
    }

    [Fact]
    public void WrongType_IsNamed()
    {
        var declaration = (JsonObject)JsonNode.Parse("""
            {"kind":"for","label":"S","description":null,"format":"note","reproducible":false,"producer":"computed",
             "modelDerived":false,"runDependent":false,"profileCarrying":false,"in":"node.plan","parallel":"yes",
             "maxParallel":null,"until":null,"max":null,"itemLabel":null,"nodes":["a"],"ret":"node.a"}
            """)!;
        var refusal = Assert.Single(Run(declaration, SourceShapes.FlowFor, "Loop 'sections'"));
        Assert.Equal("A02-001", refusal.Id);
        Assert.Equal("Loop 'sections' declares 'parallel' as text; it is true or false.", refusal.Sentence);
    }

    [Theory]
    [InlineData("""{"ordered": 1}""", "declares 'ordered' as a whole number; it is true or false.")]
    [InlineData("""{"label": ["a"]}""", "declares 'label' as a list; it is text.")]
    [InlineData("""{"label": {}}""", "declares 'label' as an object; it is text.")]
    [InlineData("""{"description": "x"}""", "declares 'description' as text; it is a list of text.")]
    [InlineData("""{"description": ["x", 2]}""", "declares 'description' as a list; it is a list of text.")]
    [InlineData("""{"parts": []}""", "declares 'parts' as a list; it is an object.")]
    [InlineData("""{"format": 1.5}""", "declares 'format' as a number; it is text.")]
    [InlineData("""{"format": true}""", "declares 'format' as true or false; it is text.")]
    public void WrongType_Words(string patch, string tail)
    {
        var declaration = Agent();
        foreach (var (key, value) in (JsonObject)JsonNode.Parse(patch)!)
            declaration[key] = value?.DeepClone();
        var refusal = Assert.Single(Run(declaration));
        Assert.Equal("A02-001", refusal.Id);
        Assert.Equal("Node 'plan' " + tail, refusal.Sentence);
    }

    [Fact]
    public void Integer_AcceptsAWholeNumberAndRefusesAFraction()
    {
        var manifest = (JsonObject)JsonNode.Parse("""
            {"name":"a","version":"v2026.01.1","specVersion":31,"derivedFrom":null,"title":null,"description":null,"tags":[],"inputs":{}}
            """)!;
        Assert.Empty(Run(manifest, SourceShapes.Manifest, "The manifest"));
        manifest["specVersion"] = 31.5;
        Assert.Equal("The manifest declares 'specVersion' as a number; it is a whole number.", Assert.Single(Run(manifest, SourceShapes.Manifest, "The manifest")).Sentence);
        manifest["specVersion"] = "31";
        Assert.Equal("A02-001", Assert.Single(Run(manifest, SourceShapes.Manifest, "The manifest")).Id);
    }

    [Fact]
    public void Format_TakesATextOrAnObject()
    {
        var declaration = Agent();
        var node = (JsonObject)JsonNode.Parse("""
            {"kind":"computed","producer":"computed","label":"C","description":null,"format":{"type":"string"},
             "reproducible":true,"modelDerived":false,"runDependent":false,"profileCarrying":false,"if":null,"expression":1}
            """)!;
        Assert.Empty(Run(node, SourceShapes.NodeComputed));
        Assert.Empty(Run(declaration));
    }

    [Fact]
    public void Description_EmptyArray()
    {
        var declaration = Agent();
        declaration["description"] = new JsonArray();
        var refusal = Assert.Single(Run(declaration));
        Assert.Equal("S01-024", refusal.Id);
        Assert.Equal("Node 'plan' has an empty description; write null for none.", refusal.Sentence);
    }

    [Fact]
    public void Description_CountsCodePointsJoined()
    {
        var declaration = Agent();
        declaration["description"] = new JsonArray(new string('a', 250), new string('b', 250));
        var refusal = Assert.Single(Run(declaration));
        Assert.Equal("S01-025", refusal.Id);
        Assert.Equal("Node 'plan''s description is 501 characters; the most is 500.", refusal.Sentence);

        declaration["description"] = new JsonArray(new string('a', 250), new string('b', 249));
        Assert.Empty(Run(declaration));

        // 500 astral characters are 1000 UTF-16 units but 500 code points.
        declaration["description"] = new JsonArray(string.Concat(Enumerable.Repeat("\U0001F600", 500)));
        Assert.Empty(Run(declaration));
        declaration["description"] = new JsonArray(string.Concat(Enumerable.Repeat("\U0001F600", 501)));
        Assert.Contains("is 501 characters", Assert.Single(Run(declaration)).Sentence);
    }

    [Fact]
    public void Label_Whitespace()
    {
        var declaration = Agent();
        foreach (var blank in new[] { "", "   ", "\t\n" })
        {
            declaration["label"] = blank;
            var refusal = Assert.Single(Run(declaration));
            Assert.Equal("S01-026", refusal.Id);
            Assert.Equal("Node 'plan' has an empty label.", refusal.Sentence);
        }
    }

    [Fact]
    public void DescriptionAndLabelRules_WaitForTheTypeToBeRight()
    {
        var declaration = Agent();
        declaration["description"] = 5;
        declaration["label"] = 5;
        Assert.All(Run(declaration), r => Assert.Equal("A02-001", r.Id));
    }

    [Fact]
    public void EmptyValues_AreValues()
    {
        var manifest = (JsonObject)JsonNode.Parse("""
            {"name":"a","version":"v2026.01.1","specVersion":31,"derivedFrom":null,"title":null,"description":null,"tags":[],"inputs":{}}
            """)!;
        Assert.Empty(Run(manifest, SourceShapes.Manifest, "The manifest"));
    }

    [Fact]
    public void NeverThrows_OnAnyMemberValue()
    {
        foreach (var shape in new[] { SourceShapes.NodeAgent, SourceShapes.FlowCase, SourceShapes.Manifest })
        {
            foreach (var json in new[] { "null", "[]", "{}", "[null]", "1e999", "\"\\ud800\"", "[[1]]", "true" })
            {
                var declaration = new JsonObject();
                foreach (var member in shape.Members)
                    declaration[member.Name] = JsonNode.Parse(json);
                declaration["x"] = JsonNode.Parse(json);
                var refusals = new List<SourceRefusal>();
                SourceMembers.Check(declaration, shape, "Thing", Path, refusals);
            }
        }
    }

    [Fact]
    public void EveryMemberSentence_IsTheDesigns()
    {
        var declaration = Agent();
        declaration.Remove("if");
        declaration["label"] = null;
        declaration["lable"] = 1;
        declaration["ordered"] = "no";
        declaration["description"] = new JsonArray();
        var refusals = Run(declaration);
        Assert.Equal(["S01-022", "S01-021", "A02-001", "S01-023", "S01-024"], refusals.Select(r => r.Id));
        foreach (var refusal in refusals)
            V31Contract.AssertSentence(refusal);

        declaration = Agent();
        declaration["description"] = new JsonArray();
        declaration["label"] = " ";
        var tail = Run(declaration);
        Assert.Equal(["S01-024", "S01-026"], tail.Select(r => r.Id));
        declaration["description"] = new JsonArray(new string('x', 501));
        tail.AddRange(Run(declaration));
        foreach (var refusal in tail)
            V31Contract.AssertSentence(refusal);
    }
}
