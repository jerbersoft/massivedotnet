namespace MassiveDotNet;

/// <summary>
/// Whether an options contract is a call or a put.
/// </summary>
public enum ContractType
{
    /// <summary>The right to buy the underlying at the strike price.</summary>
    Call = 0,

    /// <summary>The right to sell the underlying at the strike price.</summary>
    Put = 1,
}
