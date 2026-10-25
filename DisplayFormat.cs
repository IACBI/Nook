namespace Nook;

internal static class DisplayFormat
{
    /// <summary>Megabytes below one gigabyte, gigabytes from there on, with the app's invariant number format.</summary>
    public static string Bytes(double bytes)
    {
        const double megabyte = 1024d * 1024d;
        const double gigabyte = megabyte * 1024d;
        return bytes >= gigabyte ? $"{bytes / gigabyte:0.00} GB" : $"{bytes / megabyte:0.0} MB";
    }
}
