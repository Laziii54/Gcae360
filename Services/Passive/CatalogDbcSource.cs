using System.Globalization;
using SQLite;

namespace CarScanner.Services.Passive;

/// <summary>Reads the opendbc messages and signals that the catalog already ships (tables can_messages / can_signals).</summary>
public sealed class CatalogDbcSource(DtcCatalogService catalog) : IDbcSource
{
    private sealed class MessageRow
    {
        public string DbcFile { get; set; } = "";
        public int MessageId { get; set; }
        public string? MessageName { get; set; }
        public int? Dlc { get; set; }
    }

    private sealed class SignalRow
    {
        public string DbcFile { get; set; } = "";
        public int MessageId { get; set; }
        public string SignalName { get; set; } = "";
        public string? Multiplex { get; set; }
        public int? StartBit { get; set; }
        public int? BitLength { get; set; }
        public int? ByteOrder { get; set; }
        public int? IsSigned { get; set; }
        public double? Factor { get; set; }
        public double? Offset { get; set; }
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
        public string? Unit { get; set; }
    }

    public async Task<IReadOnlyList<DbcMessageInfo>> FindMessagesAsync(IReadOnlyCollection<int> messageIds, CancellationToken token)
    {
        if (messageIds.Count == 0)
            return Array.Empty<DbcMessageInfo>();

        await catalog.EnsureLoadedAsync(token).ConfigureAwait(false);
        var connection = new SQLiteAsyncConnection(catalog.DatabasePath, SQLiteOpenFlags.ReadOnly);
        try
        {
            // Only integers built here are placed in the statement.
            var list = string.Join(",", messageIds.Distinct().Select(id => id.ToString(CultureInfo.InvariantCulture)));
            var messages = await connection.QueryAsync<MessageRow>(
                "SELECT dbc_file AS DbcFile, message_id AS MessageId, message_name AS MessageName, dlc AS Dlc " +
                "FROM can_messages WHERE dlc IS NOT NULL AND message_id IN (" + list + ")").ConfigureAwait(false);
            var signals = await connection.QueryAsync<SignalRow>(
                "SELECT dbc_file AS DbcFile, message_id AS MessageId, signal_name AS SignalName, multiplex AS Multiplex, " +
                "start_bit AS StartBit, bit_length AS BitLength, byte_order AS ByteOrder, is_signed AS IsSigned, " +
                "factor AS Factor, \"offset\" AS Offset, minimum AS Minimum, maximum AS Maximum, unit AS Unit " +
                "FROM can_signals WHERE message_id IN (" + list + ")").ConfigureAwait(false);

            var grouped = signals
                .Where(s => string.IsNullOrEmpty(s.Multiplex) && s.StartBit is not null && s.BitLength is not null && s.Factor is not null)
                .GroupBy(s => (s.DbcFile, s.MessageId))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DbcSignal>)g.Select(s => new DbcSignal(
                    s.DbcFile, s.MessageId, s.SignalName, s.StartBit!.Value, s.BitLength!.Value,
                    s.ByteOrder != 1, s.IsSigned == 1, s.Factor!.Value, s.Offset ?? 0, s.Minimum, s.Maximum, s.Unit ?? "")).ToArray());

            return messages
                .Select(m => new DbcMessageInfo(m.DbcFile, m.MessageId, m.MessageName ?? "", m.Dlc ?? 0,
                    grouped.TryGetValue((m.DbcFile, m.MessageId), out var s) ? s : Array.Empty<DbcSignal>()))
                .ToArray();
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }
}
