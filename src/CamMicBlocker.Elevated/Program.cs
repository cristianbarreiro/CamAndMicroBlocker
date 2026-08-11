using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace CamMicBlocker.Elevated;

/// <summary>
/// Elevated helper process for CamMicBlocker.
/// Performs privileged operations (registry writes, device enable/disable)
/// that require administrator access.
/// 
/// Designed to be launched by the main app with Verb="runas", perform a
/// single operation, write the result to a JSON file, and exit immediately.
/// 
/// Usage:
///   CamMicBlocker.Elevated.exe set-policy "both" --result-file "path"
///   CamMicBlocker.Elevated.exe remove-policy "both" --result-file "path"
///   CamMicBlocker.Elevated.exe disable-devices "id1|id2" --result-file "path"
///   CamMicBlocker.Elevated.exe enable-devices "id1|id2" --result-file "path"
/// </summary>
internal static class Program
{
    // Registry constants
    private const string PolicyRegistryPath = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
    private const string CameraValueName = "LetAppsAccessCamera";
    private const string MicrophoneValueName = "LetAppsAccessMicrophone";
    private const int PolicyDeny = 2;

    // CfgMgr32 P/Invoke
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("CfgMgr32.dll")]
    private static extern uint CM_Disable_DevNode(uint dnDevInst, uint ulFlags);

    [DllImport("CfgMgr32.dll")]
    private static extern uint CM_Enable_DevNode(uint dnDevInst, uint ulFlags);

    private const uint CR_SUCCESS = 0;
    private const uint CM_LOCATE_DEVNODE_NORMAL = 0;
    private const uint CM_DISABLE_UI_NOT_OK = 0x00000004;

    static int Main(string[] args)
    {
        string? resultFilePath = null;

        try
        {
            if (args.Length < 2)
            {
                WriteError("Usage: CamMicBlocker.Elevated.exe <command> <argument> [--result-file <path>]", null);
                return 1;
            }

            var command = args[0].ToLowerInvariant();
            var argument = args[1];

            // Parse optional --result-file argument
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--result-file")
                {
                    resultFilePath = args[i + 1];
                    break;
                }
            }

            var result = command switch
            {
                "set-policy" => ExecuteSetPolicy(argument),
                "remove-policy" => ExecuteRemovePolicy(argument),
                "disable-devices" => ExecuteDisableDevices(argument),
                "enable-devices" => ExecuteEnableDevices(argument),
                _ => new OperationResult { Success = false, Error = $"Unknown command: {command}" }
            };

            WriteResult(result, resultFilePath);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            var error = $"Unhandled exception: {ex.Message}";
            WriteResult(new OperationResult { Success = false, Error = error }, resultFilePath);
            return 1;
        }
    }

    private static OperationResult ExecuteSetPolicy(string target)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(PolicyRegistryPath);
            if (key == null)
                return new OperationResult { Success = false, Error = "Failed to create/open registry key" };

            var setCamera = target is "camera" or "both";
            var setMicrophone = target is "microphone" or "both";

            if (setCamera)
                key.SetValue(CameraValueName, PolicyDeny, RegistryValueKind.DWord);

            if (setMicrophone)
                key.SetValue(MicrophoneValueName, PolicyDeny, RegistryValueKind.DWord);

            return new OperationResult { Success = true };
        }
        catch (Exception ex)
        {
            return new OperationResult { Success = false, Error = $"Failed to set policy: {ex.Message}" };
        }
    }

    private static OperationResult ExecuteRemovePolicy(string target)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PolicyRegistryPath, writable: true);
            if (key == null)
            {
                // Key doesn't exist — policy is already removed
                return new OperationResult { Success = true };
            }

            var removeCamera = target is "camera" or "both";
            var removeMicrophone = target is "microphone" or "both";

            if (removeCamera)
                key.DeleteValue(CameraValueName, throwOnMissingValue: false);

            if (removeMicrophone)
                key.DeleteValue(MicrophoneValueName, throwOnMissingValue: false);

            return new OperationResult { Success = true };
        }
        catch (Exception ex)
        {
            return new OperationResult { Success = false, Error = $"Failed to remove policy: {ex.Message}" };
        }
    }

    private static OperationResult ExecuteDisableDevices(string instanceIds)
    {
        var ids = instanceIds.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var errors = new List<string>();

        foreach (var id in ids)
        {
            var result = CM_Locate_DevNodeW(out uint devInst, id, CM_LOCATE_DEVNODE_NORMAL);
            if (result != CR_SUCCESS)
            {
                errors.Add($"Failed to locate device {id}: error 0x{result:X8}");
                continue;
            }

            result = CM_Disable_DevNode(devInst, CM_DISABLE_UI_NOT_OK);
            if (result != CR_SUCCESS)
            {
                errors.Add($"Failed to disable device {id}: error 0x{result:X8}");
            }
        }

        if (errors.Count > 0)
            return new OperationResult { Success = false, Error = string.Join("; ", errors) };

        return new OperationResult { Success = true };
    }

    private static OperationResult ExecuteEnableDevices(string instanceIds)
    {
        var ids = instanceIds.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var errors = new List<string>();

        foreach (var id in ids)
        {
            var result = CM_Locate_DevNodeW(out uint devInst, id, CM_LOCATE_DEVNODE_NORMAL);
            if (result != CR_SUCCESS)
            {
                errors.Add($"Failed to locate device {id}: error 0x{result:X8}");
                continue;
            }

            result = CM_Enable_DevNode(devInst, 0);
            if (result != CR_SUCCESS)
            {
                errors.Add($"Failed to enable device {id}: error 0x{result:X8}");
            }
        }

        if (errors.Count > 0)
            return new OperationResult { Success = false, Error = string.Join("; ", errors) };

        return new OperationResult { Success = true };
    }

    private static void WriteResult(OperationResult result, string? filePath)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                File.WriteAllText(filePath, json);
            }
            catch
            {
                // If we can't write the result file, write to console as fallback
                Console.WriteLine(json);
            }
        }
        else
        {
            Console.WriteLine(json);
        }
    }

    private static void WriteError(string message, string? filePath)
    {
        WriteResult(new OperationResult { Success = false, Error = message }, filePath);
    }

    private sealed class OperationResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}
