using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Starfish.References;

public static class GlLoader
{
    private static readonly nint Lib = LoadLib();

    private static nint LoadLib()
    {
        try
        {
            return NativeLibrary.Load("libGL.so.1");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG_LOG] Failed to load libGL.so.1: {ex.Message}");
            return 0;
        }
    }

    public static GL GetGl() => GL.GetApi(GetProcAddress);

    private static nint GetProcAddress(string proc)
    {
        nint addr = 0;
        try
        {
            addr = eglGetProcAddress(proc);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG_LOG] eglGetProcAddress failed for {proc}: {ex.Message}");
        }

        if (addr != 0) return addr;

        if (Lib != 0 && NativeLibrary.TryGetExport(Lib, proc, out addr))
        {
            return addr;
        }

        return 0;
    }

    [DllImport("libEGL.so.1")]
    private static extern nint eglGetProcAddress(string name);
}