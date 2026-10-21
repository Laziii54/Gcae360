namespace CarScanner.Services.Localization;

// Batch 37: the opt-in "Raw capture" of the Adapter Center (support), its time left while it is on, and the copy-log message
// that says the capture was included. Georgian <-> English pairs are in LocalizationService.Extra.cs.
// Machine-written; native review needed. Order: tr, de, pl, es, pt, fr, it. Wording follows the existing "Connection log" rows.
internal static partial class LanguagePacks
{
    private static IEnumerable<(string En, string[] T)> ExtraRows37() => new (string En, string[] T)[]
    {
        R("Raw capture: OFF — tap to start (for support)",
          "Ham kayıt: KAPALI — başlatmak için dokun (destek için)",
          "Rohdaten-Aufzeichnung: AUS – zum Starten tippen (für den Support)",
          "Surowy zapis: WYŁ. — dotknij, aby rozpocząć (dla wsparcia)",
          "Captura sin procesar: DESACTIVADA; toca para iniciar (para soporte)",
          "Captura bruta: DESLIGADA — toque para iniciar (para suporte)",
          "Capture brute : DÉSACTIVÉE — touchez pour démarrer (pour le support)",
          "Acquisizione grezza: DISATTIVA — tocca per avviare (per l'assistenza)"),

        R("Raw capture: ON, about {0} min left — tap to stop",
          "Ham kayıt: AÇIK, yaklaşık {0} dk kaldı — durdurmak için dokun",
          "Rohdaten-Aufzeichnung: AN, noch ca. {0} Min. – zum Stoppen tippen",
          "Surowy zapis: WŁ., zostało około {0} min — dotknij, aby zatrzymać",
          "Captura sin procesar: ACTIVADA, quedan unos {0} min; toca para detener",
          "Captura bruta: LIGADA, faltam cerca de {0} min — toque para parar",
          "Capture brute : ACTIVÉE, environ {0} min restantes — touchez pour arrêter",
          "Acquisizione grezza: ATTIVA, circa {0} min rimasti — tocca per fermare"),

        R("Connection log and raw capture copied — send it to me.",
          "Bağlantı günlüğü ve ham kayıt kopyalandı — bana gönder.",
          "Verbindungsprotokoll und Rohdaten-Aufzeichnung kopiert — schick sie mir.",
          "Dziennik połączenia i surowy zapis skopiowane — wyślij mi je.",
          "Registro de conexión y captura sin procesar copiados: envíamelos.",
          "Registo de ligação e captura bruta copiados — envie-mos.",
          "Journal de connexion et capture brute copiés — envoie-les-moi.",
          "Registro di connessione e acquisizione grezza copiati — inviameli."),
    };
}
