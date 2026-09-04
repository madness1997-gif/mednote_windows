using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using MedNote.Core;

namespace MedNote.Windows.App.Infrastructure;

internal sealed class WindowsCredentialStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const string TargetName = "MedNote.Reader/GoogleDriveOAuth";
    private readonly JsonSerializerOptions _json = JsonDefaults.Create();

    public GoogleOAuthCredential? Read()
    {
        if (!CredRead(TargetName, CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "Không đọc được thông tin đăng nhập Google từ Windows Credential Manager.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return JsonSerializer.Deserialize<GoogleOAuthCredential>(bytes, _json)
                ?? throw new InvalidDataException("Credential Google Drive rỗng.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Credential Google Drive bị hỏng.", exception);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(GoogleOAuthCredential value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _json);
        if (bytes.Length > 2560)
        {
            throw new InvalidDataException("Credential Google Drive vượt giới hạn Windows.");
        }

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var credential = new NativeCredential
            {
                Flags = 0,
                Type = CredentialTypeGeneric,
                TargetName = TargetName,
                Comment = IntPtr.Zero,
                LastWritten = default,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = handle.AddrOfPinnedObject(),
                Persist = CredentialPersistLocalMachine,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = IntPtr.Zero,
                UserName = value.ClientId,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Không lưu được thông tin đăng nhập Google vào Windows Credential Manager.");
            }
        }
        finally
        {
            Array.Clear(bytes);
            handle.Free();
        }
    }

    public void Delete()
    {
        if (!CredDelete(TargetName, CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "Không xóa được credential Google Drive.");
            }
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }
}
