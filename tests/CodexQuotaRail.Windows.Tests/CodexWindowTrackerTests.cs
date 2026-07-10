using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.Windows.Tests;

public sealed class CodexWindowTrackerTests
{
    private static readonly WindowProcessIdentity PackagedCodex = new(
        @"C:\Program Files\WindowsApps\OpenAI.Codex\Codex.exe",
        "OpenAI.Codex_1.0.0.0_x64__test",
        null);

    [Fact]
    public void StartSelectsPackagedCodexAndRejectsUnsignedNameOnlyWindow()
    {
        var native = new FakeWindowNativeApi();
        native.Add(new FakeWindow(
            1,
            new WindowProcessIdentity(@"C:\Temp\Codex.exe", null, null)));
        native.Add(new FakeWindow(2, PackagedCodex));
        native.ForegroundWindow = 2;
        using var tracker = new CodexWindowTracker(native, new ManualWindowUpdateScheduler());

        tracker.Start();

        Assert.Equal((nint)2, tracker.CurrentSnapshot?.Handle);
    }

    [Fact]
    public void StartAcceptsUnpackagedCodexOnlyWhenItsSignerIsOpenAi()
    {
        var native = new FakeWindowNativeApi();
        native.Add(new FakeWindow(
            7,
            new WindowProcessIdentity(
                @"C:\Tools\Codex.exe",
                null,
                "CN=OpenAI Code Signing, O=OpenAI, C=US")));
        native.ForegroundWindow = 7;
        using var tracker = new CodexWindowTracker(native, new ManualWindowUpdateScheduler());

        tracker.Start();

        Assert.Equal((nint)7, tracker.CurrentSnapshot?.Handle);
    }

    [Fact]
    public void ForegroundAndHideEventsSelectMostRecentlyActiveVisibleMainWindow()
    {
        var native = new FakeWindowNativeApi();
        native.Add(new FakeWindow(10, PackagedCodex));
        native.Add(new FakeWindow(11, PackagedCodex));
        native.ForegroundWindow = 10;
        var scheduler = new ManualWindowUpdateScheduler();
        using var tracker = new CodexWindowTracker(native, scheduler);
        tracker.Start();

        native.ForegroundWindow = 11;
        native.Raise(TrackedWindowEvent.Foreground, 11);
        scheduler.RunFrame();
        native.Window(11).IsVisible = false;

        native.Raise(TrackedWindowEvent.Hide, 11);
        scheduler.RunFrame();

        Assert.Equal((nint)10, tracker.CurrentSnapshot?.Handle);
    }

    [Fact]
    public void LocationEventsCoalesceToOneSnapshotReadPerSchedulerFrame()
    {
        var native = new FakeWindowNativeApi();
        native.Add(new FakeWindow(20, PackagedCodex));
        native.ForegroundWindow = 20;
        var scheduler = new ManualWindowUpdateScheduler();
        using var tracker = new CodexWindowTracker(native, scheduler);
        tracker.Start();
        var readsBeforeEvents = native.SnapshotReadCount;

        for (var index = 0; index < 25; index++)
        {
            native.Raise(TrackedWindowEvent.LocationChange, 20);
        }

        Assert.Equal(1, scheduler.PendingCount);
        scheduler.RunFrame();
        Assert.Equal(readsBeforeEvents + 1, native.SnapshotReadCount);
    }

    [Fact]
    public void SnapshotRetainsDpiWindowStateAndForegroundState()
    {
        var native = new FakeWindowNativeApi();
        var window = new FakeWindow(30, PackagedCodex)
        {
            Bounds = new PixelRect(200, 150, 900, 700),
            WorkArea = new PixelRect(0, 0, 2560, 1400),
            Dpi = 144,
            IsMinimized = true,
            IsMaximized = false,
        };
        native.Add(window);
        native.ForegroundWindow = 30;
        using var tracker = new CodexWindowTracker(native, new ManualWindowUpdateScheduler());

        tracker.Start();

        Assert.Equal(new PixelRect(200, 150, 900, 700), tracker.CurrentSnapshot?.Bounds);
        Assert.Equal(new PixelRect(0, 0, 2560, 1400), tracker.CurrentSnapshot?.WorkArea);
        Assert.Equal(1.5, tracker.CurrentSnapshot?.DpiScale);
        Assert.True(tracker.CurrentSnapshot?.IsMinimized);
        Assert.True(tracker.CurrentSnapshot?.IsForeground);
    }

    [Fact]
    public void DisposeReleasesEveryHookAndSuppressesPendingFrame()
    {
        var native = new FakeWindowNativeApi();
        native.Add(new FakeWindow(40, PackagedCodex));
        native.ForegroundWindow = 40;
        var scheduler = new ManualWindowUpdateScheduler();
        var tracker = new CodexWindowTracker(native, scheduler);
        tracker.Start();
        var readsBeforeEvent = native.SnapshotReadCount;
        native.Raise(TrackedWindowEvent.LocationChange, 40);
        var hookCount = native.HookedEvents.Count;

        tracker.Dispose();
        scheduler.RunFrame();

        Assert.Equal(7, hookCount);
        Assert.Equal(hookCount, native.UnhookedHandles.Count);
        Assert.Equal(readsBeforeEvent, native.SnapshotReadCount);
    }

    [Fact]
    public void DisposeDuringHookInstallationCannotLeakTheNewHook()
    {
        var native = new FakeWindowNativeApi();
        native.Add(new FakeWindow(50, PackagedCodex));
        var tracker = new CodexWindowTracker(native, new ManualWindowUpdateScheduler());
        native.HookInstalled = tracker.Dispose;

        Assert.Throws<ObjectDisposedException>(tracker.Start);

        Assert.Empty(native.HookedEvents);
        Assert.Single(native.UnhookedHandles);
    }

    [Fact]
    public void StartSkipsInaccessibleWindowAndContinuesSelectingCandidates()
    {
        var native = new FakeWindowNativeApi();
        native.Add(new FakeWindow(60, PackagedCodex) { ThrowsOnIdentity = true });
        native.Add(new FakeWindow(61, PackagedCodex));
        native.ForegroundWindow = 61;
        using var tracker = new CodexWindowTracker(native, new ManualWindowUpdateScheduler());

        tracker.Start();

        Assert.Equal((nint)61, tracker.CurrentSnapshot?.Handle);
    }
}
