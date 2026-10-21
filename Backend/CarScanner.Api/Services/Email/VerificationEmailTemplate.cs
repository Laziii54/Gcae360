using System.Globalization;
using System.Net;

namespace CarScanner.Api.Services.Email;

/// <summary>What goes into one verification email. The code is the real one issued by <see cref="EmailVerificationStore"/>.</summary>
public sealed record VerificationEmailModel(
    string Code,
    int ExpiresInMinutes,
    string Language,
    string? LogoUrl,
    string? VerifyUrl,
    string? SupportEmail,
    int Year);

public sealed record RenderedEmail(string Subject, string Html, string PlainText);

/// <summary>
/// The branded OBD360 verification email, built with table layout and inline CSS (Gmail, Outlook, Apple Mail),
/// no scripts, no external stylesheet and no tracking pixel. The one image is the real OBD360 logo; without it
/// (images blocked, or no public URL configured) the header falls back to the product name in text. The code is
/// plain, selectable text. A clipboard emoji sits beside it as a visual cue only: an email cannot copy to the
/// clipboard (no JavaScript), so no "Copy" button is offered.
///
/// Languages: the nine the app offers (ka, en, tr, de, pl, es, pt, fr, it). An unknown language gives English.
/// </summary>
public static class VerificationEmailTemplate
{
    // Brand palette (the app's Colors.xaml and the logo): navy header, white card, blue primary.
    private const string Navy = "#0B1220";
    private const string Primary = "#1D5BD6";
    private const string PageBg = "#F4F5F3";
    private const string Ink = "#1B1D20";
    private const string Muted = "#5D636B";
    private const string Border = "#DCDFE3";
    private const string InfoBg = "#EEF4FF";
    private const string InfoStroke = "#C9DAFB";

    // Kept out of the interpolated HTML literal so the CSS braces need no escaping.
    private const string Css = """
  @media only screen and (max-width:620px) {
    .container { width:100% !important; }
    .px { padding-left:20px !important; padding-right:20px !important; }
    .code { font-size:30px !important; letter-spacing:6px !important; padding-left:6px !important; }
    .h1 { font-size:23px !important; line-height:30px !important; }
  }
  @media (prefers-color-scheme: dark) {
    .bg { background:#070C16 !important; }
    .card { background:#111B2E !important; border-color:#24344F !important; }
    .t { color:#E8EDF6 !important; }
    .s { color:#AAB5C8 !important; }
    .codebox { background:#0B1220 !important; border-color:#2F4B7A !important; }
    .code { color:#FFFFFF !important; }
  }
""";

    public static readonly IReadOnlyList<string> Languages = new[] { "ka", "en", "tr", "de", "pl", "es", "pt", "fr", "it" };

    private sealed record Words(
        string Subject, string Preheader, string Title, string Intro, string CodeLabel, string CopyHint,
        string Expiry, string Button, string ButtonHelp, string Ignore, string Security, string Tagline,
        string Support, string LinkLabel);

    private static readonly Dictionary<string, Words> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ka"] = new Words(
            "დაადასტურე შენი ელფოსტა — OBD360",
            "შენი დამადასტურებელი კოდი შიგნითაა. ის {0} წუთში იწურება.",
            "დაადასტურე შენი ელფოსტა",
            "გმადლობთ, რომ შექმენი OBD360-ის ანგარიში. რეგისტრაციის დასასრულებლად შეიყვანე ქვემოთ მოცემული კოდი აპლიკაციაში.",
            "შენი დამადასტურებელი კოდია",
            "მონიშნე კოდი და შეიყვანე OBD360 აპლიკაციაში.",
            "კოდი {0} წუთში იწურება და მხოლოდ ერთხელ გამოიყენება.",
            "დაადასტურე ელფოსტა",
            "ღილაკი გახსნის ჩვენი ვებგვერდის დაცულ გვერდს, სადაც ელფოსტას დაადასტურებ. ზემოთ მოცემული კოდიც იმუშავებს.",
            "თუ OBD360-ის ანგარიში შენ არ შეგიქმნია, უბრალოდ უგულებელყავი ეს წერილი.",
            "OBD360 არასდროს გთხოვს პაროლს ელფოსტით. კოდი არავის გაუმხილო.",
            "OBD360 · ავტომობილის დიაგნოსტიკა",
            "კითხვები? მოგვწერე: {0}",
            "ან გახსენი ეს ბმული:"),

        ["en"] = new Words(
            "Verify your email address — OBD360",
            "Your verification code is inside. It expires in {0} minutes.",
            "Verify your email address",
            "Thanks for creating a OBD360 account. Enter the code below in the app to finish your registration.",
            "Your verification code",
            "Select and copy the code, then enter it in the OBD360 app.",
            "This code expires in {0} minutes and can be used once.",
            "Verify email address",
            "The button opens a secure page on our website where you confirm your email address. The code above works too.",
            "If you didn't create a OBD360 account, you can safely ignore this email.",
            "OBD360 will never ask for your password by email. Don't share this code with anyone.",
            "OBD360 · Vehicle diagnostics",
            "Questions? Write to {0}",
            "Or open this link:"),

        ["tr"] = new Words(
            "E-posta adresini doğrula — OBD360",
            "Doğrulama kodun içeride. {0} dakika içinde sona erer.",
            "E-posta adresini doğrula",
            "OBD360 hesabı oluşturduğun için teşekkürler. Kaydı tamamlamak için aşağıdaki kodu uygulamaya gir.",
            "Doğrulama kodun",
            "Kodu seç ve kopyala, ardından OBD360 uygulamasına gir.",
            "Bu kodun süresi {0} dakika içinde dolar ve yalnızca bir kez kullanılabilir.",
            "E-postayı doğrula",
            "Düğme, web sitemizde e-posta adresini onaylayacağın güvenli bir sayfa açar. Yukarıdaki kod da çalışır.",
            "OBD360 hesabını sen oluşturmadıysan bu e-postayı güvenle yok sayabilirsin.",
            "OBD360 senden asla e-postayla parola istemez. Bu kodu kimseyle paylaşma.",
            "OBD360 · Araç tanılama",
            "Sorun mu var? Yaz: {0}",
            "Ya da bu bağlantıyı aç:"),

        ["de"] = new Words(
            "Bestätige deine E-Mail-Adresse — OBD360",
            "Dein Bestätigungscode steht in dieser E-Mail. Er läuft in {0} Minuten ab.",
            "Bestätige deine E-Mail-Adresse",
            "Danke, dass du ein OBD360-Konto erstellt hast. Gib den folgenden Code in der App ein, um die Registrierung abzuschließen.",
            "Dein Bestätigungscode",
            "Code markieren, kopieren und in der OBD360-App eingeben.",
            "Dieser Code läuft in {0} Minuten ab und kann nur einmal verwendet werden.",
            "E-Mail-Adresse bestätigen",
            "Die Schaltfläche öffnet eine sichere Seite auf unserer Website, auf der du deine E-Mail-Adresse bestätigst. Der Code oben funktioniert ebenfalls.",
            "Wenn du kein OBD360-Konto erstellt hast, kannst du diese E-Mail einfach ignorieren.",
            "OBD360 fragt dich nie per E-Mail nach deinem Passwort. Gib diesen Code an niemanden weiter.",
            "OBD360 · Fahrzeugdiagnose",
            "Fragen? Schreib an {0}",
            "Oder öffne diesen Link:"),

        ["pl"] = new Words(
            "Potwierdź swój adres e-mail — OBD360",
            "Twój kod weryfikacyjny jest w środku. Wygasa za {0} min.",
            "Potwierdź swój adres e-mail",
            "Dziękujemy za założenie konta OBD360. Wpisz poniższy kod w aplikacji, aby dokończyć rejestrację.",
            "Twój kod weryfikacyjny",
            "Zaznacz i skopiuj kod, a następnie wpisz go w aplikacji OBD360.",
            "Ten kod wygasa za {0} min i można go użyć tylko raz.",
            "Potwierdź adres e-mail",
            "Przycisk otwiera bezpieczną stronę na naszej witrynie, na której potwierdzisz adres e-mail. Kod powyżej też działa.",
            "Jeśli nie zakładasz konta OBD360, po prostu zignoruj tę wiadomość.",
            "OBD360 nigdy nie prosi o hasło przez e-mail. Nie udostępniaj nikomu tego kodu.",
            "OBD360 · Diagnostyka pojazdów",
            "Pytania? Napisz na: {0}",
            "Lub otwórz ten link:"),

        ["es"] = new Words(
            "Verifica tu correo electrónico — OBD360",
            "Tu código de verificación está dentro. Caduca en {0} minutos.",
            "Verifica tu correo electrónico",
            "Gracias por crear una cuenta de OBD360. Introduce el código de abajo en la app para terminar el registro.",
            "Tu código de verificación",
            "Selecciona y copia el código, y luego introdúcelo en la app OBD360.",
            "Este código caduca en {0} minutos y solo se puede usar una vez.",
            "Verificar correo electrónico",
            "El botón abre una página segura de nuestro sitio web donde confirmas tu correo. El código de arriba también funciona.",
            "Si no has creado una cuenta de OBD360, puedes ignorar este mensaje.",
            "OBD360 nunca te pedirá tu contraseña por correo. No compartas este código con nadie.",
            "OBD360 · Diagnóstico de vehículos",
            "¿Preguntas? Escribe a {0}",
            "O abre este enlace:"),

        ["pt"] = new Words(
            "Confirme o seu e-mail — OBD360",
            "O seu código de verificação está no interior. Expira em {0} minutos.",
            "Confirme o seu e-mail",
            "Obrigado por criar uma conta OBD360. Introduza o código abaixo na app para concluir o registo.",
            "O seu código de verificação",
            "Selecione e copie o código e introduza-o na app OBD360.",
            "Este código expira em {0} minutos e só pode ser usado uma vez.",
            "Confirmar e-mail",
            "O botão abre uma página segura do nosso site onde confirma o seu e-mail. O código acima também funciona.",
            "Se não criou uma conta OBD360, pode ignorar este e-mail.",
            "O OBD360 nunca lhe pedirá a palavra-passe por e-mail. Não partilhe este código com ninguém.",
            "OBD360 · Diagnóstico de veículos",
            "Dúvidas? Escreva para {0}",
            "Ou abra esta ligação:"),

        ["fr"] = new Words(
            "Vérifiez votre adresse e-mail — OBD360",
            "Votre code de vérification est à l'intérieur. Il expire dans {0} minutes.",
            "Vérifiez votre adresse e-mail",
            "Merci d'avoir créé un compte OBD360. Saisissez le code ci-dessous dans l'application pour terminer l'inscription.",
            "Votre code de vérification",
            "Sélectionnez et copiez le code, puis saisissez-le dans l'application OBD360.",
            "Ce code expire dans {0} minutes et ne peut être utilisé qu'une seule fois.",
            "Vérifier l'adresse e-mail",
            "Le bouton ouvre une page sécurisée de notre site où vous confirmez votre adresse e-mail. Le code ci-dessus fonctionne aussi.",
            "Si vous n'avez pas créé de compte OBD360, vous pouvez ignorer cet e-mail.",
            "OBD360 ne vous demandera jamais votre mot de passe par e-mail. Ne partagez ce code avec personne.",
            "OBD360 · Diagnostic automobile",
            "Des questions ? Écrivez à {0}",
            "Ou ouvrez ce lien :"),

        ["it"] = new Words(
            "Verifica il tuo indirizzo e-mail — OBD360",
            "Il tuo codice di verifica è all'interno. Scade tra {0} minuti.",
            "Verifica il tuo indirizzo e-mail",
            "Grazie per aver creato un account OBD360. Inserisci il codice qui sotto nell'app per completare la registrazione.",
            "Il tuo codice di verifica",
            "Seleziona e copia il codice, poi inseriscilo nell'app OBD360.",
            "Questo codice scade tra {0} minuti e può essere usato una sola volta.",
            "Verifica l'e-mail",
            "Il pulsante apre una pagina sicura del nostro sito dove confermi il tuo indirizzo e-mail. Funziona anche il codice qui sopra.",
            "Se non hai creato un account OBD360, puoi ignorare questa e-mail.",
            "OBD360 non ti chiederà mai la password via e-mail. Non condividere questo codice con nessuno.",
            "OBD360 · Diagnostica dei veicoli",
            "Domande? Scrivi a {0}",
            "Oppure apri questo link:")
    };

    public enum LinkPageKind
    {
        Confirm,
        Done,
        Failed
    }

    private sealed record PageWords(string ConfirmBody, string DoneTitle, string DoneBody, string FailTitle, string FailBody);

    private static readonly Dictionary<string, PageWords> Pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ka"] = new("დაადასტურე, რომ გსურს ამ ელფოსტის დადასტურება OBD360-ისთვის.", "ელფოსტა დადასტურებულია", "გმადლობთ! შეგიძლია დაბრუნდე OBD360 აპლიკაციაში.", "ეს ბმული გამოუსადეგარია", "შესაძლოა ვადა გაუვიდა ან უკვე გამოყენებულია. მოითხოვე ახალი კოდი OBD360 აპლიკაციაში."),
        ["en"] = new("Confirm that you want to verify this email address for OBD360.", "Email verified", "Thank you! You can return to the OBD360 app.", "This link can't be used", "It may have expired or already been used. Request a new code in the OBD360 app."),
        ["tr"] = new("Bu e-posta adresini OBD360 için doğrulamak istediğini onayla.", "E-posta doğrulandı", "Teşekkürler! OBD360 uygulamasına dönebilirsin.", "Bu bağlantı kullanılamıyor", "Süresi dolmuş veya zaten kullanılmış olabilir. OBD360 uygulamasından yeni bir kod iste."),
        ["de"] = new("Bestätige, dass du diese E-Mail-Adresse für OBD360 verifizieren möchtest.", "E-Mail bestätigt", "Danke! Du kannst zur OBD360-App zurückkehren.", "Dieser Link kann nicht verwendet werden", "Er ist möglicherweise abgelaufen oder wurde bereits benutzt. Fordere in der OBD360-App einen neuen Code an."),
        ["pl"] = new("Potwierdź, że chcesz zweryfikować ten adres e-mail dla OBD360.", "E-mail potwierdzony", "Dziękujemy! Możesz wrócić do aplikacji OBD360.", "Tego linku nie można użyć", "Mógł wygasnąć lub został już użyty. Poproś o nowy kod w aplikacji OBD360."),
        ["es"] = new("Confirma que quieres verificar este correo para OBD360.", "Correo verificado", "¡Gracias! Ya puedes volver a la app OBD360.", "No se puede usar este enlace", "Puede haber caducado o haberse usado ya. Solicita un código nuevo en la app OBD360."),
        ["pt"] = new("Confirme que quer verificar este e-mail para o OBD360.", "E-mail confirmado", "Obrigado! Pode voltar à app OBD360.", "Não é possível usar esta ligação", "Pode ter expirado ou já ter sido usada. Peça um novo código na app OBD360."),
        ["fr"] = new("Confirmez que vous souhaitez vérifier cette adresse e-mail pour OBD360.", "E-mail vérifié", "Merci ! Vous pouvez revenir à l'application OBD360.", "Ce lien est inutilisable", "Il a peut-être expiré ou déjà été utilisé. Demandez un nouveau code dans l'application OBD360."),
        ["it"] = new("Conferma di voler verificare questo indirizzo e-mail per OBD360.", "E-mail verificata", "Grazie! Puoi tornare all'app OBD360.", "Questo link non può essere usato", "Potrebbe essere scaduto o già usato. Richiedi un nuovo codice nell'app OBD360.")
    };

    /// <summary>
    /// The small page behind the email button. Opening the link only shows a confirmation button (a POST then
    /// verifies), so mail scanners that pre-fetch links cannot use the one-time token up.
    /// </summary>
    public static string RenderLinkPage(string? language, LinkPageKind kind, string? token)
    {
        var lang = ResolveLanguage(language);
        var w = Table[lang];
        var p = Pages[lang];
        var (title, body) = kind switch
        {
            LinkPageKind.Done => (p.DoneTitle, p.DoneBody),
            LinkPageKind.Failed => (p.FailTitle, p.FailBody),
            _ => (w.Title, p.ConfirmBody)
        };

        var action = kind == LinkPageKind.Confirm
            ? $"""<form method="post" action="/verify-email"><input type="hidden" name="t" value="{E(token)}"><input type="hidden" name="l" value="{lang}"><button type="submit" style="margin-top:22px;padding:15px 30px;border:0;border-radius:12px;background:{Primary};color:#FFFFFF;font-size:16px;font-weight:700;cursor:pointer;">{E(w.Button)}</button></form>"""
            : string.Empty;
        var markColor = kind == LinkPageKind.Failed ? "#B3361F" : kind == LinkPageKind.Done ? "#1E8A4C" : Primary;
        var mark = MarkSvg(kind);

        return $"""
<!DOCTYPE html>
<html lang="{lang}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="robots" content="noindex"><meta name="referrer" content="no-referrer">
<title>{E(title)} — OBD360</title>
<link rel="icon" type="image/png" href="/assets/favicon.png?v=2">
</head>
<body style="margin:0;background:{PageBg};font-family:Arial,Helvetica,sans-serif;color:{Ink};">
<div style="max-width:520px;margin:0 auto;padding:24px 14px;">
<div style="background:{Navy};border-radius:20px 20px 0 0;padding:26px;text-align:center;border-bottom:4px solid {Primary};"><img src="/assets/logo.png?v=2" width="110" alt="OBD360" style="max-width:55%;height:auto;"></div>
<div style="background:#FFFFFF;border:1px solid {Border};border-top:0;border-radius:0 0 20px 20px;padding:34px 28px;text-align:center;">
<div style="width:56px;height:56px;margin:0 auto 16px;display:flex;align-items:center;justify-content:center;border-radius:28px;background:{InfoBg};color:{markColor};">{mark}</div>
<h1 style="margin:0 0 10px;font-size:24px;line-height:31px;">{E(title)}</h1>
<p style="margin:0;font-size:16px;line-height:24px;color:{Muted};">{E(body)}</p>
{action}
</div>
<p style="text-align:center;font-size:12px;color:{Muted};margin:18px 0 0;">{E(w.Tagline)}</p>
</div></body></html>
""";
    }

    /// <summary>The language code to use: a supported two-letter code, otherwise English.</summary>
    public static string ResolveLanguage(string? requested)
    {
        var code = (requested ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length >= 2)
            code = code[..2];
        return Table.ContainsKey(code) ? code : "en";
    }

    public static RenderedEmail Render(VerificationEmailModel model)
    {
        var lang = ResolveLanguage(model.Language);
        var w = Table[lang];
        var minutes = model.ExpiresInMinutes.ToString(CultureInfo.InvariantCulture);
        var expiry = string.Format(CultureInfo.InvariantCulture, w.Expiry, minutes);
        var preheader = string.Format(CultureInfo.InvariantCulture, w.Preheader, minutes);
        var support = string.IsNullOrWhiteSpace(model.SupportEmail) ? null : model.SupportEmail.Trim();

        return new RenderedEmail(w.Subject, BuildHtml(model, lang, w, expiry, preheader, support), BuildText(model, w, expiry, support));
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Monochrome outline icon of the link page (check = done, close = failed, mail = waiting for the tap). Inline SVG, so no emoji and no image request.</summary>
    private static string MarkSvg(LinkPageKind kind)
    {
        var path = kind switch
        {
            LinkPageKind.Done => "M5,12.5 L10,17 L19,7",
            LinkPageKind.Failed => "M6,6 L18,18 M18,6 L6,18",
            _ => "M4,6 L20,6 L20,18 L4,18 Z M4,7 L12,13 L20,7"
        };
        return "<svg viewBox=\"0 0 24 24\" width=\"30\" height=\"30\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\"><path d=\"" + path + "\"/></svg>";
    }

    private static string BuildHtml(VerificationEmailModel m, string lang, Words w, string expiry, string preheader, string? supportEmail)
    {
        var logo = string.IsNullOrWhiteSpace(m.LogoUrl)
            ? $"<span style=\"font-family:Arial,Helvetica,sans-serif;font-size:30px;font-weight:700;letter-spacing:1px;color:#FFFFFF;\">OBD<span style=\"color:#4C8DFF;\">360</span></span>"
            : $"<img src=\"{E(m.LogoUrl)}\" width=\"128\" alt=\"OBD360\" style=\"display:block;width:128px;max-width:60%;height:auto;border:0;outline:none;text-decoration:none;margin:0 auto;\">";

        var button = string.IsNullOrWhiteSpace(m.VerifyUrl) ? string.Empty : $"""
<tr><td align="center" class="px" style="padding:26px 32px 0 32px;">
<table role="presentation" cellpadding="0" cellspacing="0" border="0" class="btn" style="margin:0 auto;"><tr>
<td align="center" bgcolor="{Primary}" style="border-radius:12px;background:{Primary};">
<a href="{E(m.VerifyUrl)}" target="_blank" style="display:inline-block;padding:15px 30px;font-family:Arial,Helvetica,sans-serif;font-size:16px;font-weight:700;line-height:20px;color:#FFFFFF;text-decoration:none;border-radius:12px;">{E(w.Button)}</a>
</td></tr></table>
</td></tr>
<tr><td align="center" class="px s" style="padding:12px 32px 0 32px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:19px;color:{Muted};">{E(w.ButtonHelp)}</td></tr>
""";

        var supportHtml = string.IsNullOrWhiteSpace(supportEmail) ? string.Empty
            : $"<br>{E(string.Format(CultureInfo.InvariantCulture, w.Support, supportEmail.Trim()))}";

        return $"""
<!DOCTYPE html>
<html lang="{lang}" xmlns="http://www.w3.org/1999/xhtml">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="X-UA-Compatible" content="IE=edge">
<meta name="color-scheme" content="light dark">
<meta name="supported-color-schemes" content="light dark">
<title>{E(w.Title)}</title>
<style>
{Css}
</style>
</head>
<body class="bg" style="margin:0;padding:0;background:{PageBg};-webkit-text-size-adjust:100%;">
<div style="display:none;max-height:0;overflow:hidden;opacity:0;mso-hide:all;font-size:1px;line-height:1px;color:{PageBg};">{E(preheader)}&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="bg" style="background:{PageBg};">
<tr><td align="center" style="padding:24px 12px;">
<table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" class="container" style="width:600px;max-width:100%;">

<tr><td align="center" bgcolor="{Navy}" style="background:{Navy};border-radius:20px 20px 0 0;padding:30px 24px 26px 24px;">{logo}</td></tr>
<tr><td height="4" bgcolor="{Primary}" style="height:4px;line-height:4px;font-size:0;background:{Primary};">&nbsp;</td></tr>

<tr><td class="card" bgcolor="#FFFFFF" style="background:#FFFFFF;border:1px solid {Border};border-top:0;border-radius:0 0 20px 20px;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">

<tr><td align="center" class="px" style="padding:34px 32px 0 32px;">
<h1 class="h1 t" style="margin:0;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:33px;font-weight:700;color:{Ink};">{E(w.Title)}</h1>
</td></tr>
<tr><td align="center" class="px s" style="padding:12px 32px 0 32px;font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:24px;color:{Muted};">{E(w.Intro)}</td></tr>

<tr><td align="center" class="px t" style="padding:28px 32px 10px 32px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;font-weight:700;letter-spacing:.4px;color:{Ink};">{E(w.CodeLabel)}</td></tr>

<tr><td align="center" class="px" style="padding:0 32px;">
<table role="presentation" cellpadding="0" cellspacing="0" border="0" class="codebox" style="margin:0 auto;background:{InfoBg};border:1.5px solid {InfoStroke};border-radius:16px;"><tr>
<td align="center" style="padding:18px 26px 18px 26px;">
<span class="code" style="font-family:'SF Mono',Menlo,Consolas,'Courier New',monospace;font-size:38px;line-height:44px;font-weight:700;letter-spacing:10px;padding-left:10px;color:{Navy};-webkit-user-select:all;user-select:all;">{E(m.Code)}</span>
</td>
</tr></table>
</td></tr>
<tr><td align="center" class="px s" style="padding:12px 32px 0 32px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:{Muted};">{E(w.CopyHint)}</td></tr>
<tr><td align="center" class="px t" style="padding:6px 32px 0 32px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:19px;font-weight:700;color:{Ink};">{E(expiry)}</td></tr>
{button}
<tr><td class="px" style="padding:30px 32px 0 32px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr><td height="1" bgcolor="{Border}" style="height:1px;line-height:1px;font-size:0;background:{Border};">&nbsp;</td></tr></table></td></tr>
<tr><td align="center" class="px s" style="padding:18px 32px 32px 32px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:{Muted};">{E(w.Ignore)}<br><br>{E(w.Security)}</td></tr>

</table>
</td></tr>

<tr><td align="center" class="px s" style="padding:22px 24px 8px 24px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:{Muted};">
<strong style="color:{Ink};" class="t">{E(w.Tagline)}</strong><br>&copy; {m.Year.ToString(CultureInfo.InvariantCulture)} OBD360{supportHtml}
</td></tr>

</table>
</td></tr>
</table>
</body>
</html>
""";
    }

    private static string BuildText(VerificationEmailModel m, Words w, string expiry, string? supportEmail)
    {
        var lines = new List<string>
        {
            w.Title,
            string.Empty,
            w.Intro,
            string.Empty,
            w.CodeLabel + ": " + m.Code,
            w.CopyHint,
            expiry
        };

        if (!string.IsNullOrWhiteSpace(m.VerifyUrl))
        {
            lines.Add(string.Empty);
            lines.Add(w.LinkLabel);
            lines.Add(m.VerifyUrl!);
        }

        lines.Add(string.Empty);
        lines.Add(w.Ignore);
        lines.Add(w.Security);
        lines.Add(string.Empty);
        lines.Add("--");
        lines.Add(w.Tagline);
        if (!string.IsNullOrWhiteSpace(supportEmail))
            lines.Add(string.Format(CultureInfo.InvariantCulture, w.Support, supportEmail.Trim()));

        return string.Join("\r\n", lines);
    }
}
