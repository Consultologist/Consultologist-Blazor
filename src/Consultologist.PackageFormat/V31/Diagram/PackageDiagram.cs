namespace Consultologist.PackageFormat.V31.Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): a 31 package's <c>dag.mmd</c> (part 09 § 10.1), drawn from its canonical
/// <c>resolve.json</c> text: the <see cref="Func{T, TResult}"/> that <c>PublishOptions.Diagram</c> takes. Never throws on
/// the document's content (Review Focus 5).
/// </summary>
public static class PackageDiagram
{
    public static string Generate(string resolve) => DiagramWriter.Write(DiagramModel.Build(resolve));
}
