using Avalonia.Threading;
using LaTeXInserter.Services;
using SharpHook;
using Xunit;

namespace LaTeXInserter.Tests;

public class HotkeyServiceStartTests
{
    [Fact]
    public void HookFailed_RaisedWhenRunAsyncThrows()
    {
        // SimpleGlobalHook cannot be substituted (non-virtual members), so drive the
        // failure path through a disposed hook: RunAsync on a disposed hook faults.
        var hook = new SimpleGlobalHook();
        hook.Dispose();

        var sut = new HotkeyService(hook);
        string? reported = null;
        sut.HookFailed += (_, message) => reported = message;

        sut.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        // HotkeyService marshals the failure onto the Avalonia dispatcher. The test
        // host has no UI message loop, so drain queued jobs manually.
        SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return reported is not null;
            },
            TimeSpan.FromSeconds(5));

        Assert.NotNull(reported);
        Assert.False(sut.IsRunning);
    }
}
