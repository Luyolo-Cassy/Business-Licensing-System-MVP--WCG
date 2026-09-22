using BusinessLicensing_Practice.Services;
using System.ComponentModel.DataAnnotations;

internal static class RetainedRoutingStateTests
{
    public static async Task RunAsync()
    {
        var original = new TradingAddress("1 Main Road", "Unit 2", "Central", "Malmesbury", "7300");
        var state = new RetainedMunicipalRoutingState();
        var calls = 0;
        Task<MunicipalRoutingResult> Success(TradingAddress _)
        {
            calls++;
            return Task.FromResult(new MunicipalRoutingResult("Swartland Municipality", RoutingFailure.None));
        }
        var participationChecks = 0;
        Task Active(string _)
        {
            participationChecks++;
            return Task.CompletedTask;
        }

        // Component validation guards this call: invalid Step 3 input therefore cannot invoke routing.
        var stepIsValid = false;
        if (stepIsValid) await state.RouteAsync(original, Success, Active);
        Check(calls == 0, "Invalid Step 3 does not invoke routing");

        var success = await state.RouteAsync(original, Success, Active);
        Check(success.Completed && !success.Reused && success.Result?.Success == true,
            "Valid Step 3 routes successfully and permits progression");
        Check(state.Municipality == "Swartland Municipality", "Canonical municipality retained");
        Check(state.Address == original && state.IsValidFor(original), "Normalized address snapshot retained");
        Check(!state.IsRouting, "Busy state resets after success");

        var reused = await state.RouteAsync(original, Success, Active);
        Check(reused.Completed && reused.Reused && calls == 1 && participationChecks == 2,
            "Unchanged address reuses geographic routing but rechecks participation");

        var changedAddresses = new[]
        {
            original with { Line1 = "2 Main Road" },
            original with { Line2 = "Unit 3" },
            original with { Suburb = "North" },
            original with { City = "Moorreesburg" },
            original with { PostalCode = "7310" }
        };
        var expectedCalls = calls;
        foreach (var changed in changedAddresses)
        {
            Check(!state.IsValidFor(changed), "Changed routing-relevant address invalidates snapshot");
            await state.RouteAsync(changed, Success, Active);
            expectedCalls++;
            Check(calls == expectedCalls && state.IsValidFor(changed), "Changed address requires a new successful route");
        }

        state.Clear();
        Check(!state.IsValidFor(changedAddresses[^1]) && state.Municipality is null && state.Address is null,
            "Missing routing state blocks final submission");

        var failure = await state.RouteAsync(original, _ =>
            Task.FromResult(new MunicipalRoutingResult(null, RoutingFailure.AddressNotPrecise)), Active);
        Check(failure.Completed && failure.Result?.Failure == RoutingFailure.AddressNotPrecise
            && !state.IsValidFor(original), "Routing failure prevents progression and retains no stale result");
        Check(!state.IsRouting, "Busy state resets after routing failure");

        var release = new TaskCompletionSource<MunicipalRoutingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = state.RouteAsync(original, _ => release.Task, Active);
        await Task.Yield();
        Check(state.IsRouting, "Routing exposes busy state");
        var duplicate = await state.RouteAsync(original, Success, Active);
        Check(!duplicate.Completed && calls == expectedCalls, "Duplicate routing action is prevented");
        release.SetResult(new("Swartland Municipality", RoutingFailure.None));
        await first;
        Check(!state.IsRouting, "Busy state resets after concurrent routing completes");

        state.Clear();
        try
        {
            await state.RouteAsync(original, _ => throw new InvalidOperationException("test"), Active);
            throw new Exception("Expected routing exception");
        }
        catch (InvalidOperationException)
        {
            Check(!state.IsRouting && !state.IsValidFor(original), "Busy state resets after routing exception");
        }

        async Task ParticipationFailure(string label)
        {
            state.Clear();
            try
            {
                await state.RouteAsync(original, Success, _ =>
                    throw new ValidationException(MunicipalityManagementService.UnavailableRoutingMessage));
                throw new Exception("Expected participation failure");
            }
            catch (ValidationException error)
            {
                Check(error.Message == MunicipalityManagementService.UnavailableRoutingMessage
                    && !state.IsValidFor(original) && state.Municipality is null && state.Address is null,
                    label + " blocks Step 3 and retains no successful routing state");
            }
        }
        await ParticipationFailure("Missing participating municipality");
        await ParticipationFailure("Inactive participating municipality");

        var newlyParticipating = await state.RouteAsync(original, Success, Active);
        Check(newlyParticipating.Result?.Success == true && state.IsValidFor(original),
            "Newly active catalogue municipality can retain routing and proceed");
        try
        {
            await state.RouteAsync(original, Success, _ =>
                throw new ValidationException(MunicipalityManagementService.UnavailableRoutingMessage));
            throw new Exception("Expected deactivation failure");
        }
        catch (ValidationException)
        {
            Check(!state.IsValidFor(original), "Municipality deactivated after success clears reused retained state");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
