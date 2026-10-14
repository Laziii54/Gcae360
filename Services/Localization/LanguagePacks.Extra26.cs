namespace CarScanner.Services.Localization;

// Batch 16: option values shown in pickers (Settings, Fuel). The stored value stays English; only the displayed text is translated.
// Order: tr, de, pl, es, pt, fr, it.
internal static partial class LanguagePacks
{
    private static IEnumerable<(string En, string[] T)> ExtraRows26() => new (string En, string[] T)[]
    {
        R("Dark", "Koyu", "Dunkel", "Ciemny", "Oscuro", "Escuro", "Sombre", "Scuro"),
        R("Light", "Açık", "Hell", "Jasny", "Claro", "Claro", "Clair", "Chiaro"),
        R("System", "Sistem", "System", "Systemowy", "Sistema", "Sistema", "Système", "Sistema"),
        R("Metric", "Metrik", "Metrisch", "Metryczne", "Métrico", "Métrico", "Métrique", "Metrico"),
        R("Imperial", "İngiliz birimleri", "Imperial", "Imperialne", "Imperial", "Imperial", "Impérial", "Imperiale"),
        R("Fast", "Hızlı", "Schnell", "Szybko", "Rápido", "Rápido", "Rapide", "Veloce"),
        R("Balanced", "Dengeli", "Ausgewogen", "Zrównoważony", "Equilibrado", "Equilibrado", "Équilibré", "Bilanciato"),
        R("Auto", "Otomatik", "Automatisch", "Automatycznie", "Automático", "Automático", "Automatique", "Automatico"),
        R("LPG", "LPG", "LPG", "LPG", "GLP", "GPL", "GPL", "GPL"),
        R("MPG (US)", "MPG (ABD)", "MPG (US)", "MPG (USA)", "MPG (EE. UU.)", "MPG (EUA)", "MPG (US)", "MPG (USA)"),
        R("Update", "Güncelle", "Aktualisieren", "Aktualizuj", "Actualizar", "Atualizar", "Mettre à jour", "Aggiorna"),
        R("Updating…", "Güncelleniyor…", "Wird aktualisiert …", "Aktualizuję…", "Actualizando…", "A atualizar…", "Mise à jour…", "Aggiornamento…"),
        R("Loading…", "Yükleniyor…", "Wird geladen …", "Wczytywanie…", "Cargando…", "A carregar…", "Chargement…", "Caricamento…"),
        R("Loading...", "Yükleniyor...", "Wird geladen ...", "Wczytywanie...", "Cargando...", "A carregar...", "Chargement...", "Caricamento..."),
        R("Retry", "Tekrar dene", "Erneut versuchen", "Ponów", "Reintentar", "Tentar novamente", "Réessayer", "Riprova"),
        R("Start", "Başlat", "Start", "Start", "Iniciar", "Iniciar", "Démarrer", "Avvia"),
        R("Stop", "Durdur", "Stopp", "Stop", "Detener", "Parar", "Arrêter", "Ferma"),
        R("Next", "İleri", "Weiter", "Dalej", "Siguiente", "Seguinte", "Suivant", "Avanti"),
        R("Waiting…", "Bekleniyor…", "Warte …", "Czekam…", "Esperando…", "A aguardar…", "En attente…", "In attesa…"),
        R("Running", "Çalışıyor", "Läuft", "Działa", "En ejecución", "Em execução", "En cours", "In esecuzione"),
        R("Offline", "Çevrimdışı", "Offline", "Offline", "Sin conexión", "Offline", "Hors ligne", "Offline"),
        R("Online", "Çevrimiçi", "Online", "Online", "En línea", "Online", "En ligne", "Online"),
        R("Never", "Hiçbir zaman", "Nie", "Nigdy", "Nunca", "Nunca", "Jamais", "Mai"),
        R("Refreshing…", "Yenileniyor…", "Wird aktualisiert …", "Odświeżam…", "Actualizando…", "A atualizar…", "Actualisation…", "Aggiornamento…"),
        R("Please wait…", "Lütfen bekle…", "Bitte warten …", "Proszę czekać…", "Espera, por favor…", "Aguarde, por favor…", "Veuillez patienter…", "Attendere prego…"),
        R("No data yet", "Henüz veri yok", "Noch keine Daten", "Brak jeszcze danych", "Aún no hay datos", "Ainda sem dados", "Pas encore de données", "Ancora nessun dato"),
        R("Reconnecting…", "Yeniden bağlanıyor…", "Verbindung wird wiederhergestellt …", "Łączę ponownie…", "Reconectando…", "A religar…", "Reconnexion…", "Riconnessione…"),
        R("MPG (UK)", "MPG (BK)", "MPG (UK)", "MPG (Wlk. Brytania)", "MPG (Reino Unido)", "MPG (Reino Unido)", "MPG (R.-U.)", "MPG (Regno Unito)"),
    };
}
