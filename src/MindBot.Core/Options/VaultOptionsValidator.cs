using Microsoft.Extensions.Options;

namespace MindBot.Core.Options;

public sealed class VaultOptionsValidator : IValidateOptions<VaultOptions>
{
    public ValidateOptionsResult Validate(string? name, VaultOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Root))
        {
            return ValidateOptionsResult.Fail("VAULT__ROOT is required but was not set.");
        }

        if (!Path.IsPathRooted(options.Root))
        {
            return ValidateOptionsResult.Fail($"VAULT__ROOT must be an absolute path, got '{options.Root}'.");
        }

        return ValidateOptionsResult.Success;
    }
}
