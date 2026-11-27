using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Tests.Shell;

/// <summary>
/// ST-016, ST-071: the tray's quick menu to Spec §5 S1 (as built v0.6.10). The host draws these rows and
/// nothing else, so order, words, what is disabled and INV-4's rule for the pill are held here.
/// </summary>
public sealed class TrayMenuTests
{
    [Fact]
    public void WhileRecordingTheMenuRunsInTheSpecsOrder()
    {
        var rows = Rows(Capture(CaptureStates.Recording));

        Assert.Equal(
            [
                "Recording: ScreenConnect (12:41)",
                "Pause", "Stop and draft", "Discard session…",
                "-",
                "Open ScreenTrail", "All sessions", "Diagnostics", "Hide the recording pill (only between sessions)",
                "-",
                "Settings", "Set up ScreenTrail…", "Quit ScreenTrail",
            ],
            rows.Select(r => r.Kind == TrayRowKind.Separator ? "-" : r.Name));
        Assert.Equal(TrayRowKind.State, rows[0].Kind);
        Assert.False(rows[0].Enabled);
    }

    [Fact]
    public void TheCaptureControlsNameTheirHotkeys()
    {
        var rows = Rows(Capture(CaptureStates.Recording));

        Assert.Equal("Ctrl+Alt+P", Row(rows, TrayCommand.Pause).Hotkey);
        Assert.Equal("Ctrl+Alt+S", Row(rows, TrayCommand.Stop).Hotkey);
        Assert.Null(Row(rows, TrayCommand.Discard).Hotkey);
    }

    [Fact]
    public void PausedOffersResumeOnTheSameRow()
    {
        // The tray sent pause whatever the state, so a paused session could not be resumed from it.
        var rows = Rows(Capture(CaptureStates.Paused));

        Assert.Equal(TrayCommand.Resume, rows[1].Command);
        Assert.Equal("Resume", rows[1].Name);
        Assert.Equal("Ctrl+Alt+P", rows[1].Hotkey);
        Assert.True(rows[1].Enabled);
        Assert.DoesNotContain(rows, r => r.Command == TrayCommand.Pause);
    }

    [Theory]
    [InlineData(CaptureStates.Recording, true, true)]
    [InlineData(CaptureStates.Suppressed, true, true)]
    [InlineData(CaptureStates.Finalizing, false, false)]
    [InlineData("new_state", false, false)]
    public void EachControlIsEnabledOnlyWhereTheServiceAcceptsIt(string state, bool pause, bool stop)
    {
        var rows = Rows(Capture(state));

        Assert.Equal(pause, Row(rows, TrayCommand.Pause).Enabled);
        Assert.Equal(stop, Row(rows, TrayCommand.Stop).Enabled);
        Assert.True(Row(rows, TrayCommand.Discard).Enabled);
    }

    [Theory]
    [InlineData(CaptureStates.Idle, false)]
    [InlineData(CaptureStates.DraftPending, true)]
    [InlineData(CaptureStates.DraftReady, true)]
    [InlineData(CaptureStates.DraftFailed, true)]
    public void BetweenSessionsTheFirstBlockIsStartCapture(string state, bool discard)
    {
        // Spec §5 S1, as built v0.7.2: "When idle, the first block becomes Start capture Ctrl+Alt+R". It was
        // a line saying to press the hotkey, because the pipe had no start that knew the remote tool.
        var rows = Rows(Capture(state));

        Assert.Equal(TrayRowKind.Item, rows[1].Kind);
        Assert.Equal(TrayCommand.Start, rows[1].Command);
        Assert.Equal("Start capture", rows[1].Name);
        Assert.Equal('S', rows[1].AccessKey);
        Assert.Equal("Ctrl+Alt+R", rows[1].Hotkey);
        Assert.True(rows[1].Enabled);
        Assert.DoesNotContain(rows, r => r.Command is TrayCommand.Pause or TrayCommand.Resume or TrayCommand.Stop);
        Assert.Equal(discard, rows.Any(r => r.Command == TrayCommand.Discard));
        Assert.DoesNotContain(rows, r => r.Kind == TrayRowKind.Hint);
    }

    [Theory]
    [InlineData(CaptureStates.Recording)]
    [InlineData(CaptureStates.Paused)]
    [InlineData(CaptureStates.Suppressed)]
    [InlineData(CaptureStates.Finalizing)]
    [InlineData("new_state")]
    public void StartCaptureIsNotOfferedWhileASessionMayBeUnderWay(string state)
    {
        Assert.DoesNotContain(Rows(Capture(state)), r => r.Command == TrayCommand.Start);
    }

    [Theory]
    [InlineData(ServiceConnection.Connecting)]
    [InlineData(ServiceConnection.Unavailable)]
    public void StartCaptureIsNotOfferedIntoADroppedPipe(ServiceConnection connection)
    {
        // The last state heard was idle, but with the service not answering nothing that sends is offered.
        var rows = TrayMenu.For(new ShellSnapshot(connection, Capture(CaptureStates.Idle), ShellView.Review, null), pillHidden: false);

        Assert.DoesNotContain(rows, r => r.Command == TrayCommand.Start && r.Enabled);
    }

    [Fact]
    public void StartCaptureIsNamedForAScreenReaderAsTheOtherRowsAre()
    {
        // The name is the words without the marker and the hotkey is the description (TrayIconHost.Fill),
        // as Pause's is; the idle menu reads the state line, Start capture, then the second block.
        var rows = Rows(Capture(CaptureStates.Idle));
        var start = Row(rows, TrayCommand.Start);
        var pause = Row(Rows(Capture(CaptureStates.Recording)), TrayCommand.Pause);

        Assert.Equal("&Start capture", start.Text);
        Assert.Equal(pause.Kind, start.Kind);
        Assert.Equal(
            ["Start capture", "-", "Open ScreenTrail", "All sessions", "Diagnostics", "Hide the recording pill until the next session", "-", "Settings", "Set up ScreenTrail…", "Quit ScreenTrail"],
            rows.Skip(1).Select(r => r.Kind == TrayRowKind.Separator ? "-" : r.Name));
    }

    [Fact]
    public async Task AStartThatWorkedSaysNothingThePillSaysIt()
    {
        var sent = new List<StartCommand>();
        var notice = await TrayMenu.StartAsync(
            () =>
            {
                sent.Add(new StartCommand { RequestId = 1, Origin = StartOrigins.Tray });
                return Task.FromResult(new CommandResult { RequestId = 1, Ok = true });
            },
            () => Capture(CaptureStates.Recording));

        Assert.Null(notice);
        Assert.Single(sent);
    }

    [Fact]
    public async Task ARefusedStartSaysTheServicesSentenceInABalloonWithNothingToPress()
    {
        var notice = await TrayMenu.StartAsync(
            () => Task.FromResult(new CommandResult { RequestId = 1, Ok = false, Error = Core.Sessions.HandStart.NoRemoteSession }),
            () => Capture(CaptureStates.Idle));

        Assert.NotNull(notice);
        Assert.Equal(Core.Sessions.HandStart.NoRemoteSession, notice.Text);
        Assert.Equal(Core.Notifications.NotificationAction.None, notice.Action);
    }

    [Fact]
    public async Task ARefusalWithNoReasonStillSaysWhatToDo()
    {
        var notice = await TrayMenu.StartAsync(
            () => Task.FromResult(new CommandResult { RequestId = 1, Ok = false }),
            () => Capture(CaptureStates.DraftReady));

        Assert.Equal(TrayMenu.StartRefused, notice?.Text);
    }

    [Theory]
    [InlineData(CaptureStates.Recording)]
    [InlineData(CaptureStates.Paused)]
    [InlineData(CaptureStates.Suppressed)]
    [InlineData(CaptureStates.Finalizing)]
    public async Task ARefusalOnceASessionIsUnderWayIsNotABalloonOverTheRecording(string state)
    {
        // A start from elsewhere (the chord, auto-start) won the race. Spec §4: nothing interrupts a
        // recording, and the pill already says a session is under way.
        var notice = await TrayMenu.StartAsync(
            () => Task.FromResult(new CommandResult { RequestId = 1, Ok = false, Error = Core.Sessions.HandStart.Busy }),
            () => Capture(state));

        Assert.Null(notice);
    }

    [Theory]
    [InlineData(ServiceConnection.Connecting)]
    [InlineData(ServiceConnection.Unavailable)]
    public void WithNoServiceNothingThatSendsIsEnabled(ServiceConnection connection)
    {
        // The UI does not know whether a session is under way, so the controls stay where they are and
        // cannot be chosen: a command into a dropped pipe would look done and not be.
        var rows = TrayMenu.For(new ShellSnapshot(connection, Capture(CaptureStates.Recording), ShellView.Review, null), pillHidden: false);

        Assert.False(Row(rows, TrayCommand.Pause).Enabled);
        Assert.False(Row(rows, TrayCommand.Stop).Enabled);
        Assert.False(Row(rows, TrayCommand.Discard).Enabled);
        Assert.False(Row(rows, TrayCommand.HidePill).Enabled);
        Assert.True(Row(rows, TrayCommand.Quit).Enabled);
        Assert.True(Row(rows, TrayCommand.Diagnostics).Enabled);
    }

    [Fact]
    public void TheHeaderSaysTheStateAloneAndWhatWaitsOnItsSecondLine()
    {
        // Review finding C-T5: "Not capturing · 2 drafts ready · 1 publish not confirmed" was one dotted
        // run-on that set the menu's width and buried the state. Now the state, then the counts below it.
        var rows = TrayMenu.For(new ShellSnapshot(ServiceConnection.Connected, new CaptureStateSnapshot { State = CaptureStates.Idle, DraftsReady = 2, PublishesUnconfirmed = 1 }, ShellView.Review, null), pillHidden: false);

        Assert.Equal("Not recording", rows[0].Name);
        Assert.Equal("2 drafts ready, 1 publish not confirmed", rows[0].Detail);
        Assert.Equal("Not recording. 2 drafts ready, 1 publish not confirmed", rows[0].Spoken);

        // While a session is captured there is no second line (INV-4), and an item has none.
        var recording = Rows(Capture(CaptureStates.Recording) with { DraftsReady = 2 });
        Assert.Null(recording[0].Detail);
        Assert.Equal(recording[0].Name, recording[0].Spoken);
        Assert.All(rows.Skip(1), r => Assert.Null(r.Detail));
    }

    [Fact]
    public void WithTheServiceNotRunningTheFirstRowStartsIt()
    {
        // Review finding E-13: the main window's banner offers Start; the tray, the only thing on screen
        // when the window is closed, now does too, as the first thing under the state line.
        var rows = TrayMenu.For(new ShellSnapshot(ServiceConnection.Unavailable, null, ShellView.Review, null), pillHidden: false);

        Assert.Equal(TrayCommand.StartService, rows[1].Command);
        Assert.Equal("Start the capture service", rows[1].Name);
        Assert.Equal('T', rows[1].AccessKey);
        Assert.True(rows[1].Enabled);
        Assert.DoesNotContain(rows.Where(r => r.Kind == TrayRowKind.Item && r != rows[1]), r => r.AccessKey == 'T');
    }

    [Fact]
    public void WhileTheFirstConnectionIsBeingMadeNothingOffersToStartTheService()
    {
        var connecting = TrayMenu.For(new ShellSnapshot(ServiceConnection.Connecting, null, ShellView.Review, null), pillHidden: false);
        var connected = Rows(Capture(CaptureStates.Recording));

        Assert.DoesNotContain(connecting, r => r.Command == TrayCommand.StartService);
        Assert.DoesNotContain(connected, r => r.Command == TrayCommand.StartService);
    }

    [Fact]
    public void StartingTheServiceRunsItFromBesideTheApp()
    {
        var started = new List<string>();
        var folder = Path.Combine("C:", "Program Files", "ScreenTrail");

        Assert.Null(CaptureServiceStart.Run(folder, _ => true, started.Add));
        Assert.Equal([Path.Combine(folder, "ScreenTrail.Service.exe")], started);

        // Not there, or Windows would not run it: said in words, and never thrown at the technician.
        Assert.Equal(CaptureServiceStart.Failed, CaptureServiceStart.Run(folder, _ => false, started.Add));
        Assert.Equal(CaptureServiceStart.Failed, CaptureServiceStart.Run(folder, _ => true, _ => throw new System.ComponentModel.Win32Exception(5)));
        Assert.Single(started);
    }

    [Theory]
    [InlineData(ServiceConnection.Unavailable, "Capture service not running")]
    [InlineData(ServiceConnection.Connecting, "Connecting to the capture service")]
    public void WithNoServiceTheStateLineSaysWhyOnceAndTheRowsAreDisabled(ServiceConnection connection, string why)
    {
        // Review finding E-13: the state line says why, once. v0.6.12 had every disabled row repeat it in
        // brackets, four copies of one reason down the menu.
        var rows = TrayMenu.For(new ShellSnapshot(connection, null, ShellView.Review, null), pillHidden: false);

        Assert.Equal(why, rows[0].Name);
        Assert.Equal("Pause", Row(rows, TrayCommand.Pause).Name);
        Assert.Equal("Stop and draft", Row(rows, TrayCommand.Stop).Name);
        Assert.Equal("Discard session…", Row(rows, TrayCommand.Discard).Name);
        Assert.Equal("Hide the recording pill", Row(rows, TrayCommand.HidePill).Name);
        Assert.DoesNotContain(rows, r => r.Name.Contains('(', StringComparison.Ordinal));
        Assert.All(
            rows.Where(r => r.Command is TrayCommand.Pause or TrayCommand.Stop or TrayCommand.Discard or TrayCommand.HidePill),
            r => Assert.False(r.Enabled));

        // Each keeps its access key and the hotkey it would take, so the row is still recognisably itself.
        Assert.Equal('P', Row(rows, TrayCommand.Pause).AccessKey);
        Assert.Equal('S', Row(rows, TrayCommand.Stop).AccessKey);
        Assert.Equal('D', Row(rows, TrayCommand.Discard).AccessKey);
        Assert.Equal("Ctrl+Alt+P", Row(rows, TrayCommand.Pause).Hotkey);
    }

    [Fact]
    public void WithTheServiceAnsweringTheRowsKeepTheirPlainWords()
    {
        var rows = Rows(Capture(CaptureStates.Recording));

        Assert.Equal("Pause", Row(rows, TrayCommand.Pause).Name);
        Assert.Equal("Discard session…", Row(rows, TrayCommand.Discard).Name);
        Assert.DoesNotContain(rows, r => r.Name.Contains("capture service", StringComparison.Ordinal) && r.Kind == TrayRowKind.Item);
    }

    [Theory]
    [InlineData(CaptureStates.Recording)]
    [InlineData(CaptureStates.Paused)]
    [InlineData(CaptureStates.Suppressed)]
    [InlineData(CaptureStates.Finalizing)]
    [InlineData("new_state")]
    public void ThePillCannotBePutAwayWhileASessionMayBeUnderWay(string state)
    {
        // INV-4, owner decision 2026-10-25: tray-only between sessions only. The item used to be offered
        // in every state, and during a recording it hid the pill the moment the session ended instead.
        foreach (var hidden in new[] { false, true })
        {
            var row = Row(Rows(Capture(state), hidden), TrayCommand.HidePill);
            Assert.False(row.Enabled);
            Assert.Equal("Hide the recording pill (only between sessions)", row.Name);
        }
    }

    [Fact]
    public void BetweenSessionsThePillCanBePutAwayAndBroughtBack()
    {
        var shown = Row(Rows(Capture(CaptureStates.Idle)), TrayCommand.HidePill);
        Assert.True(shown.Enabled);
        Assert.Equal("Hide the recording pill until the next session", shown.Name);

        var hidden = Rows(Capture(CaptureStates.Idle), pillHidden: true);
        Assert.DoesNotContain(hidden, r => r.Command == TrayCommand.HidePill);
        Assert.True(Row(hidden, TrayCommand.ShowPill).Enabled);
        Assert.Equal("Show the recording pill", Row(hidden, TrayCommand.ShowPill).Name);
    }

    [Theory]
    [InlineData(0, "Open ScreenTrail")]
    [InlineData(1, "Open next draft (1 ready)")]
    [InlineData(2, "Open next draft (2 ready)")]
    public void OpenLastDraftCountsTheDraftsReady(int ready, string name)
    {
        var rows = Rows(Capture(CaptureStates.Idle) with { DraftsReady = ready });

        Assert.Equal(name, Row(rows, TrayCommand.OpenLastDraft).Name);
    }

    [Fact]
    public void EveryRowThatCanBeChosenHasItsOwnAccessKey()
    {
        // Win+B, the menu key, then a letter: the keyboard path through the menu without the arrows.
        foreach (var state in new[] { CaptureStates.Recording, CaptureStates.Paused, CaptureStates.Idle, CaptureStates.DraftReady })
        {
            foreach (var hidden in new[] { false, true })
            {
                var items = Rows(Capture(state) with { DraftsReady = 2 }, hidden).Where(r => r.Kind == TrayRowKind.Item).ToList();
                Assert.All(items, r => Assert.NotNull(r.AccessKey));
                Assert.Equal(items.Count, items.Select(r => r.AccessKey).Distinct().Count());
            }
        }

        Assert.All(Rows(Capture(CaptureStates.Idle)).Where(r => r.Kind is TrayRowKind.State or TrayRowKind.Hint), r => Assert.Null(r.AccessKey));
    }

    [Fact]
    public void NoRowCarriesAnEmDashOrAnExclamationMark()
    {
        foreach (var state in new[] { CaptureStates.Recording, CaptureStates.Paused, CaptureStates.Idle, CaptureStates.DraftReady, CaptureStates.Finalizing })
        {
            Assert.All(Rows(Capture(state)), r =>
            {
                Assert.DoesNotContain("—", r.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("!", r.Text, StringComparison.Ordinal);
            });
        }
    }

    [Theory]
    [InlineData(CaptureStates.Recording, true)]
    [InlineData(CaptureStates.Paused, true)]
    [InlineData(CaptureStates.Suppressed, true)]
    [InlineData(CaptureStates.Finalizing, false)]
    [InlineData(CaptureStates.DraftReady, false)]
    [InlineData(CaptureStates.Idle, false)]
    public void DiscardStopsCaptureBeforeItAsksWhileASessionIsBeingCaptured(string state, bool stopsFirst)
    {
        // Spec v0.4.1 Q3 and §4: no modal while a session is recording, so the session stops first.
        Assert.Equal(stopsFirst, TrayMenu.DiscardStopsFirst(Capture(state)));
    }

    [Theory]
    [InlineData(CaptureStates.Recording, "stop,ask")]
    [InlineData(CaptureStates.Paused, "stop,ask")]
    [InlineData(CaptureStates.DraftReady, "ask")]
    public async Task TheConfirmationIsAskedOnlyOnceTheStopIsAnswered(string state, string order)
    {
        var said = new List<string>();
        var stopped = new TaskCompletionSource();

        var discard = TrayMenu.DiscardAsync(
            Capture(state),
            async () =>
            {
                said.Add("stop");
                await stopped.Task;
            },
            () =>
            {
                said.Add("ask");
                return Task.CompletedTask;
            });

        // While the stop is unanswered, nothing is asked: the session may still be recording.
        if (order.StartsWith("stop", StringComparison.Ordinal))
        {
            Assert.Equal(["stop"], said);
        }
        stopped.SetResult();
        await discard;

        Assert.Equal(order, string.Join(',', said));
    }

    private static IReadOnlyList<TrayRow> Rows(CaptureStateSnapshot capture, bool pillHidden = false) =>
        TrayMenu.For(new ShellSnapshot(ServiceConnection.Connected, capture, ShellView.Review, null), pillHidden);

    private static TrayRow Row(IReadOnlyList<TrayRow> rows, TrayCommand command) => rows.Single(r => r.Command == command);

    private static CaptureStateSnapshot Capture(string state) => new()
    {
        State = state,
        SessionId = "s1",
        RemoteTool = "screenconnect",
        ElapsedMs = (12 * 60 * 1000) + 41_000,
    };
}
