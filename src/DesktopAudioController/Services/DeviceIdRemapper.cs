using DesktopAudioController.Models;

namespace DesktopAudioController.Services;

/// <summary>
/// Windows 가 같은 장치에 새 ID 를 붙였을 때(USB 포트를 바꿔 꽂거나 드라이버가 다시 설치될 때) 설정 속 옛 ID 를 새 ID 로 바꿉니다.
/// 표시 장치 이름을 기억해 두었다가, 옛 ID 가 사라지고 같은 이름의 장치가 새 ID 로 하나만 있으면 그 ID 로 바꿉니다.
/// </summary>
internal static class DeviceIdRemapper
{
    /// <summary>
    /// 지금 목록에 있는 표시 장치의 이름을 설정에 기억합니다. 바뀐 것이 있으면 true 입니다.
    /// </summary>
    public static bool RememberVisibleDeviceNames(AppSettings settings, IReadOnlyList<AudioDeviceInfo> devices)
    {
        var changed = false;
        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.Name) || !settings.VisibleDeviceIds.Contains(device.Id, StringComparer.Ordinal))
            {
                continue;
            }

            var known = settings.KnownDeviceNames.FirstOrDefault(name => string.Equals(name.DeviceId, device.Id, StringComparison.Ordinal));
            if (known is null)
            {
                settings.KnownDeviceNames.Add(new KnownDeviceName { DeviceId = device.Id, Name = device.Name });
                changed = true;
            }
            else if (!string.Equals(known.Name, device.Name, StringComparison.Ordinal))
            {
                known.Name = device.Name;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// 설정과 프로필이 가리키는 출력 장치 ID 중 지금 없는 것을, 이름이 같은 새 장치 ID 로 바꿉니다.
    /// 바꾼 (옛 ID, 새 ID, 이름) 목록을 돌려줍니다.
    /// </summary>
    public static IReadOnlyList<(string OldId, string NewId, string Name)> RemapMissingDeviceIds(
        AppSettings settings,
        IReadOnlyList<AudioDeviceInfo> devices)
    {
        var presentIds = devices.Select(device => device.Id).ToHashSet(StringComparer.Ordinal);
        var knownNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var knownName in settings.KnownDeviceNames.Concat(settings.AudioProfiles.SelectMany(profile => profile.KnownDeviceNames)))
        {
            if (!string.IsNullOrWhiteSpace(knownName.DeviceId) && !string.IsNullOrWhiteSpace(knownName.Name))
            {
                knownNames.TryAdd(knownName.DeviceId, knownName.Name);
            }
        }

        var referencedIds = EnumerateReferencedOutputIds(settings).ToHashSet(StringComparer.Ordinal);
        var remaps = new List<(string OldId, string NewId, string Name)>();
        foreach (var missingId in referencedIds.Where(id => !presentIds.Contains(id)))
        {
            if (!knownNames.TryGetValue(missingId, out var name))
            {
                continue;
            }

            var normalizedName = ProfileCommandLine.NormalizeName(name);
            var candidates = devices
                .Where(device => ProfileCommandLine.NormalizeName(device.Name) == normalizedName)
                .ToList();

            // 이름이 같은 장치가 여러 개면 어느 것인지 알 수 없으므로 바꾸지 않습니다.
            // 새 ID 가 이미 목록에 있어도 괜찮습니다(바꾼 뒤 중복을 하나로 합칩니다).
            if (candidates.Count != 1)
            {
                continue;
            }

            remaps.Add((missingId, candidates[0].Id, name));
        }

        if (remaps.Count == 0)
        {
            return remaps;
        }

        var map = remaps.ToDictionary(remap => remap.OldId, remap => remap.NewId, StringComparer.Ordinal);
        string Swap(string id) => map.TryGetValue(id, out var newId) ? newId : id;
        List<string> SwapList(IEnumerable<string> ids) => ids.Select(Swap).Distinct(StringComparer.Ordinal).ToList();

        settings.VisibleDeviceIds = SwapList(settings.VisibleDeviceIds);
        foreach (var knownName in settings.KnownDeviceNames)
        {
            knownName.DeviceId = Swap(knownName.DeviceId);
        }

        settings.KnownDeviceNames = settings.KnownDeviceNames
            .GroupBy(knownName => knownName.DeviceId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();

        foreach (var profile in settings.AudioProfiles)
        {
            profile.VisibleDeviceIds = SwapList(profile.VisibleDeviceIds);
            profile.DefaultOutputDeviceId = Swap(profile.DefaultOutputDeviceId);
            profile.OutputFallbackDeviceIds = SwapList(profile.OutputFallbackDeviceIds);
            foreach (var route in profile.ProgramOutputRoutes)
            {
                route.TargetDeviceId = Swap(route.TargetDeviceId);
            }

            foreach (var level in profile.DeviceLevels)
            {
                level.DeviceId = Swap(level.DeviceId);
            }

            foreach (var knownName in profile.KnownDeviceNames)
            {
                knownName.DeviceId = Swap(knownName.DeviceId);
            }

            profile.KnownDeviceNames = profile.KnownDeviceNames
                .GroupBy(knownName => knownName.DeviceId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToList();
        }

        return remaps;
    }

    /// <summary>
    /// 설정과 프로필이 가리키는 출력 장치 ID 전부입니다. 입력(마이크) 장치는 출력 목록에 없으므로 빼고, 적용할 때 이름으로 다시 찾습니다.
    /// </summary>
    private static IEnumerable<string> EnumerateReferencedOutputIds(AppSettings settings)
    {
        return settings.VisibleDeviceIds
            .Concat(settings.AudioProfiles.SelectMany(profile => profile.VisibleDeviceIds
                .Append(profile.DefaultOutputDeviceId)
                .Concat(profile.OutputFallbackDeviceIds)
                .Concat(profile.ProgramOutputRoutes.Select(route => route.TargetDeviceId))
                .Concat(profile.DeviceLevels.Select(level => level.DeviceId))))
            .Where(id => !string.IsNullOrWhiteSpace(id));
    }
}
