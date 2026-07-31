using Microsoft.Extensions.Options;

namespace MindBot.Core.Options;

public sealed class StateOptionsValidator(IOptions<VaultOptions> vaultOptions) : IValidateOptions<StateOptions>
{
    public ValidateOptionsResult Validate(string? name, StateOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            errors.Add("STATE__DATABASEPATH is required but was not set.");
        }
        else if (!Path.IsPathRooted(options.DatabasePath))
        {
            errors.Add($"STATE__DATABASEPATH must be an absolute path, got '{options.DatabasePath}'.");
        }
        else if (PathContainment.IsInside(options.DatabasePath, vaultOptions.Value.Root))
        {
            errors.Add(
                $"STATE__DATABASEPATH ('{options.DatabasePath}') is inside VAULT__ROOT ('{vaultOptions.Value.Root}'). " +
                "The state database would be committed to the vault branch by 'git add -A'. Point it at a separate volume.");
        }

        if (options.ConversationExpiryMinutes <= 0)
        {
            errors.Add($"STATE__CONVERSATIONEXPIRYMINUTES must be greater than zero, got {options.ConversationExpiryMinutes}.");
        }

        if (options.ProcessedUpdateRetentionDays <= 0)
        {
            errors.Add($"STATE__PROCESSEDUPDATERETENTIONDAYS must be greater than zero, got {options.ProcessedUpdateRetentionDays}.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
