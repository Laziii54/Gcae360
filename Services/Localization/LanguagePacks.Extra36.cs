namespace CarScanner.Services.Localization;

// Batch 36: "Enter manually" on the Service and Mileage pages (type what the dashboard shows, the app marks readings close to it).
// Machine-written; native review needed. Order: tr, de, pl, es, pt, fr, it.
internal static partial class LanguagePacks
{
    private static IEnumerable<(string En, string[] T)> ExtraRows36() => new (string En, string[] T)[]
    {
        R("Enter manually", "Elle gir", "Manuell eingeben", "Wpisz ręcznie", "Introducir manualmente", "Inserir manualmente", "Saisir manuellement", "Inserisci manualmente"),
        R("Search", "Ara", "Suchen", "Szukaj", "Buscar", "Pesquisar", "Rechercher", "Cerca"),

        R("The exact number is not needed: readings within about 0.5 % count as close.",
          "Tam sayı gerekmez: yaklaşık %0,5 içindeki değerler yakın sayılır.",
          "Die genaue Zahl ist nicht nötig: Werte innerhalb von etwa 0,5 % gelten als nah.",
          "Dokładna liczba nie jest potrzebna: odczyty w granicach około 0,5% uznajemy za bliskie.",
          "No hace falta el número exacto: las lecturas dentro de aproximadamente un 0,5 % se consideran cercanas.",
          "Não é preciso o número exato: leituras dentro de cerca de 0,5 % contam como próximas.",
          "Le chiffre exact n'est pas nécessaire : les valeurs à environ 0,5 % près sont considérées comme proches.",
          "Il numero esatto non serve: le letture entro circa lo 0,5 % sono considerate vicine."),

        R("Distance on the dashboard ({0})",
          "Göstergedeki mesafe ({0})",
          "Angezeigte Strecke ({0})",
          "Dystans na desce rozdzielczej ({0})",
          "Distancia en el tablero ({0})",
          "Distância no painel ({0})",
          "Distance au tableau de bord ({0})",
          "Distanza sul cruscotto ({0})"),

        R("Enter the distance the dashboard shows, in digits.",
          "Göstergede görünen mesafeyi rakamlarla gir.",
          "Gib die im Kombiinstrument angezeigte Strecke in Ziffern ein.",
          "Wpisz cyframi dystans pokazywany na desce rozdzielczej.",
          "Escribe con cifras la distancia que muestra el tablero.",
          "Escreva com algarismos a distância mostrada no painel.",
          "Saisis en chiffres la distance affichée au tableau de bord.",
          "Scrivi in cifre la distanza mostrata dal cruscotto."),

        R("The car returned no total-odometer reading, so there is nothing to compare with.",
          "Araç toplam kilometre bilgisi döndürmedi, bu yüzden karşılaştırılacak bir şey yok.",
          "Das Fahrzeug hat keinen Gesamtkilometerstand geliefert, daher gibt es nichts zu vergleichen.",
          "Samochód nie zwrócił całkowitego przebiegu, więc nie ma z czym porównać.",
          "El coche no devolvió ninguna lectura del cuentakilómetros total, así que no hay nada con qué comparar.",
          "O carro não devolveu nenhuma leitura do odômetro total, então não há nada para comparar.",
          "La voiture n'a renvoyé aucun kilométrage total, il n'y a donc rien à comparer.",
          "L'auto non ha restituito alcuna lettura del contachilometri totale, quindi non c'è nulla da confrontare."),

        R("{0} reading(s) match the number you entered.",
          "{0} değer girdiğin sayıyla eşleşiyor.",
          "{0} Wert(e) stimmen mit der eingegebenen Zahl überein.",
          "Odczytów zgodnych z wpisaną liczbą: {0}.",
          "{0} lectura(s) coinciden con el número introducido.",
          "{0} leitura(s) coincidem com o número informado.",
          "{0} lecture(s) correspondent au nombre saisi.",
          "{0} lettura/e corrispondono al numero inserito."),

        R("{0} reading(s) are close to the number you entered (within {1}).",
          "{0} değer girdiğin sayıya yakın ({1} içinde).",
          "{0} Wert(e) liegen nahe an der eingegebenen Zahl (innerhalb von {1}).",
          "Odczytów bliskich wpisanej liczbie: {0} (w granicach {1}).",
          "{0} lectura(s) están cerca del número introducido (dentro de {1}).",
          "{0} leitura(s) estão próximas do número informado (dentro de {1}).",
          "{0} lecture(s) sont proches du nombre saisi (à {1} près).",
          "{0} lettura/e sono vicine al numero inserito (entro {1})."),

        R("No reading is close (within {0}). The nearest ones are listed below.",
          "Hiçbir değer yakın değil ({0} içinde). En yakın olanlar aşağıda.",
          "Kein Wert ist nah dran (innerhalb von {0}). Die nächstgelegenen stehen unten.",
          "Żaden odczyt nie jest bliski (w granicach {0}). Najbliższe są poniżej.",
          "Ninguna lectura está cerca (dentro de {0}). Las más cercanas aparecen abajo.",
          "Nenhuma leitura está próxima (dentro de {0}). As mais próximas estão abaixo.",
          "Aucune lecture n'est proche (à {0} près). Les plus proches sont listées ci-dessous.",
          "Nessuna lettura è vicina (entro {0}). Le più vicine sono elencate sotto."),

        R("The number may be in kilometres.",
          "Sayı kilometre cinsinden olabilir.", "Die Zahl könnte in Kilometern sein.", "Liczba może być w kilometrach.",
          "El número puede estar en kilómetros.", "O número pode estar em quilômetros.", "Le nombre est peut-être en kilomètres.", "Il numero potrebbe essere in chilometri."),

        R("The number may be in miles.",
          "Sayı mil cinsinden olabilir.", "Die Zahl könnte in Meilen sein.", "Liczba może być w milach.",
          "El número puede estar en millas.", "O número pode estar em milhas.", "Le nombre est peut-être en miles.", "Il numero potrebbe essere in miglia."),

        R("Same number", "Aynı sayı", "Gleiche Zahl", "Ta sama liczba", "Mismo número", "Mesmo número", "Même nombre", "Stesso numero"),
        R("{0} apart", "{0} fark", "{0} Abstand", "różnica {0}", "{0} de diferencia", "{0} de diferença", "{0} d'écart", "{0} di differenza"),

        R("The exact number is not needed: close values count (oil ±2 %), so 84 also finds 82 to 86. A number above 100 is read as a distance.",
          "Tam sayı gerekmez: yakın değerler de sayılır (yağ ±%2), yani 84 aynı zamanda 82–86'yı da bulur. 100'ün üzerindeki sayı mesafe olarak okunur.",
          "Die genaue Zahl ist nicht nötig: nahe Werte zählen (Öl ±2 %), 84 findet also auch 82 bis 86. Eine Zahl über 100 wird als Strecke gelesen.",
          "Dokładna liczba nie jest potrzebna: bliskie wartości też się liczą (olej ±2%), więc 84 znajdzie także 82–86. Liczba powyżej 100 jest traktowana jako dystans.",
          "No hace falta el número exacto: los valores cercanos cuentan (aceite ±2 %), así que 84 también encuentra de 82 a 86. Un número superior a 100 se lee como distancia.",
          "Não é preciso o número exato: valores próximos contam (óleo ±2 %), então 84 também encontra de 82 a 86. Um número acima de 100 é lido como distância.",
          "Le chiffre exact n'est pas nécessaire : les valeurs proches comptent (huile ±2 %), donc 84 trouve aussi 82 à 86. Un nombre supérieur à 100 est lu comme une distance.",
          "Il numero esatto non serve: contano anche i valori vicini (olio ±2 %), quindi 84 trova anche da 82 a 86. Un numero superiore a 100 è letto come distanza."),
    };
}
