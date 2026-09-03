using Microsoft.Extensions.Options;

namespace MassiveDotNet.Extensions.DependencyInjection;

/// <summary>
/// Runs <see cref="MassiveClientOptions.Validate"/> through the options pipeline, so a caller who
/// resolves <c>IOptions&lt;MassiveClientOptions&gt;</c> directly fails the same way the registered
/// transport does rather than receiving a half-configured object.
/// </summary>
internal sealed class ValidateMassiveClientOptions : IValidateOptions<MassiveClientOptions>
{
    public ValidateOptionsResult Validate(string? name, MassiveClientOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            // Rule 11: Validate names the property that is wrong, never the value it holds, so an
            // API key cannot reach a log by way of an options validation failure.
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }
}
