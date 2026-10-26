namespace CarScanner.Services.Localization;

// Batch 39: strings the redesigned PRO subscription page needed that earlier batches did not already
// cover - the "Upgrade to PRO" intro, a disambiguated "Current plan" tag (the bare word "Current" is
// already a pack key for the electrical sense), the CTA/price templates that interpolate the plan name
// or a number before translation, and the iOS renewal + account-sync sentence, which ProViewModel
// concatenates from two already-translated pieces into one string that needs its own exact-match row.
// Machine-written; native review needed. Order: tr, de, pl, es, pt, fr, it.
internal static partial class LanguagePacks
{
    private static IEnumerable<(string En, string[] T)> ExtraRows39() => new (string En, string[] T)[]
    {
        R("Upgrade to PRO",
          "PRO'ya yükselt",
          "Auf PRO upgraden",
          "Przejdź na PRO",
          "Mejora a PRO",
          "Atualizar para PRO",
          "Passer à PRO",
          "Passa a PRO"),

        R("Full diagnostics, VIN history and AI consultation — unlocked on your account, on every device.",
          "Tam diagnostik, VIN geçmişi ve AI danışmanlığı — hesabınızda, her cihazda açık.",
          "Volle Diagnose, VIN-Verlauf und KI-Beratung — auf deinem Konto freigeschaltet, auf jedem Gerät.",
          "Pełna diagnostyka, historia VIN i konsultacja AI — odblokowane na Twoim koncie, na każdym urządzeniu.",
          "Diagnóstico completo, historial VIN y consulta con IA — desbloqueado en tu cuenta, en todos los dispositivos.",
          "Diagnóstico completo, histórico VIN e consulta com IA — desbloqueado na sua conta, em todos os dispositivos.",
          "Diagnostic complet, historique VIN et consultation IA — débloqués sur votre compte, sur chaque appareil.",
          "Diagnostica completa, storico VIN e consulenza IA — sbloccati sul tuo account, su ogni dispositivo."),

        // "Current plan" (not the bare word "Current" - that key already means the electrical
        // sense elsewhere in the app). Tag shown on the plan card the user already owns.
        R("Current plan",
          "Mevcut plan",
          "Aktueller Tarif",
          "Aktualny plan",
          "Plan actual",
          "Plano atual",
          "Offre actuelle",
          "Piano attuale"),

        // CTA button templates: {0} is the plan's own title (OBD PRO / VIN PRO / Full PRO),
        // which stays in Latin script in every language, same as everywhere else on this page.
        R("Start {0} — Monthly",
          "{0} Başlat — Aylık",
          "{0} starten — Monatlich",
          "Zacznij {0} — Miesięcznie",
          "Empezar {0} — Mensual",
          "Começar {0} — Mensal",
          "Démarrer {0} — Mensuel",
          "Inizia {0} — Mensile"),

        R("Start {0} — Yearly",
          "{0} Başlat — Yıllık",
          "{0} starten — Jährlich",
          "Zacznij {0} — Rocznie",
          "Empezar {0} — Anual",
          "Começar {0} — Anual",
          "Démarrer {0} — Annuel",
          "Inizia {0} — Annuale"),

        // Yearly price line under the big price (per-month equivalent of the yearly charge).
        R("per year · {0} ₾/mo",
          "yıllık · {0} ₾/ay",
          "pro Jahr · {0} ₾/Monat",
          "rocznie · {0} ₾/mies.",
          "al año · {0} ₾/mes",
          "por ano · {0} ₾/mês",
          "par an · {0} ₾/mois",
          "all'anno · {0} ₾/mese"),

        // The iOS "renews monthly" sentence and the account-sync sentence are each already
        // translated on their own (Extra14/Extra15), but ProViewModel concatenates them into one
        // string before display, so the combined sentence needs its own exact-match row too.
        R("The subscription renews every month until you cancel it in the App Store. PRO plans and extra VIN checks are stored on your account and work on every device where you sign in with it.",
          "Abonelik, App Store’da iptal edene kadar her ay yenilenir. PRO planları ve ek VIN kontrolleri hesabında saklanır ve onunla giriş yaptığın her cihazda çalışır.",
          "Das Abo verlängert sich jeden Monat, bis du es im App Store kündigst. PRO-Tarife und zusätzliche VIN-Prüfungen werden in deinem Konto gespeichert und funktionieren auf jedem Gerät, auf dem du dich damit anmeldest.",
          "Subskrypcja odnawia się co miesiąc, dopóki nie anulujesz jej w App Store. Plany PRO i dodatkowe sprawdzenia VIN są zapisane na Twoim koncie i działają na każdym urządzeniu, na którym się zalogujesz.",
          "La suscripción se renueva cada mes hasta que la canceles en la App Store. Los planes PRO y las comprobaciones VIN adicionales se guardan en tu cuenta y funcionan en cada dispositivo donde inicies sesión con ella.",
          "A subscrição renova-se todos os meses até a cancelar na App Store. Os planos PRO e as verificações VIN adicionais ficam guardados na sua conta e funcionam em todos os dispositivos onde iniciar sessão com ela.",
          "L’abonnement se renouvelle chaque mois jusqu’à ce que vous l’annuliez dans l’App Store. Les offres PRO et les vérifications VIN supplémentaires sont enregistrées sur votre compte et fonctionnent sur chaque appareil où vous vous connectez avec lui.",
          "L’abbonamento si rinnova ogni mese finché non lo annulli nell’App Store. I piani PRO e i controlli VIN aggiuntivi sono salvati sul tuo account e funzionano su ogni dispositivo in cui accedi con esso."),

        // {0} is the store name (App Store / Google Play), which stays untranslated like
        // everywhere else in the app. Reachable on iOS when a product id (e.g. a yearly plan)
        // is not yet created in App Store Connect - the old text always said "Google Play"
        // even when StoreName was "App Store".
        R("This offer is not available in {0} yet.",
          "Bu teklif henüz {0}'te mevcut değil.",
          "Dieses Angebot ist in {0} noch nicht verfügbar.",
          "Ta oferta nie jest jeszcze dostępna w {0}.",
          "Esta oferta aún no está disponible en {0}.",
          "Esta oferta ainda não está disponível na {0}.",
          "Cette offre n’est pas encore disponible sur {0}.",
          "Questa offerta non è ancora disponibile su {0}."),

        R("{0} is temporarily unavailable. Check your connection and try again.",
          "{0} şu anda kullanılamıyor. Bağlantını kontrol edip tekrar dene.",
          "{0} ist vorübergehend nicht verfügbar. Prüfe deine Verbindung und versuche es erneut.",
          "{0} jest tymczasowo niedostępny. Sprawdź połączenie i spróbuj ponownie.",
          "{0} no está disponible temporalmente. Comprueba tu conexión e inténtalo de nuevo.",
          "{0} está temporariamente indisponível. Verifique a sua ligação e tente novamente.",
          "{0} est temporairement indisponible. Vérifiez votre connexion et réessayez.",
          "{0} non è temporaneamente disponibile. Controlla la connessione e riprova."),
    };
}
