using System.Runtime.InteropServices;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// Windows의 앱별 출력 장치 정책을 변경하는 내부 래퍼입니다.
/// </summary>
internal static class ApplicationAudioOutputPolicy
{
    // 이 클래스는 공개 API가 없는 Windows.Media.Internal.AudioPolicyConfig를 직접 호출합니다.
    // .NET COM/WinRT RCW 변환 경로는 이 내부 인터페이스에서 막히므로 원시 vtable 포인터와 HSTRING을 사용합니다.
    // 시그니처와 vtable slot은 Windows 내부 구현에 의존하므로 호출자는 반드시 워커 프로세스로 격리해야 합니다.
    private const string AudioPolicyConfigClassName = "Windows.Media.Internal.AudioPolicyConfig";
    private const int SetPersistedDefaultAudioEndpointVtableSlot = 25;
    private static readonly Guid AudioPolicyConfigFactoryIdFor21H2 = new("ab3d4648-e242-459f-b02f-541c70306324");
    private static readonly Guid AudioPolicyConfigFactoryIdForDownlevel = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");

    /// <summary>
    /// 지정한 프로세스의 앱별 기본 출력 장치를 대상 렌더 장치로 변경합니다.
    /// </summary>
    public static void SetPersistedDefaultOutputDevice(uint processId, string targetDeviceId)
    {
        if (processId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), Loc.T("프로세스 ID가 0인 세션은 출력 장치를 변경할 수 없습니다."));
        }

        if (string.IsNullOrWhiteSpace(targetDeviceId))
        {
            throw new ArgumentException(Loc.T("대상 출력 장치 ID가 비어 있습니다."), nameof(targetDeviceId));
        }

        var policyEndpointId = AudioPolicyEndpointId.ToRenderPolicyEndpointId(targetDeviceId);

        // Windows 볼륨 믹서의 "앱별 출력 장치"는 role별로 저장되므로 세 역할을 모두 같은 장치로 맞춥니다.
        ApplyPolicyToRoles(
            role => ApplyProcessPolicyRole(processId, policyEndpointId, role),
            roleSuccessLog: role =>
                $"앱별 출력 정책 변경 성공 processId={processId} role={role} endpointAbi=HString endpointIdKind=PackedRender targetDeviceId={targetDeviceId}",
            roleFailureLog: (role, hresult) =>
                $"앱별 출력 정책 변경 실패 processId={processId} role={role} endpointAbi=HString endpointIdKind=PackedRender targetDeviceId={targetDeviceId} hresult={hresult}",
            partialFailureLog: (successfulRoles, failedRoles) =>
                $"앱별 출력 정책 일부 role 실패 processId={processId} endpointAbi=HString endpointIdKind=PackedRender successfulRoles=[{string.Join(", ", successfulRoles)}] failedRoles=[{string.Join(", ", failedRoles)}]");
    }

    /// <summary>
    /// 지정한 프로세스의 앱별 출력 지정을 지워 Windows 기본 출력 장치를 따르게 합니다.
    /// </summary>
    public static void ClearPersistedDefaultOutputDevice(uint processId)
    {
        if (processId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), Loc.T("프로세스 ID가 0인 세션은 출력 장치를 변경할 수 없습니다."));
        }

        // 빈 HSTRING(null)을 넘기면 Windows 볼륨 믹서의 "기본값"과 같은 상태로 돌아갑니다.
        ApplyPolicyToRoles(
            role =>
            {
                using var factory = CreateFactory();
                return factory.SetPersistedDefaultAudioEndpointForProcessHString(
                    processId,
                    AudioDataFlow.Render,
                    role,
                    IntPtr.Zero);
            },
            roleSuccessLog: role =>
                $"앱별 출력 정책 해제 성공 processId={processId} role={role}",
            roleFailureLog: (role, hresult) =>
                $"앱별 출력 정책 해제 실패 processId={processId} role={role} hresult={hresult}",
            partialFailureLog: (successfulRoles, failedRoles) =>
                $"앱별 출력 정책 해제 일부 role 실패 processId={processId} successfulRoles=[{string.Join(", ", successfulRoles)}] failedRoles=[{string.Join(", ", failedRoles)}]");
    }

    private static uint ApplyProcessPolicyRole(
        uint processId,
        string policyEndpointId,
        AudioRole role)
    {
        using var factory = CreateFactory();
        var endpointId = IntPtr.Zero;
        try
        {
            // 이 내부 메서드는 LPWSTR가 아니라 HSTRING endpoint ID로 호출할 때 정상 동작하는 것으로 확인됐습니다.
            ThrowIfFailed(WindowsCreateString(policyEndpointId, (uint)policyEndpointId.Length, out endpointId));
            return factory.SetPersistedDefaultAudioEndpointForProcessHString(
                processId,
                AudioDataFlow.Render,
                role,
                endpointId);
        }
        finally
        {
            if (endpointId != IntPtr.Zero)
            {
                WindowsDeleteString(endpointId);
            }
        }
    }

    private static void ApplyPolicyToRoles(
        Func<AudioRole, uint> applyRole,
        Func<AudioRole, string> roleSuccessLog,
        Func<AudioRole, string, string> roleFailureLog,
        Func<IReadOnlyCollection<AudioRole>, IReadOnlyCollection<string>, string> partialFailureLog)
    {
        var successfulRoles = new List<AudioRole>();
        var failedRoles = new List<string>();
        foreach (var role in GetPolicyRoles())
        {
            var hresult = applyRole(role);
            if (IsSuccess(hresult))
            {
                successfulRoles.Add(role);
                AppLog.Info(
                    "ApplicationAudioOutputPolicy",
                    roleSuccessLog(role));
                continue;
            }

            var formattedResult = FormatHResult(hresult);
            failedRoles.Add($"{role}:{formattedResult}");
            AppLog.Warn(
                "ApplicationAudioOutputPolicy",
                roleFailureLog(role, formattedResult));
        }

        if (successfulRoles.Count == 0)
        {
            throw new InvalidOperationException(
                $"앱별 출력 장치 정책 변경에 실패했습니다. roleResults=[{string.Join(", ", failedRoles)}]");
        }

        if (failedRoles.Count > 0)
        {
            AppLog.Warn(
                "ApplicationAudioOutputPolicy",
                partialFailureLog(successfulRoles, failedRoles));
        }
    }

    private static AudioPolicyConfigFactory CreateFactory()
    {
        try
        {
            var iid = AudioPolicyConfigFactoryIdFor21H2;
            return GetActivationFactory(iid);
        }
        catch (COMException exception)
        {
            AppLog.Warn(
                "ApplicationAudioOutputPolicy",
                "21H2 AudioPolicyConfigFactory 활성화 실패, downlevel 팩토리로 재시도",
                exception);
            var iid = AudioPolicyConfigFactoryIdForDownlevel;
            return GetActivationFactory(iid);
        }
    }

    private static AudioPolicyConfigFactory GetActivationFactory(Guid iid)
    {
        var className = IntPtr.Zero;
        var factory = IntPtr.Zero;
        try
        {
            ThrowIfFailed(WindowsCreateString(AudioPolicyConfigClassName, (uint)AudioPolicyConfigClassName.Length, out className));
            ThrowIfFailed(RoGetActivationFactory(className, ref iid, out factory));
            var result = new AudioPolicyConfigFactory(factory);
            factory = IntPtr.Zero;
            return result;
        }
        finally
        {
            if (factory != IntPtr.Zero)
            {
                Marshal.Release(factory);
            }

            if (className != IntPtr.Zero)
            {
                WindowsDeleteString(className);
            }
        }
    }

    private static IEnumerable<AudioRole> GetPolicyRoles()
    {
        yield return AudioRole.Multimedia;
        yield return AudioRole.Console;
        yield return AudioRole.Communications;
    }

    private static bool IsSuccess(uint hresult)
    {
        return hresult < 0x80000000;
    }

    private static string FormatHResult(uint hresult)
    {
        return $"0x{hresult:X8}";
    }

    private static void ThrowIfFailed(uint hresult)
    {
        if (hresult >= 0x80000000)
        {
            Marshal.ThrowExceptionForHR(unchecked((int)hresult));
        }
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    [DllImport("combase.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        ref Guid iid,
        out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    private enum AudioDataFlow
    {
        Render = 0,
        Capture = 1,
        All = 2
    }

    private enum AudioRole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }

    private sealed class AudioPolicyConfigFactory : IDisposable
    {
        private readonly SetPersistedDefaultAudioEndpointForProcessHStringDelegate _setPersistedDefaultAudioEndpointForProcessHString;
        private IntPtr _nativePointer;

        public AudioPolicyConfigFactory(IntPtr nativePointer)
        {
            if (nativePointer == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(nativePointer));
            }

            _nativePointer = nativePointer;
            var vtable = Marshal.ReadIntPtr(_nativePointer);
            // IInspectable 기본 메서드 뒤쪽에 있는 내부 메서드를 slot 번호로 직접 가져옵니다.
            var methodPointer = Marshal.ReadIntPtr(
                vtable,
                SetPersistedDefaultAudioEndpointVtableSlot * IntPtr.Size);
            _setPersistedDefaultAudioEndpointForProcessHString =
                Marshal.GetDelegateForFunctionPointer<SetPersistedDefaultAudioEndpointForProcessHStringDelegate>(methodPointer);
        }

        public uint SetPersistedDefaultAudioEndpointForProcessHString(
            uint processId,
            AudioDataFlow flow,
            AudioRole role,
            IntPtr endpointId)
        {
            ObjectDisposedException.ThrowIf(_nativePointer == IntPtr.Zero, this);
            return _setPersistedDefaultAudioEndpointForProcessHString(_nativePointer, processId, flow, role, endpointId);
        }

        public void Dispose()
        {
            if (_nativePointer == IntPtr.Zero)
            {
                return;
            }

            Marshal.Release(_nativePointer);
            _nativePointer = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint SetPersistedDefaultAudioEndpointForProcessHStringDelegate(
        IntPtr self,
        uint processId,
        AudioDataFlow flow,
        AudioRole role,
        IntPtr endpointId);

}
