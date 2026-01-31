namespace CustomMemoryEFProvider.Core.Diagnostics;

public static class ProviderDiagnostics
{
    public static int QueryCalled;
    public static int QueryRowsCalled;
    public static int MaterializeCalled;

    public static void Reset()
    {
        QueryCalled = 0;
        QueryRowsCalled = 0;
        MaterializeCalled = 0;
    }
}