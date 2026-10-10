namespace CarScanner.Services.DataCollection;

/// <summary>
/// The JSON body of one upload to <c>POST /api/ecu-observations</c>. Kept separate from the HTTP code so a test
/// can send exactly this shape to the server's validator: the field names here are the contract with the backend.
/// </summary>
public static class CollectionPayload
{
    public static object Build(IReadOnlyList<QueuedExchange> batch, string appVersion) => new
    {
        appVersion,
        groups = batch
            .GroupBy(i => (i.Make, i.Model, i.Year, i.Protocol))
            .Select(g => new
            {
                make = g.Key.Make,
                model = g.Key.Model,
                year = g.Key.Year,
                protocol = g.Key.Protocol,
                items = g.Select(i => new
                {
                    tx = i.Exchange.TxCanId,
                    rx = i.Exchange.RxCanId,
                    ext29 = i.Exchange.Extended29Bit,
                    request = i.Exchange.Request,
                    response = i.Exchange.Response,
                    observedOn = i.ObservedOn
                }).ToArray()
            }).ToArray()
    };
}
