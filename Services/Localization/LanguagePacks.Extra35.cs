namespace CarScanner.Services.Localization;

// Batch 35: English texts that were reachable from a Georgian XAML literal (via the KaToEn-family dictionaries)
// but had no row in any of the seven added-language packs. Found by cross-checking every Georgian Text/Title/
// Placeholder/Header literal across Views/*.xaml against its dictionary-mapped English value, then checking that
// value against the packs - a check no existing test performed (the existing XAML tests only check that a
// Georgian literal has *some* English mapping, and that literals already written in English have pack coverage;
// neither checks the English value a Georgian literal's dictionary entry actually resolves to). Also covers two
// new entries added for the ModuleScanViewModel.Scan() localization fix (LanguagePacks.cs/LocalizationService.
// Extra.cs changes made in the same pass).
// Machine-written; native review needed. Order: tr, de, pl, es, pt, fr, it.
internal static partial class LanguagePacks
{
    private static IEnumerable<(string En, string[] T)> ExtraRows35() => new (string En, string[] T)[]
    {
        R("• After writing, the value is read back; on a mismatch the old value is restored automatically.",
          "• Yazıldıktan sonra değer geri okunur; uyuşmazlık durumunda eski değer otomatik olarak geri yüklenir.",
          "• Nach dem Schreiben wird der Wert zurückgelesen; bei einer Abweichung wird der alte Wert automatisch wiederhergestellt.",
          "• Po zapisie wartość jest odczytywana ponownie; w przypadku niezgodności stara wartość jest automatycznie przywracana.",
          "• Después de escribir, el valor se vuelve a leer; si no coincide, el valor anterior se restaura automáticamente.",
          "• Depois de escrever, o valor é lido novamente; em caso de incompatibilidade, o valor anterior é restaurado automaticamente.",
          "• Après l'écriture, la valeur est relue ; en cas d'incohérence, l'ancienne valeur est restaurée automatiquement.",
          "• Dopo la scrittura, il valore viene riletto; in caso di discrepanza, il valore precedente viene ripristinato automaticamente."),

        R("• Only settings verified on this exact model, year and module can be changed.",
          "• Yalnızca bu tam model, yıl ve modül için doğrulanmış ayarlar değiştirilebilir.",
          "• Es können nur Einstellungen geändert werden, die für genau dieses Modell, Jahr und Modul verifiziert wurden.",
          "• Można zmieniać tylko ustawienia zweryfikowane dla tego dokładnego modelu, roku i modułu.",
          "• Solo se pueden cambiar los ajustes verificados para este modelo, año y módulo exactos.",
          "• Só podem ser alterados os ajustes verificados para este modelo, ano e módulo exatos.",
          "• Seuls les réglages vérifiés pour ce modèle, cette année et ce module exacts peuvent être modifiés.",
          "• Possono essere modificate solo le impostazioni verificate per questo esatto modello, anno e modulo."),

        R("• The car must be stopped, engine off, battery above 12.2 V.",
          "• Araç durmuş, motor kapalı ve akü 12.2 V'un üzerinde olmalıdır.",
          "• Das Auto muss stehen, der Motor muss aus sein und die Batterie muss über 12,2 V liegen.",
          "• Samochód musi być zatrzymany, silnik wyłączony, a akumulator powyżej 12,2 V.",
          "• El coche debe estar detenido, el motor apagado y la batería por encima de 12,2 V.",
          "• O carro deve estar parado, o motor desligado e a bateria acima de 12,2 V.",
          "• La voiture doit être arrêtée, le moteur éteint et la batterie au-dessus de 12,2 V.",
          "• L'auto deve essere ferma, il motore spento e la batteria sopra i 12,2 V."),

        R("• A full backup is always made first.",
          "• Önce her zaman tam bir yedekleme yapılır.",
          "• Zuerst wird immer ein vollständiges Backup erstellt.",
          "• Najpierw zawsze wykonywana jest pełna kopia zapasowa.",
          "• Primero siempre se hace una copia de seguridad completa.",
          "• Primeiro é sempre feita uma cópia de segurança completa.",
          "• Une sauvegarde complète est toujours effectuée d'abord.",
          "• Viene sempre eseguito prima un backup completo."),

        R("• Any change can be undone from the history with one tap.",
          "• Herhangi bir değişiklik geçmişten bir dokunuşla geri alınabilir.",
          "• Jede Änderung kann im Verlauf mit einem Tipp rückgängig gemacht werden.",
          "• Każdą zmianę można wycofać z historii jednym dotknięciem.",
          "• Cualquier cambio se puede deshacer desde el historial con un toque.",
          "• Qualquer alteração pode ser desfeita a partir do histórico com um toque.",
          "• Toute modification peut être annulée depuis l'historique en un geste.",
          "• Qualsiasi modifica può essere annullata dalla cronologia con un tocco."),

        R("Only reads — nothing is changed on the car. To compare, tap one backup in the list below, then the other.",
          "Yalnızca okur — araçta hiçbir şey değiştirilmez. Karşılaştırmak için aşağıdaki listeden önce birini, sonra diğerini dokunun.",
          "Liest nur — am Auto wird nichts geändert. Zum Vergleichen zuerst auf ein Backup in der Liste unten tippen, dann auf das andere.",
          "Tylko odczytuje — w samochodzie nic się nie zmienia. Aby porównać, dotknij jednej kopii zapasowej na liście poniżej, a potem drugiej.",
          "Solo lee — no se cambia nada en el coche. Para comparar, toca primero una copia de seguridad en la lista de abajo y luego la otra.",
          "Apenas lê — nada é alterado no carro. Para comparar, toque numa cópia de segurança na lista abaixo e depois na outra.",
          "Lit uniquement — rien n'est modifié sur la voiture. Pour comparer, touchez une sauvegarde dans la liste ci-dessous, puis l'autre.",
          "Legge solo — non viene modificato nulla sull'auto. Per confrontare, tocca un backup nell'elenco sotto, poi l'altro."),

        R("minimum", "minimum", "Minimum", "minimum", "mínimo", "mínimo", "minimum", "minimo"),
        R("maximum", "maksimum", "Maximum", "maksimum", "máximo", "máximo", "maximum", "massimo"),
        R("10s", "10sn", "10s", "10s", "10s", "10s", "10s", "10s"),
        R("30s", "30sn", "30s", "30s", "30s", "30s", "30s", "30s"),
        R("1m", "1dk", "1min", "1min", "1min", "1min", "1min", "1min"),
        R("5m", "5dk", "5min", "5min", "5min", "5min", "5min", "5min"),
        R("Report", "Rapor", "Bericht", "Raport", "Informe", "Relatório", "Rapport", "Rapporto"),

        R("answered, code list incomplete",
          "yanıt verdi, kod listesi eksik",
          "antwortet, Codeliste unvollständig",
          "odpowiada, lista kodów niekompletna",
          "responde, lista de códigos incompleta",
          "responde, lista de códigos incompleta",
          "répond, liste de codes incomplète",
          "risponde, elenco codici incompleto"),

        R("answered, code list incomplete · {0} fault(s) received",
          "yanıt verdi, kod listesi eksik · {0} arıza alındı",
          "antwortet, Codeliste unvollständig · {0} Fehler empfangen",
          "odpowiada, lista kodów niekompletna · odebrano {0} usterek",
          "responde, lista de códigos incompleta · {0} fallo(s) recibido(s)",
          "responde, lista de códigos incompleta · {0} falha(s) recebida(s)",
          "répond, liste de codes incomplète · {0} défaut(s) reçu(s)",
          "risponde, elenco codici incompleto · {0} guasto/i ricevuto/i"),
    };
}
