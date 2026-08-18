using LaTeXInserter.Platform.MacOS;
using Xunit;
using Xunit.Sdk;

namespace LaTeXInserter.Tests;

public class MacInteropTests
{
    [Fact]
    public void CGPoint_IsBlittableTwoDoubles()
    {
        Assert.Equal(16, System.Runtime.InteropServices.Marshal.SizeOf<MacNativeMethods.CGPoint>());
    }

    [SkippableFact]
    public void GetClass_ResolvesNSObject()
    {
        Skip.IfNot(OperatingSystem.IsMacOS());
        Assert.NotEqual(IntPtr.Zero, ObjC.GetClass("NSObject"));
    }

    [SkippableFact]
    public void Sel_ResolvesDescription()
    {
        Skip.IfNot(OperatingSystem.IsMacOS());
        Assert.NotEqual(IntPtr.Zero, ObjC.Sel("description"));
    }
}
