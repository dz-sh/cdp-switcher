using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CdpSwitcher.Core.Chrome;

public sealed class WindowsTcpPortOwnerResolver :
    ITcpPortOwnerResolver
{
    private const int AddressFamilyInternet = 2;
    private const uint ErrorInsufficientBuffer = 122;

    public int? FindListeningProcessId(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        return FindWindowsListeningProcessId(port);
    }

    [SupportedOSPlatform("windows")]
    private static int? FindWindowsListeningProcessId(int port)
    {
        var bufferSize = 0;
        var result = GetExtendedTcpTable(
            IntPtr.Zero,
            ref bufferSize,
            order: false,
            AddressFamilyInternet,
            TcpTableClass.OwnerPidListener,
            reserved: 0);
        if (result != ErrorInsufficientBuffer)
        {
            throw new Win32Exception((int)result);
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            result = GetExtendedTcpTable(
                buffer,
                ref bufferSize,
                order: false,
                AddressFamilyInternet,
                TcpTableClass.OwnerPidListener,
                reserved: 0);
            if (result != 0)
            {
                throw new Win32Exception((int)result);
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(uint));
            var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            int? owner = null;

            for (var index = 0; index < rowCount; index++)
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(
                    IntPtr.Add(rowPointer, index * rowSize));
                if (ReadPort(row.LocalPort) != port)
                {
                    continue;
                }

                var processId = checked((int)row.OwningProcessId);
                if (owner is not null && owner != processId)
                {
                    return null;
                }

                owner = processId;
            }

            return owner;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int ReadPort(uint networkOrderPort)
    {
        var bytes = BitConverter.GetBytes(networkOrderPort);
        return bytes[0] << 8 | bytes[1];
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        TcpTableClass tableClass,
        uint reserved);

    private enum TcpTableClass
    {
        OwnerPidListener = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningProcessId;
    }
}
