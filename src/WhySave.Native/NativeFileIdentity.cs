using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WhySave.Native;

public sealed record FileNtfsIdentity(long VolumeSerial, long NtfsFileId);

public static class NativeFileIdentity
{
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    private const uint DRIVE_UNKNOWN = 0;
    private const uint DRIVE_NO_ROOT_DIR = 1;
    private const uint DRIVE_REMOTE = 4;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string lpRootPathName);

    public static FileNtfsIdentity? GetFileIdentity(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (!File.Exists(path))
            return null;

        if (IsNetworkPath(path))
            return null;

        using var handle = CreateFileW(
            path,
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle.IsInvalid)
            return null;

        if (!GetFileInformationByHandle(handle, out var info))
            return null;

        if (info.dwVolumeSerialNumber == 0)
            return null;

        var fileId = ((long)info.nFileIndexHigh << 32) | info.nFileIndexLow;
        return new FileNtfsIdentity(info.dwVolumeSerialNumber, fileId);
    }

    private static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            return true;

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.Length < 2)
            return false;

        var driveType = GetDriveTypeW(root);
        return driveType == DRIVE_REMOTE;
    }
}
