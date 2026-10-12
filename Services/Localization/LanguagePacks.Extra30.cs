namespace CarScanner.Services.Localization;

// Batch 20: the explanation shown when a car feature is tapped without a connected adapter.
// Machine-written; native review needed. Order: tr, de, pl, es, pt, fr, it.
internal static partial class LanguagePacks
{
    private static IEnumerable<(string En, string[] T)> ExtraRows30() => new (string En, string[] T)[]
    {
        R("OBD adapter needed", "OBD adaptörü gerekli", "OBD-Adapter erforderlich", "Wymagany adapter OBD", "Se necesita un adaptador OBD", "É necessário um adaptador OBD", "Adaptateur OBD requis", "Serve un adattatore OBD"),
        R("This feature needs a connected OBD adapter. Open Adapter, connect it, and try again.", "Bu özellik bağlı bir OBD adaptörü gerektirir. Adaptör bölümünü aç, bağla ve tekrar dene.", "Diese Funktion benötigt einen verbundenen OBD-Adapter. Öffne „Adapter“, verbinde ihn und versuche es erneut.", "Ta funkcja wymaga podłączonego adaptera OBD. Otwórz Adapter, połącz go i spróbuj ponownie.", "Esta función necesita un adaptador OBD conectado. Abre Adaptador, conéctalo e inténtalo de nuevo.", "Esta função precisa de um adaptador OBD ligado. Abre Adaptador, liga-o e tenta novamente.", "Cette fonction nécessite un adaptateur OBD connecté. Ouvrez Adaptateur, connectez-le et réessayez.", "Questa funzione richiede un adattatore OBD collegato. Apri Adattatore, collegalo e riprova."),
    };
}
