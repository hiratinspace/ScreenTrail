namespace ScreenTrail.Service;

/// <summary>
/// Whether this is the build the installer packages (<c>-p:ScreenTrailInstaller=true</c>, Packaging.props
/// defines <c>SCREENTRAIL_INSTALLER</c>). Compiled in, so every copy of the release carries it: moving the
/// file out of Program Files, renaming it or stripping its signature does not make it a development build.
/// Development builds (dotnet build, the tests, run-local.ps1, the demo) are not the installer's.
/// </summary>
internal static class BuildKind
{
#if SCREENTRAIL_INSTALLER
    public static bool Installer => true;
#else
    public static bool Installer => false;
#endif
}
