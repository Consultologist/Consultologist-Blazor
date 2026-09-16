using System.Runtime.CompilerServices;

// #733: the chart helpers expose internal seams (NiceCeil, ShowsLabel, Short)
// the tests assert directly. Written here rather than as an <InternalsVisibleTo>
// item because this project sets GenerateAssemblyInfo=false, so MSBuild emits no
// assembly attributes and the item form is silently inert (the Web project's
// AssemblyAttributes.cs precedent). Both apps' test projects render/inspect the
// shared components.
[assembly: InternalsVisibleTo("Consultologist.Web.Tests")]
[assembly: InternalsVisibleTo("Consultologist.Admin.Tests")]
