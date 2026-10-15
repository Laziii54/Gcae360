namespace CarScanner.Services.Localization;

// Batch 32: the admin-only "Research" page (Home tile, ResearchDataViewModel, ResearchDataPage.xaml).
// Machine-written; native review needed. Order: tr, de, pl, es, pt, fr, it.
internal static partial class LanguagePacks
{
    private static IEnumerable<(string En, string[] T)> ExtraRows32() => new (string En, string[] T)[]
    {
        R("Research",
          "Araştırma",
          "Forschung",
          "Badania",
          "Investigación",
          "Pesquisa",
          "Recherche",
          "Ricerca"),

        R("Checking unverified signals",
          "Doğrulanmamış sinyaller kontrol ediliyor",
          "Unbestätigte Signale werden geprüft",
          "Sprawdzanie niezweryfikowanych sygnałów",
          "Comprobando señales no verificadas",
          "A verificar sinais não confirmados",
          "Vérification des signaux non confirmés",
          "Verifica dei segnali non confermati"),

        R("Admin account not confirmed.",
          "Yönetici hesabı doğrulanmadı.",
          "Admin-Konto nicht bestätigt.",
          "Konto administratora niepotwierdzone.",
          "Cuenta de administrador no confirmada.",
          "Conta de administrador não confirmada.",
          "Compte administrateur non confirmé.",
          "Account amministratore non confermato."),
    };
}
