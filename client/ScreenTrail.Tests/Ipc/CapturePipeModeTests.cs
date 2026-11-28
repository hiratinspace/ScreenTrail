using System.IO.Pipes;
using ScreenTrail.Core.Ipc;

namespace ScreenTrail.Tests.Ipc;

/// <summary>
/// The modes the service's pipe is created with (security review B-01). The pipe refuses every client
/// that reaches it through the SMB server, loopback included, and is otherwise made exactly as the
/// framework made it before: duplex, overlapped, a byte stream, the first instance claiming the name.
/// </summary>
public sealed class CapturePipeModeTests
{
    private const uint RejectRemoteClients = 0x8;

    [Fact]
    public void TheCapturePipeRefusesRemoteClients() =>
        Assert.Equal(RejectRemoteClients, CapturePipeMode.PipeMode & RejectRemoteClients);

    [Fact]
    public void TheCapturePipeIsStillAByteStreamThatWaits()
    {
        // PIPE_TYPE_MESSAGE (4), PIPE_READMODE_MESSAGE (2) and PIPE_NOWAIT (1) all stay clear: the framework
        // asked for PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, which is 0.
        Assert.Equal(0u, CapturePipeMode.PipeMode & ~RejectRemoteClients);
    }

    [Fact]
    public void OnlyTheFirstInstanceClaimsTheName()
    {
        Assert.Equal((uint)PipeOptions.FirstPipeInstance, CapturePipeMode.OpenMode(firstInstance: true) & (uint)PipeOptions.FirstPipeInstance);
        Assert.Equal(0u, CapturePipeMode.OpenMode(firstInstance: false) & (uint)PipeOptions.FirstPipeInstance);
    }

    [Fact]
    public void TheOpenModeIsWhatTheFrameworkWasGivenBefore()
    {
        // NamedPipeServerStreamAcl.Create passed the direction and the options straight through as the open
        // mode, so these are the values the service's pipe was made with before it was made directly.
        Assert.Equal(
            (uint)PipeDirection.InOut | (uint)PipeOptions.Asynchronous | (uint)PipeOptions.FirstPipeInstance,
            CapturePipeMode.OpenMode(firstInstance: true));
        Assert.Equal(
            (uint)PipeDirection.InOut | (uint)PipeOptions.Asynchronous,
            CapturePipeMode.OpenMode(firstInstance: false));
    }
}
