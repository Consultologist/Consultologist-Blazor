using Consultologist.Api.Models;

namespace Consultologist.Api.Tests;

public class InputOriginKindsTests
{
    [Fact]
    public void TypedOriginKind_IsTyped()
    {
        Assert.Equal("typed", Consultologist.Api.Models.ConsultInputOriginKinds.Typed);
    }
}
