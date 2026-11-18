namespace CarScanner.Services.Localization;

// Batch 40: the VIN history row on the Home screen.
//
// Home is the landing page now, so this label is seen by every user; it is also the
// one car function that works with no OBD adapter, which makes it the first thing a
// reviewer without hardware can actually tap. It has to read naturally in every
// supported language, so it gets a row of its own instead of falling back to English.
//
// Order, always: tr, de, pl, es, pt, fr, it.
internal static partial class LanguagePacks
{
    private static IEnumerable<(string En, string[] T)> ExtraRows40() => new (string En, string[] T)[]
    {
        R("VIN History",
          "VIN Geçmişi",
          "VIN-Verlauf",
          "Historia VIN",
          "Historial del VIN",
          "Histórico VIN",
          "Historique VIN",
          "Storico VIN"),
    };
}
