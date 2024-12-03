using System.Runtime.InteropServices;

namespace YoutubeChatRead.PythonInterOp;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct _GROUP_AFFINITY
{
    public UIntPtr Mask;
    [MarshalAs(UnmanagedType.U2)] public ushort Group;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U2)]
    public ushort[] Reserved;
}