using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sofol.App.Helpers;

/// <summary>
/// Hỗ trợ đưa ứng dụng vào chế độ tiết kiệm tài nguyên Windows 11 (Efficiency Mode / EcoQoS)
/// và thu gọn bộ nhớ Working Set khi chạy ngầm dưới khay hệ thống.
/// </summary>
public static class EfficiencyModeHelper
{
    private const int ProcessPowerThrottling = 4;
    private const uint ProcessPowerThrottlingCurrentVersion = 1;
    private const uint ProcessPowerThrottlingExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess,
        int processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation,
        int processInformationSize);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static void EnableBackgroundEfficiencyMode()
    {
        try
        {
            IntPtr hProcess = GetCurrentProcess();
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = ProcessPowerThrottlingCurrentVersion,
                ControlMask = ProcessPowerThrottlingExecutionSpeed,
                StateMask = ProcessPowerThrottlingExecutionSpeed
            };
            SetProcessInformation(hProcess, ProcessPowerThrottling, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());

            using Process current = Process.GetCurrentProcess();
            current.PriorityClass = ProcessPriorityClass.BelowNormal;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
                    EmptyWorkingSet(GetCurrentProcess());
                }
                catch
                {
                    // Bỏ qua lỗi giải phóng bộ nhớ nền
                }
            });
        }
        catch
        {
            // Bỏ qua nếu hệ điều hành hoặc quyền hiện tại không hỗ trợ
        }
    }

    public static void RestoreNormalMode()
    {
        try
        {
            IntPtr hProcess = GetCurrentProcess();
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = ProcessPowerThrottlingCurrentVersion,
                ControlMask = ProcessPowerThrottlingExecutionSpeed,
                StateMask = 0
            };
            SetProcessInformation(hProcess, ProcessPowerThrottling, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());

            using Process current = Process.GetCurrentProcess();
            current.PriorityClass = ProcessPriorityClass.Normal;
        }
        catch
        {
            // Bỏ qua lỗi khôi phục ưu tiên
        }
    }
}
