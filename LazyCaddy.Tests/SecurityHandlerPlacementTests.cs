using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class SecurityHandlerPlacementTests
{
    [Fact] public void InsertIndex_BeforeFirstTerminalHandler()
        => Assert.Equal(1, SecurityHandlerPlacement.InsertIndex("""[{"handler":"headers"},{"handler":"reverse_proxy"}]"""));
    [Fact] public void InsertIndex_FileServerIsTerminal()
        => Assert.Equal(0, SecurityHandlerPlacement.InsertIndex("""[{"handler":"file_server"}]"""));
    [Fact] public void InsertIndex_StaticResponseIsTerminal()
        => Assert.Equal(0, SecurityHandlerPlacement.InsertIndex("""[{"handler":"static_response"}]"""));
    [Fact] public void InsertIndex_SubrouteIsTerminal()
        => Assert.Equal(0, SecurityHandlerPlacement.InsertIndex("""[{"handler":"subroute"}]"""));
    [Fact] public void InsertIndex_NoTerminal_AppendsAtEnd()
        => Assert.Equal(2, SecurityHandlerPlacement.InsertIndex("""[{"handler":"headers"},{"handler":"rewrite"}]"""));
    [Fact] public void InsertIndex_Empty_Zero()
        => Assert.Equal(0, SecurityHandlerPlacement.InsertIndex("[]"));
    [Fact] public void InsertIndex_NonArray_Zero()
        => Assert.Equal(0, SecurityHandlerPlacement.InsertIndex("""{"x":1}"""));
}
