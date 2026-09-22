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
        Func<TradingAddress, Task<MunicipalRoutingResult>> resolveAsync,
        Func<string, Task> requireActiveAsync)
    {
        if (IsRouting)
            return new(false, false, null);

        var reused = IsValidFor(address);
        if (!reused) Clear();
        IsRouting = true;
        try
        {
            var result = reused
                ? new MunicipalRoutingResult(Municipality, RoutingFailure.None)
                : await resolveAsync(address);
            if (!result.Success)
            {
                Clear();
                return new(true, false, result);
            }
            await requireActiveAsync(result.Municipality!);
            Municipality = result.Municipality;
            Address = address;
            return new(true, reused, result);
        }
        catch
        {
            Clear();
            throw;
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
