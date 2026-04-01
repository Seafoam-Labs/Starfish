using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Starfish.References;

public static class GlLoader
{
    public static GL GetGl() => GL.GetApi(GetProcAddress);

    private static nint GetProcAddress(string proc) =>
        eglGetProcAddress(proc);

    [DllImport("libEGL.so.1")]
    private static extern nint eglGetProcAddress(string name);
}