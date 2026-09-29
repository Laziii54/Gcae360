using CarScanner.Services.UDS;

namespace CarScanner.Services.Mileage;

/// <summary>
/// Keeps the mileage reader tolerant of small UdsResponse model changes.
/// The adapter looks for a byte-array payload property named Data/Payload/RawData.
/// </summary>
internal static class UdsMileageResponseAdapter
{
    public static bool TryGetPayload(
        UdsResponse response,
        out byte[] payload)
    {
        payload = Array.Empty<byte>();

        var type = response.GetType();

        foreach (var propertyName in new[]
                 {
                     "Data",
                     "Payload",
                     "RawData",
                     "Bytes"
                 })
        {
            var property =
                type.GetProperty(
                    propertyName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public);

            if (property?.GetValue(response) is byte[] bytes)
            {
                payload = bytes;
                return true;
            }

            if (property?.GetValue(response) is ReadOnlyMemory<byte> memory)
            {
                payload = memory.ToArray();
                return true;
            }
        }

        return false;
    }
}
