using System.IO;
using System.Text.Json;
using DesktopAudioController.Models;

namespace DesktopAudioController.Services;

/// <summary>
/// 사용자 설정을 로컬 앱 데이터 폴더에 JSON 파일로 저장하고 읽어오는 서비스입니다.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    // 손상된 설정 파일 감지 후 사용자에게 1회 표시할 경고 메시지 저장소입니다.
    private string? _pendingLoadWarningMessage;

    /// <summary>
    /// 설정 파일을 직렬화/역직렬화할 때 사용하는 공통 옵션입니다.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 현재 사용자 프로필 기준 설정 파일 저장 경로입니다.
    /// </summary>
    public string SettingsFilePath { get; }

    /// <summary>
    /// 손상된 설정 파일을 백업할 때 사용하는 경로입니다.
    /// </summary>
    public string BackupSettingsFilePath { get; }

    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopAudioController",
            "settings.json"))
    {
    }

    public SettingsService(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);

        SettingsFilePath = settingsFilePath;
        BackupSettingsFilePath = $"{SettingsFilePath}.bak";
    }

    /// <summary>
    /// 저장된 설정 파일이 있으면 읽고, 없거나 손상되었으면 기본 설정을 반환합니다.
    /// </summary>
    public AppSettings Load()
    {
        AppLog.Debug("SettingsService", $"Load 시작 path={SettingsFilePath}");
        try
        {
            // 아직 설정 파일이 없으면 기본값으로 시작합니다.
            if (!File.Exists(SettingsFilePath))
            {
                AppLog.Info("SettingsService", "설정 파일이 없어 기본값 사용");
                return new AppSettings();
            }

            // JSON 파일을 읽어 현재 설정 모델로 역직렬화합니다.
            // 디스크에서 읽은 원본 JSON 문자열입니다.
            var json = File.ReadAllText(SettingsFilePath);
            var settings = NormalizeSettings(JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings());
            AppLog.Debug("SettingsService", $"Load 성공 visibleDevices={settings.VisibleDeviceIds.Count} profiles={settings.AudioProfiles.Count} startMinimized={settings.StartMinimized} runAtStartup={settings.RunAtWindowsStartup}");
            return settings;
        }
        catch (Exception exception)
        {
            // 파일 손상이나 읽기 오류가 나도 앱이 죽지 않도록 기본값으로 복구합니다.
            TryBackupCorruptedSettingsFile();
            _pendingLoadWarningMessage =
                $"설정 파일을 읽지 못해 기본 설정으로 복구했습니다.\n\n원본 경로: {SettingsFilePath}\n백업 경로: {BackupSettingsFilePath}";
            AppLog.Error("SettingsService", $"Load 실패, 기본 설정 복구 path={SettingsFilePath}", exception);
            return new AppSettings();
        }
    }

    /// <summary>
    /// 현재 설정을 JSON 파일로 저장합니다.
    /// </summary>
    public void Save(AppSettings settings)
    {
        settings = NormalizeSettings(settings);
        AppLog.Info("SettingsService", $"Save 시작 path={SettingsFilePath} visibleDevices={settings.VisibleDeviceIds.Count} profiles={settings.AudioProfiles.Count} startMinimized={settings.StartMinimized} runAtStartup={settings.RunAtWindowsStartup}");
        try
        {
            WriteSettingsFile(SettingsFilePath, settings);
            AppLog.Info("SettingsService", $"Save 성공 path={SettingsFilePath}");
        }
        catch (Exception exception)
        {
            // 저장 실패는 UI에서 사용자에게 안내할 수 있도록 경로와 함께 명시적 예외로 래핑합니다.
            AppLog.Error("SettingsService", $"Save 실패 path={SettingsFilePath}", exception);
            throw new SettingsPersistenceException(
                "설정 파일을 저장하지 못했습니다.",
                SettingsFilePath,
                exception);
        }
    }

    /// <summary>
    /// 외부 JSON 파일에서 설정을 읽어 검증된 설정 모델로 반환합니다.
    /// </summary>
    public AppSettings ImportFromFile(string sourceFilePath)
    {
        AppLog.Info("SettingsService", $"설정 가져오기 시작 source={sourceFilePath}");
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("가져올 설정 파일을 찾을 수 없습니다.", sourceFilePath);
            }

            var json = File.ReadAllText(sourceFilePath);
            var importedSettings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                ?? throw new InvalidDataException("설정 파일이 비어 있거나 올바른 설정 JSON이 아닙니다.");

            importedSettings = NormalizeSettings(importedSettings);
            AppLog.Info(
                "SettingsService",
                $"설정 가져오기 성공 source={sourceFilePath} visibleDevices={importedSettings.VisibleDeviceIds.Count} programPreferences={importedSettings.ProgramAudioPreferences.Count} profiles={importedSettings.AudioProfiles.Count}");
            return importedSettings;
        }
        catch (Exception exception)
        {
            AppLog.Error("SettingsService", $"설정 가져오기 실패 source={sourceFilePath}", exception);
            throw new SettingsPersistenceException(
                "설정 파일을 가져오지 못했습니다.",
                sourceFilePath,
                exception);
        }
    }

    /// <summary>
    /// 지정한 설정 모델을 외부 JSON 파일로 내보냅니다.
    /// </summary>
    public void ExportToFile(AppSettings settings, string destinationFilePath)
    {
        AppLog.Info("SettingsService", $"설정 내보내기 시작 destination={destinationFilePath}");
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
            WriteSettingsFile(destinationFilePath, settings);
            AppLog.Info("SettingsService", $"설정 내보내기 성공 destination={destinationFilePath}");
        }
        catch (Exception exception)
        {
            AppLog.Error("SettingsService", $"설정 내보내기 실패 destination={destinationFilePath}", exception);
            throw new SettingsPersistenceException(
                "설정 파일을 내보내지 못했습니다.",
                destinationFilePath,
                exception);
        }
    }

    /// <summary>
    /// 직전 로드 과정에서 기록한 경고 메시지를 한 번만 반환합니다.
    /// </summary>
    public bool TryConsumeLoadWarning(out string warningMessage)
    {
        if (string.IsNullOrWhiteSpace(_pendingLoadWarningMessage))
        {
            warningMessage = string.Empty;
            return false;
        }

        warningMessage = _pendingLoadWarningMessage;
        _pendingLoadWarningMessage = null;
        return true;
    }

    /// <summary>
    /// 손상되었을 가능성이 있는 설정 파일을 .bak로 복사합니다.
    /// </summary>
    private void TryBackupCorruptedSettingsFile()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(BackupSettingsFilePath)!);
            File.Copy(SettingsFilePath, BackupSettingsFilePath, overwrite: true);
            AppLog.Warn("SettingsService", $"손상된 설정 파일 백업 완료 backup={BackupSettingsFilePath}");
        }
        catch (Exception exception)
        {
            // 백업 실패는 원본 로드 실패보다 우선순위가 낮으므로 추가 예외를 만들지 않습니다.
            AppLog.Warn("SettingsService", $"손상된 설정 파일 백업 실패 backup={BackupSettingsFilePath}", exception);
        }
    }

    private static void WriteSettingsFile(string filePath, AppSettings settings)
    {
        var tempFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // 사람이 읽기 쉬운 들여쓰기 형태로 저장합니다.
            // 저장 직전에 직렬화된 JSON 문자열입니다.
            var json = JsonSerializer.Serialize(NormalizeSettings(settings), SerializerOptions);
            File.WriteAllText(tempFilePath, json);
            if (File.Exists(filePath))
            {
                File.Replace(tempFilePath, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempFilePath, filePath);
            }
        }
        catch
        {
            TryDeleteTemporarySettingsFile(tempFilePath);
            throw;
        }
    }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        if (settings.VisibleDeviceIds is null)
        {
            settings.VisibleDeviceIds = [];
        }

        settings.ProgramAudioPreferences = NormalizeProgramAudioPreferences(settings.ProgramAudioPreferences);
        settings.AudioProfiles = NormalizeAudioProfiles(settings.AudioProfiles);
        settings.LastAppliedAudioProfileId = NormalizeLastAppliedAudioProfileId(
            settings.LastAppliedAudioProfileId,
            settings.AudioProfiles);
        settings.AutoProfileRules = NormalizeAutoProfileRules(settings.AutoProfileRules);
        settings.AutoProfileFallbackProfileId = NormalizeLastAppliedAudioProfileId(
            settings.AutoProfileFallbackProfileId,
            settings.AudioProfiles);
        return settings;
    }

    private static List<string> NormalizeDeviceIdList(IEnumerable<string>? deviceIds)
    {
        return (deviceIds ?? [])
            .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
            .Select(deviceId => deviceId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static List<AutoProfileRule> NormalizeAutoProfileRules(IEnumerable<AutoProfileRule>? rules)
    {
        var normalizedRules = new List<AutoProfileRule>();
        if (rules is null)
        {
            return normalizedRules;
        }

        foreach (var rule in rules)
        {
            if (rule is null)
            {
                continue;
            }

            rule.Id = string.IsNullOrWhiteSpace(rule.Id)
                ? Guid.NewGuid().ToString("N")
                : rule.Id.Trim();
            rule.Name = rule.Name?.Trim() ?? string.Empty;
            rule.ProfileId = rule.ProfileId?.Trim() ?? string.Empty;
            rule.ProcessNames = (rule.ProcessNames ?? [])
                .Select(ProcessNameMatcher.Normalize)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!Enum.IsDefined(rule.RestoreMode))
            {
                rule.RestoreMode = AutoProfileRestoreMode.PreviousProfile;
            }

            normalizedRules.Add(rule);
        }

        return normalizedRules;
    }

    private static List<ProgramAudioPreference> NormalizeProgramAudioPreferences(IEnumerable<ProgramAudioPreference>? preferences)
    {
        var normalizedPreferences = new List<ProgramAudioPreference>();
        if (preferences is null)
        {
            return normalizedPreferences;
        }

        foreach (var preference in preferences)
        {
            if (preference is null)
            {
                continue;
            }

            preference.MatchKey ??= string.Empty;
            preference.DisplayName ??= string.Empty;
            normalizedPreferences.Add(preference);
        }

        return normalizedPreferences;
    }

    private static List<AudioProfile> NormalizeAudioProfiles(IEnumerable<AudioProfile>? profiles)
    {
        var normalizedProfiles = new List<AudioProfile>();
        if (profiles is null)
        {
            return normalizedProfiles;
        }

        foreach (var profile in profiles)
        {
            if (profile is null)
            {
                continue;
            }

            profile.Name = AudioProfileStore.NormalizeProfileName(profile.Name);
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                continue;
            }

            profile.Id = string.IsNullOrWhiteSpace(profile.Id)
                ? Guid.NewGuid().ToString("N")
                : profile.Id.Trim();
            profile.VisibleDeviceIds = profile.VisibleDeviceIds is null
                ? []
                : profile.VisibleDeviceIds
                    .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
                    .Select(deviceId => deviceId.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            profile.ProgramAudioPreferences = NormalizeProgramAudioPreferences(profile.ProgramAudioPreferences);
            profile.DefaultOutputDeviceId = profile.DefaultOutputDeviceId?.Trim() ?? string.Empty;
            profile.ProgramOutputRoutes = (profile.ProgramOutputRoutes ?? [])
                .Where(route => route is not null && !string.IsNullOrWhiteSpace(ProcessNameMatcher.Normalize(route.ProcessName)))
                .Select(route => new ProgramOutputRoute
                {
                    ProcessName = ProcessNameMatcher.Normalize(route.ProcessName),
                    TargetDeviceId = route.TargetDeviceId?.Trim() ?? string.Empty
                })
                .ToList();
            profile.Hotkey = profile.Hotkey?.Trim() ?? string.Empty;
            profile.DefaultInputDeviceId = profile.DefaultInputDeviceId?.Trim() ?? string.Empty;
            profile.OutputFallbackDeviceIds = NormalizeDeviceIdList(profile.OutputFallbackDeviceIds);
            profile.InputFallbackDeviceIds = NormalizeDeviceIdList(profile.InputFallbackDeviceIds);
            profile.DeviceLevels = (profile.DeviceLevels ?? [])
                .Where(level => level is not null && !string.IsNullOrWhiteSpace(level.DeviceId) && (level.Volume is not null || level.IsMuted is not null))
                .Select(level => new ProfileDeviceLevel
                {
                    DeviceId = level.DeviceId.Trim(),
                    Volume = level.Volume is { } volume ? Math.Clamp(volume, 0, 100) : null,
                    IsMuted = level.IsMuted
                })
                .ToList();
            profile.KnownDeviceNames = (profile.KnownDeviceNames ?? [])
                .Where(knownName => knownName is not null && !string.IsNullOrWhiteSpace(knownName.DeviceId) && !string.IsNullOrWhiteSpace(knownName.Name))
                .ToList();
            normalizedProfiles.Add(profile);
        }

        return normalizedProfiles;
    }

    private static string NormalizeLastAppliedAudioProfileId(
        string? lastAppliedAudioProfileId,
        IEnumerable<AudioProfile> profiles)
    {
        var normalizedProfileId = lastAppliedAudioProfileId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedProfileId))
        {
            return string.Empty;
        }

        return profiles.Any(profile => string.Equals(profile.Id, normalizedProfileId, StringComparison.Ordinal))
            ? normalizedProfileId
            : string.Empty;
    }

    private static void TryDeleteTemporarySettingsFile(string tempFilePath)
    {
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
        catch
        {
            // 임시 파일 삭제 실패는 저장 실패보다 우선순위가 낮으므로 추가 예외를 만들지 않습니다.
        }
    }
}
