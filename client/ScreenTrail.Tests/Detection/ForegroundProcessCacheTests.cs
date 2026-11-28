using ScreenTrail.Core.Detection;

namespace ScreenTrail.Tests.Detection;

/// <summary>
/// The foreground watcher asks a process's name and elevation once and keeps the answer (I-02, INV-5,
/// INV-6). Kept by process id alone, the answer outlived the process: when it exited and Windows gave the
/// same id to the next program to come to the front, that program was judged by the old one's name and
/// elevation, so a password manager or an elevated console could be recorded as PowerShell. The answer is
/// now kept only while a handle to the process is held open, which stops Windows giving the id to anything
/// else, and asked again after nothing was in front.
/// </summary>
public sealed class ForegroundProcessCacheTests
{
    [Fact]
    public void TheSameProcessIsAskedAboutOnceWhileItsHandleIsHeld()
    {
        var processes = new FakeProcesses { [7] = ("powershell", false) };
        var cache = processes.Cache();

        Assert.Equal(("powershell", false), cache.Lookup(7));
        Assert.Equal(("powershell", false), cache.Lookup(7));

        Assert.Equal(1, processes.Opened);
        Assert.Equal(1, processes.Read);
        Assert.Empty(processes.Closed);
    }

    [Fact]
    public void AfterNothingWasInFrontTheSameIdIsAskedAboutAgain()
    {
        // PowerShell exits, nothing is in front for a moment, and the next program Windows starts is given
        // its id: a password manager, or a console run as administrator.
        var processes = new FakeProcesses { [7] = ("powershell", false) };
        var cache = processes.Cache();
        _ = cache.Lookup(7);

        cache.Forget();
        processes[7] = ("keepass", true);

        Assert.Equal(("keepass", true), cache.Lookup(7));
        Assert.Equal(2, processes.Opened);
        Assert.Single(processes.Closed);
    }

    [Fact]
    public void AProcessThatCouldNotBeOpenedIsAskedAboutEveryTime()
    {
        // With no handle held, nothing keeps the id from going to another process, so the answer is not
        // kept. Read without a handle, a process is reported as elevated, the closed answer.
        var processes = new FakeProcesses { [9] = ("protected", true) };
        processes.Unopenable.Add(9);
        var cache = processes.Cache();

        _ = cache.Lookup(9);
        processes[9] = ("keepass", true);
        Assert.Equal(("keepass", true), cache.Lookup(9));
        _ = cache.Lookup(9);

        Assert.Equal(3, processes.Read);
        Assert.Empty(processes.Closed);
    }

    [Fact]
    public void AnotherProcessInFrontLetsGoOfTheLastOnesHandle()
    {
        var processes = new FakeProcesses { [7] = ("powershell", false), [8] = ("mmc", true) };
        var cache = processes.Cache();

        _ = cache.Lookup(7);
        Assert.Equal(("mmc", true), cache.Lookup(8));
        Assert.Equal([processes.Handles[0]], processes.Closed);

        cache.Forget();
        Assert.Equal(processes.Handles, processes.Closed);
    }

    [Fact]
    public void EveryHandleOpenedIsClosedOnceAndNoMore()
    {
        var processes = new FakeProcesses { [7] = ("powershell", false), [8] = ("mmc", false) };
        var cache = processes.Cache();

        _ = cache.Lookup(7);
        _ = cache.Lookup(8);
        _ = cache.Lookup(7);
        cache.Forget();
        cache.Forget();

        Assert.Equal(processes.Handles, processes.Closed);
    }

    /// <summary>Processes by id, with handles that can be counted: each open gives a new one.</summary>
    private sealed class FakeProcesses : Dictionary<uint, (string? Name, bool Elevated)>
    {
        public HashSet<uint> Unopenable { get; } = [];

        /// <summary>Every handle opened, in order.</summary>
        public List<nint> Handles { get; } = [];

        public int Opened => Handles.Count;

        public int Read { get; private set; }

        public List<nint> Closed { get; } = [];

        public ForegroundProcessCache Cache() => new(Open, ReadFrom, Closed.Add);

        private nint Open(uint processId)
        {
            if (Unopenable.Contains(processId))
            {
                return 0;
            }

            var handle = (nint)((processId * 100) + Handles.Count + 1);
            Handles.Add(handle);
            return handle;
        }

        private (string? Name, bool Elevated) ReadFrom(uint processId, nint handle)
        {
            Read++;
            return this[processId];
        }
    }
}
