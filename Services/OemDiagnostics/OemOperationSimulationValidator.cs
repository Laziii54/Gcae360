using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Validates an operation's reviewed request/response sequence without vehicle
/// I/O. Passing this check never authorizes execution on a real vehicle.
/// </summary>
public static class OemOperationSimulationValidator
{
    public static void Validate(OemOperationDefinition operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        foreach (var step in operation.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.SimulationResponseHex))
                throw new InvalidOperationException(
                    $"{operation.OperationId}/{step.Id}: a reviewed simulation response is required.");

            var request = OemOperationPackageParser.ParseRequest(step.RequestHex);
            var response = UdsResponseParser.Parse(ParseHex(step.SimulationResponseHex));
            var expected = step.Role.Equals("VERIFY", StringComparison.OrdinalIgnoreCase)
                ? step.ExpectedResponsePayloadHex
                : string.Empty;
            var result = OemOperationResponseValidator.Validate(request, response, expected);
            if (!result.Success)
                throw new InvalidOperationException(
                    $"{operation.OperationId}/{step.Id}: simulated response failed validation: {result.Detail}");
        }
    }

    private static byte[] ParseHex(string value)
    {
        if (value.Any(character =>
                !Uri.IsHexDigit(character) && !char.IsWhiteSpace(character) && character is not ('-' or ':')))
            throw new FormatException("Simulation response must contain hexadecimal bytes only.");

        var normalized = new string(value.Where(Uri.IsHexDigit).ToArray());
        if (normalized.Length < 2 || normalized.Length % 2 != 0)
            throw new FormatException("Simulation response must contain complete bytes.");

        return Enumerable.Range(0, normalized.Length / 2)
            .Select(index => Convert.ToByte(normalized.Substring(index * 2, 2), 16))
            .ToArray();
    }
}
