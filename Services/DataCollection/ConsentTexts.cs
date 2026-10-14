using System.Globalization;

namespace CarScanner.Services.DataCollection;

/// <summary>All the words of the consent question and its Settings status line.</summary>
public sealed record ConsentText(
    string Title,
    string Body,
    string Agree,
    string NotNow,
    string SettingsTitle,
    string StatusNone,
    string StatusGranted,
    string StatusDeclined,
    string StatusRevoked);

/// <summary>
/// Consent texts in every language the app offers (ka, en, tr, de, pl, es, pt, fr, it). They describe what the
/// existing collection pipeline really does: read-only observations, VIN and standard identification numbers
/// filtered out, a random device code that makes the data pseudonymous rather than anonymous, and withdrawal in
/// Settings. Keep them in step with <c>CollectionSanitizer</c> and <c>DataCollectionService</c>.
/// </summary>
public static class ConsentTexts
{
    public static readonly IReadOnlyList<string> Codes = new[] { "ka", "en", "tr", "de", "pl", "es", "pt", "fr", "it" };

    private static string Join(params string[] paragraphs) => string.Join("\n\n", paragraphs);

    private static readonly Dictionary<string, ConsentText> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ka"] = new ConsentText(
            "გავაუმჯობესოთ OBD360 ერთად!",
            Join(
                "OBD360-ს სურს ავტომობილის დიაგნოსტიკის ტექნიკური მონაცემების შეგროვება, რათა გააუმჯობესოს სხვადასხვა მარკისა და მოდელის მხარდაჭერა.",
                "თანხმობის შემთხვევაში აპლიკაცია ავტომატურად გაგზავნის OBD360-ის სერვერზე მხოლოდ-წაკითხვის დიაგნოსტიკურ დაკვირვებებს: მანქანის მარკას, მოდელს, წელს და კავშირის ტიპს; რომელ სტანდარტულ OBD2 მოთხოვნებსა და მოდულებს პასუხობს მანქანა, მხარდაჭერილი PID-ების სიას და შეცდომის კოდებს; ასევე ცალკეული მოდულების ნედლ მხოლოდ-წაკითხვის პასუხებს, რომლებიც შეიძლება შეიცავდეს იმ წამს მოდულის მიერ დაბრუნებულ ტექნიკურ მნიშვნელობას. მანქანაში არაფერი იწერება და ამის გამო დამატებითი ბრძანებები არ იგზავნება.",
                "გაგზავნამდე აპი ფილტრავს VIN-ს და სტანდარტულ საიდენტიფიკაციო ნომრებს; სახელი, ანგარიში და ლოკაცია არ იგზავნება. იგზავნება შემთხვევითი მოწყობილობის კოდი, რათა განმეორებითი გაგზავნა ორჯერ არ დაითვალოს; ამიტომ მონაცემები ფსევდონიმურია და არა სრულად ანონიმური. მათ გამოვიყენებთ მხოლოდ იმის გასაგებად, რომელი დიაგნოსტიკური ბრძანებები მუშაობს, რომელი მოდულები პასუხობს და რას სჭირდება გაუმჯობესება; ისინი მხარდაჭერის სტატუსს თავისით არასდროს ცვლიან.",
                "მონაცემების შეგროვება ნებაყოფლობითია. თანხმობის გაუქმება ნებისმიერ დროს შეგიძლიათ პარამეტრებიდან: რიგში დარჩენილი მონაცემები წაიშლება და სერვერს მოვთხოვთ ამ მოწყობილობიდან გაგზავნილის წაშლას."),
            "თანახმა ვარ",
            "ახლა არა",
            "მანქანის დიაგნოსტიკური მონაცემების გაზიარება (არასავალდებულო)",
            "გადაწყვეტილება ჯერ არ არის მიღებული. შეკითხვა ერთხელ გაგიჩნდება.",
            "თანხმობა მიცემულია ({0}). ნებისმიერ დროს შეგიძლია აქ გამორთო.",
            "უარი ითქვა. კვლავ გკითხავთ ამ დროის შემდეგ: {0}.",
            "გამორთულია ({0}). ასე დარჩება, სანამ აქ არ ჩართავ."),

        ["en"] = new ConsentText(
            "Let's improve OBD360 together!",
            Join(
                "OBD360 would like to collect technical vehicle diagnostic data to improve support for different makes and models.",
                "If you agree, the app will automatically send read-only diagnostic observations to the OBD360 server: the vehicle make, model, year and connection type; which standard OBD2 requests and modules your car answers, the list of supported PIDs and trouble codes; and the raw read-only replies of individual modules, which can include a technical value a module reported at that moment. Nothing is written to your car, and no extra commands are sent because of this.",
                "Before sending, the app filters out the VIN and standard identification numbers; no name, account or location is sent. A random device code is sent so repeated reports are not counted twice, so the data is pseudonymous, not fully anonymous. We use it only to see which diagnostic commands work, which modules respond and what needs improvement; it never changes support claims by itself.",
                "Sharing is voluntary. You can withdraw at any time in Settings: queued data is deleted and we ask the server to delete what this device sent."),
            "I agree",
            "Not now",
            "Share car diagnostic data (optional)",
            "Not decided yet. You will be asked once.",
            "Allowed ({0}). You can turn it off here at any time.",
            "Declined. You will be asked again after: {0}.",
            "Turned off ({0}). It stays off until you turn it on here."),

        ["tr"] = new ConsentText(
            "OBD360'ı birlikte geliştirelim!",
            Join(
                "OBD360, farklı marka ve modellere desteği geliştirmek için aracın teknik tanılama verilerini toplamak istiyor.",
                "Kabul ederseniz uygulama, yalnızca okuma amaçlı tanılama gözlemlerini otomatik olarak OBD360 sunucusuna gönderir: aracın markası, modeli, yılı ve bağlantı türü; aracınızın hangi standart OBD2 isteklerine ve modüllerine yanıt verdiği, desteklenen PID listesi ve arıza kodları; ayrıca tek tek modüllerin ham, yalnızca okunan yanıtları (bir modülün o anda bildirdiği teknik bir değer içerebilir). Aracınıza hiçbir şey yazılmaz ve bunun için ek komut gönderilmez.",
                "Göndermeden önce uygulama VIN'i ve standart kimlik numaralarını filtreler; ad, hesap veya konum gönderilmez. Tekrarlanan raporlar iki kez sayılmasın diye rastgele bir cihaz kodu gönderilir; bu nedenle veriler takma adlıdır, tamamen anonim değildir. Verileri yalnızca hangi tanılama komutlarının çalıştığını, hangi modüllerin yanıt verdiğini ve nelerin iyileştirilmesi gerektiğini görmek için kullanırız; destek durumunu kendiliğinden asla değiştirmez.",
                "Paylaşım isteğe bağlıdır. İstediğiniz zaman Ayarlar'dan geri çekebilirsiniz: sırada bekleyen veriler silinir ve sunucudan bu cihazın gönderdiklerini silmesini isteriz."),
            "Kabul ediyorum",
            "Şimdi değil",
            "Araç tanılama verilerini paylaş (isteğe bağlı)",
            "Henüz karar verilmedi. Bir kez sorulacak.",
            "İzin verildi ({0}). Dilediğiniz zaman buradan kapatabilirsiniz.",
            "Reddedildi. Şu zamandan sonra tekrar sorulacak: {0}.",
            "Kapatıldı ({0}). Burada açana kadar kapalı kalır."),

        ["de"] = new ConsentText(
            "Verbessern wir OBD360 gemeinsam!",
            Join(
                "OBD360 möchte technische Fahrzeugdiagnosedaten erfassen, um die Unterstützung verschiedener Marken und Modelle zu verbessern.",
                "Wenn Sie zustimmen, sendet die App automatisch rein lesende Diagnosebeobachtungen an den OBD360-Server: Marke, Modell, Baujahr und Verbindungsart des Fahrzeugs; welche Standard-OBD2-Anfragen und Module Ihr Fahrzeug beantwortet, die Liste der unterstützten PIDs und Fehlercodes; sowie die rohen, rein lesenden Antworten einzelner Module, die einen technischen Wert enthalten können, den ein Modul in diesem Moment gemeldet hat. Es wird nichts in Ihr Fahrzeug geschrieben, und deswegen werden keine zusätzlichen Befehle gesendet.",
                "Vor dem Senden filtert die App die Fahrzeug-Identifizierungsnummer (VIN) und Standard-Identifikationsnummern heraus; Name, Konto oder Standort werden nicht gesendet. Ein zufälliger Gerätecode wird gesendet, damit wiederholte Meldungen nicht doppelt gezählt werden; die Daten sind daher pseudonym, nicht vollständig anonym. Wir nutzen sie nur, um zu sehen, welche Diagnosebefehle funktionieren, welche Module antworten und was verbessert werden muss; sie ändern Unterstützungsaussagen nie von selbst.",
                "Die Teilnahme ist freiwillig. Sie können jederzeit in den Einstellungen widerrufen: wartende Daten werden gelöscht, und wir bitten den Server, die von diesem Gerät gesendeten Daten zu löschen."),
            "Ich stimme zu",
            "Jetzt nicht",
            "Fahrzeugdiagnosedaten teilen (optional)",
            "Noch nicht entschieden. Sie werden einmal gefragt.",
            "Erlaubt ({0}). Sie können es hier jederzeit ausschalten.",
            "Abgelehnt. Erneute Frage nach: {0}.",
            "Ausgeschaltet ({0}). Bleibt aus, bis Sie es hier einschalten."),

        ["pl"] = new ConsentText(
            "Ulepszmy OBD360 razem!",
            Join(
                "OBD360 chce zbierać techniczne dane diagnostyczne pojazdu, aby ulepszyć obsługę różnych marek i modeli.",
                "Jeśli wyrazisz zgodę, aplikacja automatycznie wyśle na serwer OBD360 obserwacje diagnostyczne tylko do odczytu: markę, model, rok i typ połączenia pojazdu; na które standardowe zapytania OBD2 i moduły odpowiada samochód, listę obsługiwanych PID i kody błędów; a także surowe odpowiedzi poszczególnych modułów tylko do odczytu, które mogą zawierać wartość techniczną zgłoszoną przez moduł w danej chwili. Do samochodu nic nie jest zapisywane, a z tego powodu nie są wysyłane żadne dodatkowe polecenia.",
                "Przed wysłaniem aplikacja odfiltrowuje numer VIN i standardowe numery identyfikacyjne; nie jest wysyłane imię, konto ani lokalizacja. Wysyłany jest losowy kod urządzenia, aby powtórne zgłoszenia nie były liczone podwójnie, dlatego dane są pseudonimowe, a nie w pełni anonimowe. Używamy ich tylko do sprawdzenia, które polecenia diagnostyczne działają, które moduły odpowiadają i co wymaga poprawy; same nigdy nie zmieniają deklaracji o obsłudze.",
                "Udostępnianie jest dobrowolne. W każdej chwili możesz je wycofać w Ustawieniach: dane w kolejce zostaną usunięte, a my poprosimy serwer o usunięcie danych wysłanych z tego urządzenia."),
            "Zgadzam się",
            "Nie teraz",
            "Udostępniaj dane diagnostyczne pojazdu (opcjonalnie)",
            "Jeszcze nie zdecydowano. Zostaniesz zapytany jeden raz.",
            "Zgoda udzielona ({0}). Możesz ją tu wyłączyć w dowolnym momencie.",
            "Odmówiono. Zapytamy ponownie po: {0}.",
            "Wyłączono ({0}). Pozostanie wyłączone, dopóki nie włączysz tutaj."),

        ["es"] = new ConsentText(
            "¡Mejoremos OBD360 juntos!",
            Join(
                "OBD360 quiere recopilar datos técnicos de diagnóstico del vehículo para mejorar la compatibilidad con distintas marcas y modelos.",
                "Si aceptas, la app enviará automáticamente al servidor de OBD360 observaciones de diagnóstico de solo lectura: marca, modelo, año y tipo de conexión del vehículo; a qué solicitudes OBD2 estándar y módulos responde tu coche, la lista de PID admitidos y los códigos de avería; y las respuestas brutas de solo lectura de cada módulo, que pueden incluir un valor técnico que el módulo informó en ese momento. No se escribe nada en tu coche y por ello no se envían comandos adicionales.",
                "Antes de enviar, la app filtra el VIN y los números de identificación estándar; no se envía nombre, cuenta ni ubicación. Se envía un código de dispositivo aleatorio para que los envíos repetidos no se cuenten dos veces, así que los datos son seudónimos, no totalmente anónimos. Solo los usamos para ver qué comandos de diagnóstico funcionan, qué módulos responden y qué hay que mejorar; nunca cambian por sí solos la compatibilidad declarada.",
                "Compartir es voluntario. Puedes retirar el consentimiento en cualquier momento en Ajustes: los datos en cola se borran y pedimos al servidor que elimine lo que envió este dispositivo."),
            "Acepto",
            "Ahora no",
            "Compartir datos de diagnóstico del coche (opcional)",
            "Aún sin decidir. Se te preguntará una vez.",
            "Permitido ({0}). Puedes desactivarlo aquí en cualquier momento.",
            "Rechazado. Se volverá a preguntar después de: {0}.",
            "Desactivado ({0}). Seguirá así hasta que lo actives aquí."),

        ["pt"] = new ConsentText(
            "Vamos melhorar o OBD360 juntos!",
            Join(
                "O OBD360 quer recolher dados técnicos de diagnóstico do veículo para melhorar o suporte a diferentes marcas e modelos.",
                "Se concordar, a app enviará automaticamente ao servidor do OBD360 observações de diagnóstico só de leitura: marca, modelo, ano e tipo de ligação do veículo; a que pedidos OBD2 padrão e módulos o seu carro responde, a lista de PIDs suportados e os códigos de avaria; e as respostas brutas só de leitura de cada módulo, que podem incluir um valor técnico que o módulo comunicou nesse momento. Nada é escrito no seu carro e, por isso, não são enviados comandos adicionais.",
                "Antes de enviar, a app filtra o VIN e os números de identificação padrão; não são enviados nome, conta nem localização. É enviado um código de dispositivo aleatório para que envios repetidos não sejam contados duas vezes, pelo que os dados são pseudónimos e não totalmente anónimos. Usamo-los apenas para ver que comandos de diagnóstico funcionam, que módulos respondem e o que precisa de melhorar; nunca alteram por si só as indicações de compatibilidade.",
                "A partilha é voluntária. Pode retirar o consentimento a qualquer momento nas Definições: os dados em fila são apagados e pedimos ao servidor que elimine o que este dispositivo enviou."),
            "Concordo",
            "Agora não",
            "Partilhar dados de diagnóstico do carro (opcional)",
            "Ainda por decidir. Será perguntado uma vez.",
            "Permitido ({0}). Pode desativar aqui a qualquer momento.",
            "Recusado. Será perguntado novamente depois de: {0}.",
            "Desativado ({0}). Fica assim até o ativar aqui."),

        ["fr"] = new ConsentText(
            "Améliorons OBD360 ensemble !",
            Join(
                "OBD360 souhaite collecter des données techniques de diagnostic du véhicule afin d'améliorer la prise en charge de différentes marques et modèles.",
                "Si vous acceptez, l'application enverra automatiquement au serveur OBD360 des observations de diagnostic en lecture seule : la marque, le modèle, l'année et le type de connexion du véhicule ; les requêtes OBD2 standard et les modules auxquels votre voiture répond, la liste des PID pris en charge et les codes défaut ; ainsi que les réponses brutes en lecture seule de chaque module, qui peuvent contenir une valeur technique signalée par le module à cet instant. Rien n'est écrit dans votre voiture et aucune commande supplémentaire n'est envoyée pour cela.",
                "Avant l'envoi, l'application filtre le VIN et les numéros d'identification standard ; aucun nom, compte ni position n'est envoyé. Un code d'appareil aléatoire est envoyé pour que les envois répétés ne soient pas comptés deux fois : les données sont donc pseudonymisées, pas totalement anonymes. Nous les utilisons uniquement pour voir quelles commandes de diagnostic fonctionnent, quels modules répondent et ce qu'il faut améliorer ; elles ne modifient jamais d'elles-mêmes les indications de compatibilité.",
                "Le partage est volontaire. Vous pouvez le retirer à tout moment dans les Réglages : les données en attente sont supprimées et nous demandons au serveur de supprimer ce que cet appareil a envoyé."),
            "J'accepte",
            "Pas maintenant",
            "Partager les données de diagnostic de la voiture (facultatif)",
            "Pas encore décidé. La question vous sera posée une fois.",
            "Autorisé ({0}). Vous pouvez le désactiver ici à tout moment.",
            "Refusé. La question sera reposée après : {0}.",
            "Désactivé ({0}). Reste désactivé tant que vous ne l'activez pas ici."),

        ["it"] = new ConsentText(
            "Miglioriamo OBD360 insieme!",
            Join(
                "OBD360 vorrebbe raccogliere dati tecnici di diagnostica del veicolo per migliorare il supporto a diverse marche e modelli.",
                "Se accetti, l'app invierà automaticamente al server di OBD360 osservazioni diagnostiche di sola lettura: marca, modello, anno e tipo di connessione del veicolo; a quali richieste OBD2 standard e moduli risponde la tua auto, l'elenco dei PID supportati e i codici di guasto; e le risposte grezze di sola lettura dei singoli moduli, che possono includere un valore tecnico segnalato dal modulo in quel momento. Nulla viene scritto nella tua auto e per questo non vengono inviati comandi aggiuntivi.",
                "Prima dell'invio, l'app filtra il VIN e i numeri di identificazione standard; non vengono inviati nome, account o posizione. Viene inviato un codice dispositivo casuale perché gli invii ripetuti non siano contati due volte, quindi i dati sono pseudonimi, non completamente anonimi. Li usiamo solo per vedere quali comandi diagnostici funzionano, quali moduli rispondono e cosa va migliorato; non modificano mai da soli le indicazioni di compatibilità.",
                "La condivisione è volontaria. Puoi revocarla in qualsiasi momento nelle Impostazioni: i dati in coda vengono eliminati e chiediamo al server di eliminare quanto inviato da questo dispositivo."),
            "Accetto",
            "Non ora",
            "Condividi i dati di diagnostica dell'auto (facoltativo)",
            "Non ancora deciso. Ti verrà chiesto una volta.",
            "Consentito ({0}). Puoi disattivarlo qui in qualsiasi momento.",
            "Rifiutato. Verrà richiesto di nuovo dopo: {0}.",
            "Disattivato ({0}). Resta disattivato finché non lo attivi qui.")
    };

    /// <summary>The texts for a language code; an unknown or empty code gives English.</summary>
    public static ConsentText For(string? code) =>
        code is not null && Texts.TryGetValue(code.Trim(), out var text) ? text : Texts["en"];

    /// <summary>The one-line status shown in Settings for the current consent state.</summary>
    public static string Status(ConsentText text, ConsentPolicy policy)
    {
        static string Date(DateTimeOffset? value) =>
            value is null ? string.Empty : value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

        // Consent given before timestamps were kept has no date: drop the empty brackets instead of showing "()".
        static string NoEmptyDate(string line) => line.Replace(" ()", string.Empty, StringComparison.Ordinal);

        return policy.State switch
        {
            ConsentState.Granted => NoEmptyDate(string.Format(CultureInfo.InvariantCulture, text.StatusGranted, Date(policy.GrantedAt))),
            ConsentState.DeclinedWaiting => string.Format(CultureInfo.InvariantCulture, text.StatusDeclined, Date(policy.NextPromptAt)),
            ConsentState.Revoked => NoEmptyDate(string.Format(CultureInfo.InvariantCulture, text.StatusRevoked, Date(policy.RevokedAt))),
            _ => text.StatusNone
        };
    }
}
