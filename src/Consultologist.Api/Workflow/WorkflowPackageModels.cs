using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Workflow;

/// <summary>
/// A deliverable of a resolved package: the declared (or sugar-derived) id and
/// label with the aggregator node it names. v5/v6 packages resolve with a null
/// set — ResultNodeId remains their single-result contract.
/// </summary>
public sealed record WorkflowResolvedResult(
    string Id,
    string NodeId,
    string Label,
    // Parsed at load so the engine evaluates a structure rather than
    // re-parsing a string. Null = always produced. v10 (#494): an expression
    // tree; a v9 condition is its one leaf.
    WorkflowConditionExpression? Condition = null,
    // v11 #513: the macro ids this deliverable appends, in declared order —
    // null below 11 and when the result names none.
    IReadOnlyList<string>? Macros = null,
    // v11 #516: the package says this deliverable is signed — the profile's
    // chosen block is appended at completion. Null below 11 and when unset.
    bool? Signature = null,
    // v12 #619: the placed macros' anchors, resolved from the manifest's
    // entry objects; null when every entry is the bare v11 form.
    IReadOnlyList<Consultologist.Api.Models.ConsultMacroPlacement>? MacroPlacements = null,
    // v12 #624: the check node gating this deliverable (node:<id>).
    string? Check = null);

public sealed record WorkflowPackage(
    WorkflowPackageManifest Manifest,
    IReadOnlyDictionary<string, WorkflowPromptTemplate>? Prompts = null,
    IReadOnlyList<WorkflowNodeSpec>? Nodes = null,
    IReadOnlyDictionary<string, string>? SchemaContracts = null,
    WorkflowPackageData? Data = null,
    string? ResultNodeId = null,
    IReadOnlyDictionary<string, string>? SourceFiles = null,
    IReadOnlyList<WorkflowResolvedResult>? Results = null,
    // #433: the publication stamp the version carries, or null for every
    // version published before it existed. Deliberately not in SourceFiles.
    WorkflowPackageStamp? Stamp = null)
{
    public string Ref => $"{Manifest.Name}@{Manifest.Version}";

    public bool HasPrompts => Prompts is { Count: > 0 };
}

public sealed record WorkflowPackageBlockResponse(string Id, string Name);

/// <summary>
/// One declared input slot on the current-package response — what the consult
/// setup form renders a field for (package-format-v7.md § inputs). Null on
/// v5/v6 packages, whose single slot is the frozen consult_draft convention.
/// </summary>
public sealed record WorkflowPackageInputResponse(
    string Id,
    string Label,
    bool Required,
    // v8: what the setup form renders. Trailing optionals — a v5-v7 package
    // sends null and the form draws the textarea it always did.
    string? Type = null,
    IReadOnlyList<string>? Values = null,
    // v9 (#424): the element of an array and the fields of an object, so the
    // form can draw a repeating row or a field group (#429). Null on every
    // package before 9. v10 (#497): the element is a spec, not a type name,
    // and it carries its own fields and values at every depth — a v9 array
    // of objects sends its fields on the element as well as here.
    WorkflowPackageElementResponse? Items = null,
    IReadOnlyList<WorkflowPackageFieldResponse>? Fields = null);

/// <summary>
/// One declared field of an object, as the setup form renders it (v9 § 4).
/// v10 (#497): a field may itself be structure — trailing Items and Fields,
/// null on every field that is a scalar.
/// </summary>
public sealed record WorkflowPackageFieldResponse(
    string Id,
    string Label,
    bool Required,
    string? Type = null,
    IReadOnlyList<string>? Values = null,
    WorkflowPackageElementResponse? Items = null,
    IReadOnlyList<WorkflowPackageFieldResponse>? Fields = null);

/// <summary>
/// v10 (#497): what one element of an array is — the resolved shape of
/// WorkflowDeclarationNode.ElementOf, so the form never sees the manifest's
/// bare-vs-spec split. Type is resolved (text when the manifest left it
/// out); Items and Fields are the next level down; Values are an enum's.
/// </summary>
public sealed record WorkflowPackageElementResponse(
    string Type,
    WorkflowPackageElementResponse? Items = null,
    IReadOnlyList<WorkflowPackageFieldResponse>? Fields = null,
    IReadOnlyList<string>? Values = null);

/// <summary>
/// One declared deliverable on the current-package response: the authored
/// identity the client groups blocks and result tabs by. The result node is
/// engine-side and never leaves the API.
/// </summary>
public sealed record WorkflowPackageResultResponse(string Id, string Label);

public sealed record WorkflowPackageResponse(
    string Name,
    string Version,
    int SpecVersion,
    IReadOnlyList<WorkflowPackageBlockResponse>? Blocks = null,
    IReadOnlyList<WorkflowPackageInputResponse>? Inputs = null,
    IReadOnlyList<WorkflowPackageResultResponse>? Results = null,
    // v9 § 4 (#432): the package's title; null when it has none, and the
    // client shows the ref instead — the stated fallback.
    string? Title = null);

/// <summary>
/// The pin-resolved package's full editable content: the typed manifest (the
/// binding-value converter round-trips it) plus every source file the store
/// downloaded — prompts (incl. preludes), schemas, and data files including each
/// collection's index.json. The editor's load half of the load→edit→publish
/// round-tripping contract (docs/customizable-workflow/in-app-editing.md).
/// </summary>
public sealed record WorkflowPackageContentResponse(
    string Name,
    string Version,
    int SpecVersion,
    WorkflowPackageManifest Manifest,
    IReadOnlyDictionary<string, string> Files);

/// <summary>
/// The publish half of the editor's round-tripping contract: the manifest and
/// files as loaded from the content endpoint (edited texts substituted), plus
/// Source — the concrete ref the content was loaded from, which the server
/// validates and stamps as the fork's derivedFrom. The client's manifest
/// name/version/derivedFrom are ignored.
/// </summary>
public sealed record WorkflowPackagePublishRequest(
    string? Source = null,
    WorkflowPackageManifest? Manifest = null,
    Dictionary<string, string>? Files = null,
    // #447: where it goes. Target names one of the account's packages for a
    // new version; NewPackageSlug names a package the account does not have
    // yet — a slug or, since #448, a folder path (acct-<root>/oncology/breast);
    // both null is every older client — the account's first, derived name.
    // Both set is refused.
    string? Target = null,
    string? NewPackageSlug = null);

public sealed record WorkflowPackagePublishResponse(
    string Name,
    string Version,
    string Ref,
    IReadOnlyList<string> Warnings);
