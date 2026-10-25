using System.Diagnostics;
using DesktopAudioController.Models;

namespace DesktopAudioController.Services;

/// <summary>
/// 별도 프로세스에서 Core Audio 장치 열거를 수행하고 결과를 JSON 파일로 남깁니다.
/// </summary>
internal static class AudioDeviceProbeWorker
{
    public static int Run(string outputPath, bool probeInput = false)
    {
        var stopwatch = Stopwatch.StartNew();
        AppLog.Info("AudioDeviceProbeWorker", $"오디오 probe 시작 outputPath={outputPath} input={probeInput}");

        try
        {
            using var service = new NativeAudioDeviceCatalogService();
            var sourceDevices = probeInput
                ? service.GetAvailableInputDevices()
                : service.GetAvailableOutputDevices();
            var devices = sourceDevices
                .Select(device => new AudioDeviceInfo
                {
                    Id = device.Id,
                    Name = device.Name,
                    IsConnected = device.IsConnected,
                    IsDefault = device.IsDefault,
                    IsMuted = device.IsMuted,
                    Volume = device.Volume
                })
                .ToList();

            AudioDeviceStartupSnapshotService.WriteProbeOutput(
                outputPath,
                new AudioDeviceStartupSnapshot
                {
                    CapturedAtUtc = DateTimeOffset.UtcNow,
                    Devices = devices
                });

            AppLog.Info(
                "AudioDeviceProbeWorker",
                $"오디오 probe 완료 outputPath={outputPath} count={devices.Count} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return 0;
        }
        catch (Exception exception)
        {
            AppLog.Error("AudioDeviceProbeWorker", $"오디오 probe 실패 outputPath={outputPath}", exception);
            return 1;
        }
    }
}
