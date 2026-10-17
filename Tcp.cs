using System.Net;
using System.Runtime.InteropServices;

namespace InternetSeparator;

// Established TCP connections with their owning process, IPv4 and IPv6 (GetExtendedTcpTable).
public static class Tcp
{
    [DllImport("iphlpapi.dll")]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, int reserved);

    const int AF_INET = 2, AF_INET6 = 23, OwnerPidConnections = 4, Established = 5;

    public static List<(IPAddress Local, int Pid)> Established4And6() => [.. Read(AF_INET), .. Read(AF_INET6)];

    static List<(IPAddress, int)> Read(int af)
    {
        var list = new List<(IPAddress, int)>();
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, OwnerPidConnections, 0);
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, af, OwnerPidConnections, 0) != 0) return list;
            var n = Marshal.ReadInt32(buf);
            for (var i = 0; i < n; i++)
            {
                if (af == AF_INET)
                {
                    // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid (6 DWORDs)
                    var row = buf + 4 + i * 24;
                    if (Marshal.ReadInt32(row) != Established) continue;
                    list.Add((new IPAddress((uint)Marshal.ReadInt32(row + 4)), Marshal.ReadInt32(row + 20)));
                }
                else
                {
                    // MIB_TCP6ROW_OWNER_PID: localAddr[16], scope, port, remoteAddr[16], scope, port, state, pid
                    var row = buf + 4 + i * 56;
                    if (Marshal.ReadInt32(row + 48) != Established) continue;
                    var addr = new byte[16];
                    Marshal.Copy(row, addr, 0, 16);
                    list.Add((new IPAddress(addr), Marshal.ReadInt32(row + 52)));
                }
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return list;
    }
}
