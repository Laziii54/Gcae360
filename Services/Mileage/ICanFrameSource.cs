namespace CarScanner.Services.Mileage;

public readonly record struct CanFrame(
    uint Id, bool Extended29Bit, ReadOnlyMemory<byte> Data, DateTimeOffset Timestamp);

public interface ICanFrameSource
{
    Task<CanFrame?> ReadFrameAsync(
        uint canId, bool extended29Bit, TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
