namespace CarScanner.Services;

/// <summary>
/// The one way pages and view models navigate. It runs on the main thread, one navigation at a time (see
/// <see cref="NavigationGate"/>), refuses a "back" at the root, and never lets a navigation failure escape: a failure is
/// reported (privacy-safe, like every other command error) instead of closing the app. It returns false when the request
/// was ignored or failed, so callers that care can tell.
/// </summary>
public static class SafeNavigation
{
    private static readonly NavigationGate Gate = new();

    public static Task<bool> GoToAsync(string route) => RunAsync(route, null);

    public static Task<bool> GoToAsync(string route, IDictionary<string, object> parameters) => RunAsync(route, parameters);

    public static Task<bool> GoToAsync(string route, ShellNavigationQueryParameters parameters) => RunAsync(route, parameters);

    private static async Task<bool> RunAsync(string route, object? parameters)
    {
        if (string.IsNullOrWhiteSpace(route))
            return false;

        if (!Gate.TryBegin(route))
        {
            System.Diagnostics.Debug.WriteLine($"Navigation to '{route}' ignored (another navigation is running or it was just requested).");
            return false;
        }

        try
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var shell = Shell.Current;
                if (shell is null)
                    return false;

                if (NavigationGate.IsBack(route) &&
                    !NavigationGate.CanGoBack(shell.Navigation.NavigationStack.Count, shell.Navigation.ModalStack.Count))
                    return false;

                switch (parameters)
                {
                    case ShellNavigationQueryParameters query:
                        await shell.GoToAsync(route, query);
                        break;
                    case IDictionary<string, object> dictionary:
                        await shell.GoToAsync(route, dictionary);
                        break;
                    default:
                        await shell.GoToAsync(route);
                        break;
                }

                return true;
            });
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            CommandErrors.Report(exception, "Navigation");
            return false;
        }
        finally
        {
            Gate.End();
        }
    }
}
