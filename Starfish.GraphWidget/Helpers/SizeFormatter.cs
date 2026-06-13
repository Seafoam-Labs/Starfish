namespace Starfish.Helpers;

public static class SizeFormatter
{
    public static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KiB", "MiB", "GiB", "TiB"];
        var i = 0;
        double dblSByte = bytes;
        while (i < suffixes.Length - 1 && bytes >= 1024)
        {
            dblSByte /= 1024;
            i++;
            bytes /= 1024;
        }

        return $"{dblSByte:0.##} {suffixes[i]}";
    }
}