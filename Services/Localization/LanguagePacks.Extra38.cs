namespace CarScanner.Services.Localization;

// Batch 38: the sentences of the hybrid battery check that tell why a Volt / Bolt check ended without a result (setup command not
// accepted, module silent, another module answered, reply could not be read, too few cells, link lost). Georgian <-> English pairs
// are in LocalizationService.Extra.cs. Machine-written; native review needed. Order: tr, de, pl, es, pt, fr, it.
internal static partial class LanguagePacks
{
    private static IEnumerable<(string En, string[] T)> ExtraRows38() => new (string En, string[] T)[]
    {
        R("This adapter does not accept a setting the battery check needs ({0}). Try a different adapter.",
          "Bu adaptör batarya kontrolünün gerektirdiği bir ayarı kabul etmiyor ({0}). Farklı bir adaptör deneyin.",
          "Dieser Adapter akzeptiert eine Einstellung nicht, die die Batterieprüfung benötigt ({0}). Versuche einen anderen Adapter.",
          "Ten adapter nie przyjmuje ustawienia potrzebnego do sprawdzenia baterii ({0}). Spróbuj innego adaptera.",
          "Este adaptador no acepta un ajuste que necesita la comprobación de la batería ({0}). Prueba con otro adaptador.",
          "Este adaptador não aceita uma definição de que a verificação da bateria precisa ({0}). Experimente outro adaptador.",
          "Cet adaptateur n'accepte pas un réglage nécessaire au contrôle de la batterie ({0}). Essayez un autre adaptateur.",
          "Questo adattatore non accetta un'impostazione necessaria al controllo della batteria ({0}). Prova un altro adattatore."),

        R("The battery module did not respond to the requests. There can be several causes — for example the car is not switched on (READY), or the adapter cannot reach this module. Check the car's state and try again.",
          "Batarya modülü isteklere yanıt vermedi. Birkaç neden olabilir — örneğin araç açık değil (READY) veya adaptör bu modüle ulaşamıyor. Aracın durumunu kontrol edip tekrar deneyin.",
          "Das Batteriemodul hat auf die Anfragen nicht geantwortet. Es kann mehrere Ursachen geben — zum Beispiel ist das Auto nicht eingeschaltet (READY) oder der Adapter erreicht dieses Modul nicht. Prüfe den Zustand des Autos und versuche es erneut.",
          "Moduł baterii nie odpowiedział na zapytania. Przyczyn może być kilka — na przykład samochód nie jest włączony (READY) albo adapter nie może dotrzeć do tego modułu. Sprawdź stan samochodu i spróbuj ponownie.",
          "El módulo de la batería no respondió a las solicitudes. Puede haber varias causas; por ejemplo, el coche no está encendido (READY) o el adaptador no puede llegar a este módulo. Comprueba el estado del coche y vuelve a intentarlo.",
          "O módulo da bateria não respondeu aos pedidos. Pode haver várias causas — por exemplo, o carro não está ligado (READY) ou o adaptador não consegue chegar a este módulo. Verifique o estado do carro e tente novamente.",
          "Le module de la batterie n'a pas répondu aux requêtes. Il peut y avoir plusieurs causes — par exemple la voiture n'est pas allumée (READY) ou l'adaptateur ne peut pas atteindre ce module. Vérifiez l'état de la voiture et réessayez.",
          "Il modulo della batteria non ha risposto alle richieste. Possono esserci più cause — per esempio l'auto non è accesa (READY) oppure l'adattatore non riesce a raggiungere questo modulo. Controlla lo stato dell'auto e riprova."),

        R("The adapter returned a reply from another module, not from the battery module. It may not support addressing a specific module.",
          "Adaptör, batarya modülünden değil başka bir modülden yanıt döndürdü. Belirli bir modüle adres vermeyi desteklemiyor olabilir.",
          "Der Adapter hat eine Antwort von einem anderen Modul geliefert, nicht vom Batteriemodul. Er unterstützt das Ansprechen eines bestimmten Moduls möglicherweise nicht.",
          "Adapter zwrócił odpowiedź z innego modułu, a nie z modułu baterii. Może nie obsługiwać adresowania konkretnego modułu.",
          "El adaptador devolvió una respuesta de otro módulo, no del módulo de la batería. Puede que no admita dirigirse a un módulo concreto.",
          "O adaptador devolveu uma resposta de outro módulo, não do módulo da bateria. Pode não suportar o endereçamento de um módulo específico.",
          "L'adaptateur a renvoyé une réponse d'un autre module, et non du module de la batterie. Il ne prend peut-être pas en charge l'adressage d'un module précis.",
          "L'adattatore ha restituito una risposta da un altro modulo, non dal modulo della batteria. Potrebbe non supportare l'indirizzamento di un modulo specifico."),

        R("A reply came back from the battery module, but it could not be read (damaged or unexpected format). Try again; if it keeps happening, send the connection log from the Adapter page.",
          "Batarya modülünden bir yanıt geldi ancak okunamadı (bozuk veya beklenmeyen biçim). Tekrar deneyin; devam ederse Adaptör sayfasındaki bağlantı günlüğünü gönderin.",
          "Vom Batteriemodul kam eine Antwort, sie konnte aber nicht gelesen werden (beschädigt oder unerwartetes Format). Versuche es erneut; wenn es weiter auftritt, sende das Verbindungsprotokoll von der Adapter-Seite.",
          "Z modułu baterii przyszła odpowiedź, ale nie dało się jej odczytać (uszkodzona lub nieoczekiwany format). Spróbuj ponownie; jeśli problem się powtarza, wyślij dziennik połączenia ze strony Adapter.",
          "Llegó una respuesta del módulo de la batería, pero no se pudo leer (dañada o con un formato inesperado). Vuelve a intentarlo; si sigue ocurriendo, envía el registro de conexión de la página Adaptador.",
          "Chegou uma resposta do módulo da bateria, mas não foi possível lê-la (danificada ou em formato inesperado). Tente novamente; se continuar a acontecer, envie o registo de ligação da página Adaptador.",
          "Une réponse est arrivée du module de la batterie, mais elle n'a pas pu être lue (endommagée ou format inattendu). Réessayez ; si cela continue, envoyez le journal de connexion de la page Adaptateur.",
          "È arrivata una risposta dal modulo della batteria, ma non è stato possibile leggerla (danneggiata o in formato inatteso). Riprova; se continua, invia il registro di connessione dalla pagina Adattatore."),

        R("Only {0} of the battery's 96 cells returned a valid value — not enough for a reliable result. Try again; if the same cells keep missing, send the connection log from the Adapter page.",
          "Bataryanın 96 hücresinden yalnızca {0} tanesi geçerli bir değer döndürdü — güvenilir bir sonuç için yeterli değil. Tekrar deneyin; aynı hücreler eksik kalırsa Adaptör sayfasındaki bağlantı günlüğünü gönderin.",
          "Nur {0} der 96 Batteriezellen haben einen gültigen Wert geliefert — zu wenig für ein verlässliches Ergebnis. Versuche es erneut; wenn dieselben Zellen weiter fehlen, sende das Verbindungsprotokoll von der Adapter-Seite.",
          "Tylko {0} z 96 ogniw baterii zwróciło prawidłową wartość — to za mało na wiarygodny wynik. Spróbuj ponownie; jeśli wciąż brakuje tych samych ogniw, wyślij dziennik połączenia ze strony Adapter.",
          "Solo {0} de las 96 celdas de la batería devolvieron un valor válido; no es suficiente para un resultado fiable. Vuelve a intentarlo; si siguen faltando las mismas celdas, envía el registro de conexión de la página Adaptador.",
          "Apenas {0} das 96 células da bateria devolveram um valor válido — insuficiente para um resultado fiável. Tente novamente; se faltarem sempre as mesmas células, envie o registo de ligação da página Adaptador.",
          "Seules {0} des 96 cellules de la batterie ont renvoyé une valeur valide — insuffisant pour un résultat fiable. Réessayez ; si les mêmes cellules manquent toujours, envoyez le journal de connexion de la page Adaptateur.",
          "Solo {0} delle 96 celle della batteria hanno restituito un valore valido — non bastano per un risultato affidabile. Riprova; se mancano sempre le stesse celle, invia il registro di connessione dalla pagina Adattatore."),

        R("The connection to the adapter was lost during the check. Reconnect the adapter and try again.",
          "Kontrol sırasında adaptörle bağlantı kesildi. Adaptörü yeniden bağlayıp tekrar deneyin.",
          "Die Verbindung zum Adapter ist während der Prüfung abgebrochen. Verbinde den Adapter erneut und versuche es noch einmal.",
          "Podczas sprawdzania utracono połączenie z adapterem. Połącz adapter ponownie i spróbuj jeszcze raz.",
          "Se perdió la conexión con el adaptador durante la comprobación. Vuelve a conectar el adaptador e inténtalo de nuevo.",
          "A ligação ao adaptador perdeu-se durante a verificação. Volte a ligar o adaptador e tente novamente.",
          "La connexion à l'adaptateur a été perdue pendant le contrôle. Reconnectez l'adaptateur et réessayez.",
          "La connessione all'adattatore è stata persa durante il controllo. Ricollega l'adattatore e riprova."),
    };
}
