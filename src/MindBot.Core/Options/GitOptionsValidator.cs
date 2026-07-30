using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace MindBot.Core.Options;

public sealed class GitOptionsValidator : IValidateOptions<GitOptions>
{
    public ValidateOptionsResult Validate(string? name, GitOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RemoteUrl))
        {
            errors.Add("GIT__REMOTEURL is required but was not set.");
        }

        if (string.IsNullOrWhiteSpace(options.Branch))
        {
            errors.Add("GIT__BRANCH is required but was not set.");
        }

        if (string.IsNullOrWhiteSpace(options.SshKeyPath))
        {
            errors.Add("GIT__SSHKEYPATH is required but was not set.");
        }
        else if (!File.Exists(options.SshKeyPath))
        {
            errors.Add($"GIT__SSHKEYPATH points to a file that does not exist: '{options.SshKeyPath}'.");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var mode = File.GetUnixFileMode(options.SshKeyPath);
            const UnixFileMode groupOrOtherRead =
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

            if ((mode & groupOrOtherRead) != 0)
            {
                errors.Add(
                    $"GIT__SSHKEYPATH ('{options.SshKeyPath}') is readable or writable by group/other (mode {ToOctal(mode)}). " +
                    "Run 'chmod 600' on the key file.");
            }
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }

    private static string ToOctal(UnixFileMode mode) => Convert.ToString((int)mode, 8).PadLeft(3, '0');
}
