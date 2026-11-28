using ScreenTrail.Core.Ipc;

namespace ScreenTrail.Tests.Ipc;

/// <summary>
/// The service's second look at where a client is (security review B-01): Windows names the computer of a
/// client that came through the SMB server, loopback included, and answers ERROR_PIPE_LOCAL for one on
/// this machine. Only that answer lets the check go on.
/// </summary>
public sealed class PipeClientOriginTests
{
    private const int ErrorPipeLocal = 229;
    private const int ErrorAccessDenied = 5;
    private const int ErrorMoreData = 234;

    [Fact]
    public void AClientWithAComputerNameIsRefused() =>
        Assert.Equal(PipeClientOrigin.ClientNotLocal, PipeClientOrigin.Refusal(nameReturned: true, error: 0));

    [Fact]
    public void ANameReturnedIsRefusedWhateverTheLastErrorSays() =>
        Assert.Equal(PipeClientOrigin.ClientNotLocal, PipeClientOrigin.Refusal(nameReturned: true, error: ErrorPipeLocal));

    [Fact]
    public void ALocalClientGoesOnToTheProcessCheck() =>
        Assert.Null(PipeClientOrigin.Refusal(nameReturned: false, error: ErrorPipeLocal));

    [Theory]
    [InlineData(0)]
    [InlineData(ErrorAccessDenied)]
    [InlineData(ErrorMoreData)]
    public void AnyOtherAnswerIsRefused(int error) =>
        Assert.Equal(PipeClientOrigin.ClientNotLocal, PipeClientOrigin.Refusal(nameReturned: false, error));

    [Fact]
    public void TheReasonIsAFixedLabel() =>
        Assert.Equal("client_not_local", PipeClientOrigin.ClientNotLocal);
}
