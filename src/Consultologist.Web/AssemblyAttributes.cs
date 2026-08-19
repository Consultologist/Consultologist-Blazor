using System.Runtime.CompilerServices;

// The two spec-version constants this assembly mirrors by hand — the picker's
// floor and the editor's ceiling — are internal so the test project can assert
// they still match the engine's set (#376). The test project references both
// this assembly and the Api, which is what makes the mirror provable without
// the client fetching anything at runtime.
//
// Written here rather than as an <InternalsVisibleTo> item because this project
// sets GenerateAssemblyInfo=false, so MSBuild emits no assembly attributes at
// all and the item form is silently inert.
[assembly: InternalsVisibleTo("Consultologist.Web.Tests")]
