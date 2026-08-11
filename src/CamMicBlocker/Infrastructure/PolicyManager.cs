using CamMicBlocker.Domain.Interfaces;
using CamMicBlocker.Domain.Models;
using Microsoft.Win32;
using Serilog;

namespace CamMicBlocker.Infrastructure;

/// <summary>
/// Reads and writes Windows privacy policy registry values.
/// 
/// Registry path: HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy
/// Values:
///   LetAppsAccessCamera = 2 → Deny camera access
///   LetAppsAccessMicrophone = 2 → Deny microphone access
///   (absent or 0) → Allow (user-controlled, default Windows behavior)
/// 
/// READING does not require admin privileges.
/// WRITING requires admin — delegated to the elevated helper.
/// </summary>
public sealed class PolicyManager : IPolicyManager
{
    private static readonly ILogger Log = Serilog.Log.ForContext<PolicyManager>();

    private const string PolicyRegistryPath = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
    private const string CameraValueName = "LetAppsAccessCamera";
    private const string MicrophoneValueName = "LetAppsAccessMicrophone";
    private const int PolicyDeny = 2;

    private readonly PrivilegedOperationClient _privilegedClient;

    public PolicyManager(PrivilegedOperationClient privilegedClient)
    {
        _privilegedClient = privilegedClient;
    }

    public BlockStatus GetCameraPolicyStatus()
    {
        return ReadPolicyValue(CameraValueName);
    }

    public BlockStatus GetMicrophonePolicyStatus()
    {
        return ReadPolicyValue(MicrophoneValueName);
    }

    public async Task<OperationResult> SetPolicyAsync(BlockTarget target, BlockStatus status)
    {
        Log.Information("Setting policy: Target={Target}, Status={Status}", target, status);

        var command = status == BlockStatus.Blocked ? "set-policy" : "remove-policy";
        var targetArg = target switch
        {
            BlockTarget.Camera => "camera",
            BlockTarget.Microphone => "microphone",
            BlockTarget.Both => "both",
            _ => "both"
        };

        return await _privilegedClient.ExecuteAsync(command, targetArg);
    }

    private BlockStatus ReadPolicyValue(string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PolicyRegistryPath);
            if (key == null)
            {
                Log.Debug("Policy registry key not found — policies not set (Allowed)");
                return BlockStatus.Allowed;
            }

            var value = key.GetValue(valueName);
            if (value == null)
            {
                Log.Debug("Policy value {ValueName} not found — Allowed", valueName);
                return BlockStatus.Allowed;
            }

            var intValue = Convert.ToInt32(value);
            var status = intValue == PolicyDeny ? BlockStatus.Blocked : BlockStatus.Allowed;
            Log.Debug("Policy {ValueName} = {Value} → {Status}", valueName, intValue, status);
            return status;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read policy value {ValueName}", valueName);
            return BlockStatus.Unknown;
        }
    }
}
