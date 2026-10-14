namespace CarScanner.Services.Localization;

// Batch 18: accessibility labels of the icon-only buttons that replaced glyph buttons (Controls/IconButton).
// Order: tr, de, pl, es, pt, fr, it.
internal static partial class LanguagePacks
{
    private static IEnumerable<(string En, string[] T)> ExtraRows28() => new (string En, string[] T)[]
    {
        R("Delete backup", "Yedeği sil", "Sicherung löschen", "Usuń kopię zapasową", "Eliminar copia de seguridad", "Eliminar cópia de segurança", "Supprimer la sauvegarde", "Elimina il backup"),
        R("Add marker", "İşaret ekle", "Markierung hinzufügen", "Dodaj znacznik", "Añadir marcador", "Adicionar marcador", "Ajouter un repère", "Aggiungi indicatore"),
        R("Channels", "Kanallar", "Kanäle", "Kanały", "Canales", "Canais", "Canaux", "Canali"),
        R("Full screen", "Tam ekran", "Vollbild", "Pełny ekran", "Pantalla completa", "Ecrã inteiro", "Plein écran", "Schermo intero"),
        R("Pause / resume", "Duraklat / devam et", "Pause / fortsetzen", "Pauza / wznów", "Pausar / reanudar", "Pausar / retomar", "Pause / reprendre", "Pausa / riprendi"),
        R("Previous photo", "Önceki fotoğraf", "Vorheriges Foto", "Poprzednie zdjęcie", "Foto anterior", "Foto anterior", "Photo précédente", "Foto precedente"),
        R("Next photo", "Sonraki fotoğraf", "Nächstes Foto", "Następne zdjęcie", "Foto siguiente", "Foto seguinte", "Photo suivante", "Foto successiva"),
    };
}
