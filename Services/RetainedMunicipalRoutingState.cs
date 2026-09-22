namespace BusinessLicensing_Practice.Services;

public sealed class RetainedMunicipalRoutingState
{
    public string? Municipality { get; private set; }
    public TradingAddress? Address { get; private set; }
    public bool IsRouting { get; private set; }

    public bool IsValidFor(TradingAddress address) =>
        Municipality is not null && Address == address;

    public async Task<RetainedRoutingAttempt> RouteAsync(
        TradingAddress address,
        Func<TradingAddress, Task<MunicipalRoutingResult>> resolveAsync)
    {
        if (IsValidFor(address))
            return new(true, true, new(Municipality, RoutingFailure.None));
        if (IsRouting)
            return new(false, false, null);

        Clear();
        IsRouting = true;
        try
        {
            var result = await resolveAsync(address);
            if (result.Success)
            {
                Municipality = result.Municipality;
                Address = address;
            }
            return new(true, false, result);
        }
        finally
        {
            IsRouting = false;
        }
    }

    public void Clear()
    {
        Municipality = null;
        Address = null;
    }
}

public sealed record RetainedRoutingAttempt(bool Completed, bool Reused, MunicipalRoutingResult? Result);
