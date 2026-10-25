using DesktopAudioController.Models;

namespace DesktopAudioController.Services;

/// <summary>
/// 대체 장치 순서에서 고른 결과입니다. UsedFallback 이면 첫 장치가 없어 다른 장치를 골랐다는 뜻입니다.
/// </summary>
internal sealed record ProfileDeviceChoice(AudioDeviceInfo? Device, bool UsedFallback, string RequestedName);

/// <summary>
/// 프로필에 저장한 장치 ID를 지금 연결된 장치로 바꿔 찾는 순수 로직입니다.
/// ID가 그대로 있으면 ID로, 다른 PC처럼 ID가 바뀌었으면 저장해 둔 장치 이름으로 찾습니다.
/// </summary>
internal static class ProfileDeviceResolver
{
    /// <summary>
    /// 연결된 장치 중에서 ID 또는 저장된 이름이 같은 장치를 찾습니다.
    /// </summary>
    public static AudioDeviceInfo? Resolve(
        string? deviceId,
        IReadOnlyList<AudioDeviceInfo> devices,
        IEnumerable<KnownDeviceName> knownNames)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        var byId = devices.FirstOrDefault(device => device.IsConnected && string.Equals(device.Id, deviceId, StringComparison.Ordinal));
        if (byId is not null)
        {
            return byId;
        }

        var knownName = FindKnownName(deviceId, knownNames);
        if (knownName is null)
        {
            return null;
        }

        var normalizedName = ProfileCommandLine.NormalizeName(knownName);
        return devices.FirstOrDefault(device =>
            device.IsConnected && ProfileCommandLine.NormalizeName(device.Name) == normalizedName);
    }

    /// <summary>
    /// 첫 장치부터 차례로 보며 연결된 장치를 고릅니다.
    /// </summary>
    public static ProfileDeviceChoice ChooseFirstAvailable(
        IReadOnlyList<string> deviceIdsInOrder,
        IReadOnlyList<AudioDeviceInfo> devices,
        IEnumerable<KnownDeviceName> knownNames)
    {
        var names = knownNames.ToList();
        var candidates = deviceIdsInOrder
            .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var requestedName = candidates.Count == 0 ? string.Empty : DescribeDevice(candidates[0], devices, names);
        for (var index = 0; index < candidates.Count; index++)
        {
            var device = Resolve(candidates[index], devices, names);
            if (device is not null)
            {
                return new ProfileDeviceChoice(device, index > 0, requestedName);
            }
        }

        return new ProfileDeviceChoice(null, candidates.Count > 0, requestedName);
    }

    /// <summary>
    /// 알림·로그에 보여 줄 장치 이름입니다. 지금 목록에 없으면 저장된 이름을 씁니다.
    /// </summary>
    public static string DescribeDevice(string deviceId, IReadOnlyList<AudioDeviceInfo> devices, IEnumerable<KnownDeviceName> knownNames)
    {
        return devices.FirstOrDefault(device => device.Id == deviceId)?.Name
            ?? FindKnownName(deviceId, knownNames)
            ?? "알 수 없는 장치";
    }

    /// <summary>
    /// 프로필이 가리키는 모든 장치 ID입니다. 이름 기록을 갱신할 때 씁니다.
    /// </summary>
    public static IEnumerable<string> GetReferencedDeviceIds(AudioProfile profile)
    {
        return new[] { profile.DefaultOutputDeviceId, profile.DefaultInputDeviceId }
            .Concat(profile.OutputFallbackDeviceIds)
            .Concat(profile.InputFallbackDeviceIds)
            .Concat(profile.ProgramOutputRoutes.Select(route => route.TargetDeviceId))
            .Concat(profile.DeviceLevels.Select(level => level.DeviceId))
            .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// 프로필이 가리키는 장치의 이름 기록을 지금 장치 목록 기준으로 갱신합니다. 목록에 없는 장치는 예전 이름을 유지합니다.
    /// </summary>
    public static List<KnownDeviceName> BuildKnownNames(
        AudioProfile profile,
        IReadOnlyList<AudioDeviceInfo> devices)
    {
        return GetReferencedDeviceIds(profile)
            .Select(deviceId => new KnownDeviceName
            {
                DeviceId = deviceId,
                Name = devices.FirstOrDefault(device => device.Id == deviceId)?.Name
                    ?? FindKnownName(deviceId, profile.KnownDeviceNames)
                    ?? string.Empty
            })
            .Where(knownName => knownName.Name.Length > 0)
            .ToList();
    }

    private static string? FindKnownName(string deviceId, IEnumerable<KnownDeviceName> knownNames)
    {
        return knownNames.FirstOrDefault(knownName => string.Equals(knownName.DeviceId, deviceId, StringComparison.Ordinal))?.Name;
    }
}
