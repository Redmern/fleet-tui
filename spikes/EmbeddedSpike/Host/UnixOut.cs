using System.Runtime.InteropServices;

namespace EmbeddedSpike.Host;

internal static unsafe partial class UnixOut
{
    public static void Write(byte[] bytes)
    {
        fixed (byte* p = bytes)
        {
            var offset = 0;
            while (offset < bytes.Length)
            {
                var n = write(1, p + offset, (nuint)(bytes.Length - offset));
                if (n <= 0)
                {
                    if (n < 0 && Marshal.GetLastPInvokeError() == 4)
                    {
                        continue;
                    }

                    return;
                }

                offset += (int)n;
            }
        }
    }

    [LibraryImport("libc.so.6", SetLastError = true)]
    private static partial nint write(int fd, byte* buf, nuint count);
}
