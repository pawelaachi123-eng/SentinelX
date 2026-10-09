using System.Diagnostics;
using System.Runtime.InteropServices;
namespace SentinelX.Services.Base;
internal static class ShutdownPrivilege
{
    [StructLayout(LayoutKind.Sequential)]private struct Luid{public uint Low;public int High;}
    [StructLayout(LayoutKind.Sequential)]private struct Privilege{public uint Count;public Luid Id;public uint Attributes;}
    public static bool Sleep()
    {
        if(!OpenProcessToken(Process.GetCurrentProcess().Handle,0x20|0x8,out var token))return false;
        try{
            if(!LookupPrivilegeValue(null,"SeShutdownPrivilege",out var id))return false;
            Privilege requested=new(){Count=1,Id=id,Attributes=2};
            if(!AdjustTokenPrivileges(token,false,ref requested,(uint)Marshal.SizeOf<Privilege>(),out var old,out _)||Marshal.GetLastWin32Error()!=0)return false;
            try{return SetSuspendState(false,false,false);}finally{AdjustTokenPrivileges(token,false,ref old,0,out _,out _);}
        }finally{CloseHandle(token);}
    }
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool LookupPrivilegeValue(string? system,string name,out Luid id);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool AdjustTokenPrivileges(IntPtr token,bool disable,ref Privilege desired,uint length,out Privilege previous,out uint needed);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr token);
    [DllImport("powrprof.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)]private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)]bool hibernate,[MarshalAs(UnmanagedType.U1)]bool force,[MarshalAs(UnmanagedType.U1)]bool disableWake);
}
