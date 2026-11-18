using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MyLocalCloud.ShellIntegration.Interop;

/// <summary>
/// Definiciones P/Invoke de bajo nivel para la API nativa Windows Cloud Files (<c>cldapi.dll</c>).
/// Permite registrar raíces de sincronización (Sync Roots), crear marcadores de posición (placeholders)
/// y responder a las llamadas de hidratación bajo demanda (<c>CF_CALLBACK_TYPE_FETCH_DATA</c>) del Explorador de Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe class CfApiNativeMethods
{
    private const string CldApiDll = "cldapi.dll";

    public enum CF_HYDRATION_POLICY_PRIMARY : ushort
    {
        CF_HYDRATION_POLICY_PARTIAL = 0,
        CF_HYDRATION_POLICY_PROGRESSIVE = 1,
        CF_HYDRATION_POLICY_FULL = 2,
        CF_HYDRATION_POLICY_ALWAYS_FULL = 3
    }

    [Flags]
    public enum CF_HYDRATION_POLICY_MODIFIER : ushort
    {
        CF_HYDRATION_POLICY_MODIFIER_NONE = 0x0000,
        CF_HYDRATION_POLICY_MODIFIER_VALIDATION_REQUIRED = 0x0001,
        CF_HYDRATION_POLICY_MODIFIER_STREAMING_ALLOWED = 0x0002,
        CF_HYDRATION_POLICY_MODIFIER_AUTO_DEHYDRATION_ALLOWED = 0x0004
    }

    public enum CF_POPULATION_POLICY_PRIMARY : ushort
    {
        CF_POPULATION_POLICY_PARTIAL = 0,
        CF_POPULATION_POLICY_FULL = 2,
        CF_POPULATION_POLICY_ALWAYS_FULL = 3
    }

    [Flags]
    public enum CF_POPULATION_POLICY_MODIFIER : ushort
    {
        CF_POPULATION_POLICY_MODIFIER_NONE = 0x0000
    }

    [Flags]
    public enum CF_REGISTER_FLAGS : uint
    {
        CF_REGISTER_FLAG_NONE = 0x00000000,
        CF_REGISTER_FLAG_UPDATE = 0x00000001,
        CF_REGISTER_FLAG_DISABLE_ON_DEMAND_POPULATION_ON_ROOT = 0x00000002
    }

    [Flags]
    public enum CF_CONNECT_FLAGS : uint
    {
        CF_CONNECT_FLAG_NONE = 0x00000000,
        CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO = 0x00000002,
        CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH = 0x00000004
    }

    [Flags]
    public enum CF_PLACEHOLDER_CREATE_FLAGS : uint
    {
        CF_PLACEHOLDER_CREATE_FLAG_NONE = 0x00000000,
        CF_PLACEHOLDER_CREATE_FLAG_DISABLE_ON_DEMAND_POPULATION = 0x00000001,
        CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC = 0x00000002,
        CF_PLACEHOLDER_CREATE_FLAG_SUPERSEDE = 0x00000004
    }

    public enum CF_CALLBACK_TYPE : uint
    {
        CF_CALLBACK_TYPE_FETCH_DATA = 0,
        CF_CALLBACK_TYPE_VALIDATE_DATA = 1,
        CF_CALLBACK_TYPE_CANCEL_FETCH_DATA = 2,
        CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS = 3,
        CF_CALLBACK_TYPE_CANCEL_FETCH_PLACEHOLDERS = 4,
        CF_CALLBACK_TYPE_NOTIFY_FILE_OPEN_COMPLETION = 5,
        CF_CALLBACK_TYPE_NOTIFY_FILE_CLOSE_COMPLETION = 6,
        CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE = 7,
        CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE_COMPLETION = 8,
        CF_CALLBACK_TYPE_NOTIFY_DELETE = 9,
        CF_CALLBACK_TYPE_NOTIFY_DELETE_COMPLETION = 10,
        CF_CALLBACK_TYPE_NOTIFY_RENAME = 11,
        CF_CALLBACK_TYPE_NOTIFY_RENAME_COMPLETION = 12,
        CF_CALLBACK_TYPE_NONE = 0xffffffff
    }

    public enum CF_OPERATION_TYPE : uint
    {
        CF_OPERATION_TYPE_TRANSFER_DATA = 0,
        CF_OPERATION_TYPE_RETRIEVE_DATA = 1,
        CF_OPERATION_TYPE_ACK_DATA = 2,
        CF_OPERATION_TYPE_RESTART_HYDRATION = 3,
        CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS = 4,
        CF_OPERATION_TYPE_ACK_DEHYDRATE = 5,
        CF_OPERATION_TYPE_ACK_DELETE = 6,
        CF_OPERATION_TYPE_ACK_RENAME = 7
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_HYDRATION_POLICY
    {
        public CF_HYDRATION_POLICY_PRIMARY Primary;
        public CF_HYDRATION_POLICY_MODIFIER Modifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_POPULATION_POLICY
    {
        public CF_POPULATION_POLICY_PRIMARY Primary;
        public CF_POPULATION_POLICY_MODIFIER Modifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_SYNC_POLICIES
    {
        public uint StructSize;
        public CF_HYDRATION_POLICY Hydration;
        public CF_POPULATION_POLICY Population;
        public uint InSync;
        public uint HardLink;
        public uint PlaceholderManagement;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_SYNC_REGISTRATION
    {
        public uint StructSize;
        public nint ProviderName;
        public nint ProviderVersion;
        public nint SyncRootIdentity;
        public uint SyncRootIdentityLength;
        public nint FileIdentity;
        public uint FileIdentityLength;
        public Guid ProviderId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_FS_METADATA
    {
        public FILE_BASIC_INFO BasicInfo;
        public long FileSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_BASIC_INFO
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_PLACEHOLDER_CREATE_INFO
    {
        public nint RelativeFileName;
        public CF_FS_METADATA FsMetadata;
        public nint FileIdentity;
        public uint FileIdentityLength;
        public CF_PLACEHOLDER_CREATE_FLAGS Flags;
        public int Result;
        public long CreateUsn;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_INFO
    {
        public uint StructSize;
        public long ConnectionKey;
        public nint CallbackContext;
        public nint VolumeDosName;
        public nint VolumeGuidName;
        public nint VolumeSerialNumber;
        public long SyncRootFileId;
        public nint SyncRootIdentity;
        public uint SyncRootIdentityLength;
        public long FileId;
        public long FileSize;
        public nint FileIdentity;
        public uint FileIdentityLength;
        public nint NormalizedPath;
        public long TransferKey;
        public byte PriorityHint;
        public nint CorrelationVector;
        public nint ProcessInfo;
        public long RequestKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_PARAMETERS_FETCH_DATA
    {
        public uint ParamSize;
        public uint Flags;
        public long RequiredFileOffset;
        public long RequiredLength;
        public long OptionalFileOffset;
        public long OptionalLength;
        public long LastDehydrationTime;
        public uint LastDehydrationReason;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void CF_CALLBACK(
        in CF_CALLBACK_INFO callbackInfo,
        nint callbackParameters);

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_REGISTRATION
    {
        public CF_CALLBACK_TYPE Type;
        public nint Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_INFO
    {
        public uint StructSize;
        public CF_OPERATION_TYPE Type;
        public long ConnectionKey;
        public long TransferKey;
        public nint CorrelationVector;
        public nint SyncStatus;
        public long RequestKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_PARAMETERS_TRANSFER_DATA
    {
        public uint ParamSize;
        public uint Flags;
        public int CompletionStatus;
        public nint Buffer;
        public long Offset;
        public long Length;
    }

    [DllImport(CldApiDll, CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int CfRegisterSyncRoot(
        [MarshalAs(UnmanagedType.LPWStr)] string syncRootPath,
        in CF_SYNC_REGISTRATION registration,
        in CF_SYNC_POLICIES policies,
        CF_REGISTER_FLAGS registerFlags);

    [DllImport(CldApiDll, CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int CfUnregisterSyncRoot(
        [MarshalAs(UnmanagedType.LPWStr)] string syncRootPath);

    [DllImport(CldApiDll, CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int CfConnectSyncRoot(
        [MarshalAs(UnmanagedType.LPWStr)] string syncRootPath,
        [In] CF_CALLBACK_REGISTRATION[] callbackTable,
        nint callbackContext,
        CF_CONNECT_FLAGS connectFlags,
        out long connectionKey);

    [DllImport(CldApiDll, ExactSpelling = true)]
    public static extern int CfDisconnectSyncRoot(long connectionKey);

    [DllImport(CldApiDll, CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int CfCreatePlaceholders(
        [MarshalAs(UnmanagedType.LPWStr)] string baseDirectoryPath,
        [In, Out] CF_PLACEHOLDER_CREATE_INFO[] placeholderArray,
        uint placeholderCount,
        CF_PLACEHOLDER_CREATE_FLAGS createFlags,
        out uint entriesProcessed);

    [DllImport(CldApiDll, ExactSpelling = true)]
    public static extern int CfExecute(
        in CF_OPERATION_INFO opInfo,
        ref CF_OPERATION_PARAMETERS_TRANSFER_DATA opParams);
}
