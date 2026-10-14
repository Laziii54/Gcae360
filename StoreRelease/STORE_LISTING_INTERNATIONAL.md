# OBD360 — მაღაზიის აღწერები სხვა ქვეყნებისთვის

აპის ID: `com.gcar360.app`. ქართული და ძირითადი ინგლისური ტექსტი არის `STORE_LISTING.md`-ში.
ლიმიტები შემოწმებულია: სახელი ≤30, Apple subtitle ≤30, Google მოკლე აღწერა ≤80, Apple keywords ≤100, სრული ≤4000.

## რიგითობა (პრიორიტეტი)

| # | ქვეყანა | ენა (Play / App Store) | რატომ |
|---|---|---|---|
| 1 | საფრანგეთი | fr-FR | EOBD ყველა ბენზინზე 2001-დან, დიზელზე 2004-დან; ბევრი ძველი მანქანა |
| 2 | კანადა | en-CA + fr-CA | OBD2 და VIN ისტორია (აუქციონები) მუშაობს; კვებეკისთვის ფრანგული |
| 3 | იაპონია | ja | ჰიბრიდები/EV ბევრია; ადგილობრივ მანქანებს ხშირად 17-ნიშნა VIN არ აქვს → VIN-ზე ნაკლები აქცენტი |
| 4 | ჩინეთი | zh-Hans | Google Play იქ არ მუშაობს; App Store-ს ICP ნომერი სჭირდება (იხ. ქვემოთ) |
| 5 | ინდოეთი | en-IN + hi | დიდი ბაზარი, ფასზე მგრძნობიარე; თითქმის ყველა Android |
| 6 | აშშ | en-US | OBD2 1996-დან; VIN აუქციონის ისტორია ყველაზე ძლიერია |
| 7 | დიდი ბრიტანეთი | en-GB | EOBD; ბევრი მეორადი მანქანა |
| 8 | გერმანია | de-DE | ევროპის ყველაზე დიდი ავტობაზარი |
| + | ესპანეთი, რუსულენოვანი, თურქეთი | es-ES, ru, tr | დამატებით, როცა პირველები ჩაეშვება |

## რა უნდა იცოდე ქვეყნების მიხედვით

- **აპის ენა.** აპი ჯერ მხოლოდ ინგლისური/ქართულია. ამიტომ ყველა არაინგლისურ აღწერაში ბოლოს წერია „აპის ენა: ინგლისური“ — ცუდი შეფასებები რომ არ მივიღოთ. მოგვიანებით აპის ინტერფეისსაც ვთარგმნით (ფრანგული, გერმანული, ესპანური, იაპონური).
- **iPhone (ყველა ქვეყანა).** Apple თავად ყიდის, ცვლის ვალუტას და იხდის გადასახადს. App Store Connect → Pricing-ში აირჩიე ძირითადი ფასი დოლარში: PRO $4.99, VIN PRO $5.99, Full PRO $8.99, +5 VIN $3.99, +10 VIN $6.99, 1 VIN $2.99. Apple სხვა ქვეყნებში ავტომატურად გადაიყვანს.
  - ინდოეთისთვის ხელით დააყენე უფრო დაბალი ფასი (მაგ. ₹199 / ₹249 / ₹399) — იქ $9 ძვირია.
  - საქართველოსთვის ხელით: 4.99 ₾ / 5.99 ₾ / 8.99 ₾ (თუ Apple ლარს გთავაზობს), სხვა შემთხვევაში უახლოესი დოლარის ფასი.
- **Android (ყველა ქვეყანა).** აპში ყიდვა არ არის. უცხოელი Android-ზე პრაქტიკულად მხოლოდ უფასო ვერსიას გამოიყენებს, სანამ Google Play-ის გადახდას არ ჩავრთავთ (კომპანია ქვეყანაში, სადაც Google ფულს რიცხავს) ან აშშ-ის ბმულის პროგრამას.
- **საიტი.** საქართველოდან შემოსულს ფასი ლარში ეჩვენება, სხვა ქვეყნიდან — დოლარში (BOG ჩამოჭრის დოლარში, გვერდი ინგლისურად).
- **VIN.** აუქციონის ისტორია და ფოტოები ძირითადად აშშ/კანადის მანქანებზეა. ევროპულ, იაპონურ, ჩინურ, ინდურ VIN-ზე ახლა აპი აჩვენებს მარკას, მწარმოებელს და ქვეყანას (ახალი ოფლაინ დეკოდერი), მაგრამ ისტორიას — არა. ამიტომ ამ ქვეყნების აღწერებში მთავარი OBD დიაგნოსტიკაა.
- **ჩინეთი.** Google Play მატერიკულ ჩინეთში არ მუშაობს. App Store China-ში ახალ აპს ICP რეგისტრაციის ნომერი სჭირდება, რომლის აღებაც უცხოელ დეველოპერს უჭირს. რჩევა: ჩინეთი ბოლოს, ან ჯერ App Store-ში ჩინეთი გამორთე. ჩინური აღწერა მაინც გამოგადგება ტაივანის/ჰონკონგის/მალაიზიის ჩინურენოვანი მომხმარებლებისთვის (ტრადიციული დამწერლობა ცალკე დასამატებელია).
- **იაპონია.** იაპონური ბაზრის მანქანებს ხშირად აქვს შასის ნომერი (მაგ. ZVW30-1234567) და არა 17-ნიშნა VIN — VIN ფუნქცია იქ თითქმის არ გამოდგება.
- **კანადა.** კვებეკში ფრანგული სავალდებულოა — ამიტომ fr-CA ცალკეა.


---

## საფრანგეთი — `fr-FR`

**სახელი (Play + App Store):** OBD360 : Diagnostic OBD2

**Apple subtitle:** Codes défaut et état du moteur

**Google მოკლე აღწერა:** Valise OBD2 : lire et effacer les codes défaut, données en direct, VE.

**Apple keywords:**
```
obd2,eobd,elm327,diagnostic,valise,voyant moteur,code defaut,dtc,abs,airbag,vin,entretien,hybride
```

**სრული აღწერა:**
```
OBD360 transforme votre téléphone et un adaptateur OBD2 (ELM327 Bluetooth, BLE ou Wi-Fi) en valise de diagnostic. L'application affiche uniquement ce que la voiture et l'adaptateur renvoient réellement : une valeur non prise en charge est indiquée comme indisponible, jamais inventée.

DIAGNOSTIC
• Lire et effacer les codes défaut (voyant moteur) avec une explication claire
• ABS, airbag, boîte de vitesses et autres calculateurs sur les véhicules compatibles (PRO)
• Données en direct et graphiques, freeze frame, préparation antipollution, Mode 06
• Assistant IA : causes probables et plan de contrôle (PRO)

ENTRETIEN
• Chaque élément suivi séparément : huile, filtres, bougies, liquide de frein, liquide de refroidissement, huile de boîte, plaquettes
• Intervalles constructeur Toyota/Lexus, Mercedes-Benz, Hyundai/Kia, BMW/MINI, VW/Audi, tous modifiables
• Kilométrage lu dans la voiture si possible, rappels avant l'échéance

CARBURANT ET ÉLECTRIQUE
• Consommation (L/100 km), historique des trajets et coût
• Électriques et hybrides : kWh/100 km, récupération d'énergie et batterie sur les modèles compatibles

ET AUSSI
• Chrono 0–100 km/h basé sur la vitesse du véhicule, vidéo avec données, classement
• Contrôle avant achat et check-list
• VIN : marque, pays et caractéristiques ; historique d'enchères pour les véhicules nord-américains (PRO)
• Rapport PDF et rapport QR à partager

Gratuit : codes défaut, données en direct, consommation, entretien, 0–100 et VIN de base. PRO est un abonnement mensuel résiliable à tout moment. Les fonctions dépendent du véhicule, du calculateur et de l'adaptateur. N'utilisez pas l'application en conduisant. Langue de l'application : anglais.
```

---

## კანადა (ინგლისური) — `en-CA`

**სახელი (Play + App Store):** OBD360: OBD2 Car Scanner

**Apple subtitle:** Check engine codes, car health

**Google მოკლე აღწერა:** OBD2 car scanner: read & clear check engine codes, live data, EV battery, 0–100.

**Apple keywords:**
```
obd2,obd,elm327,check engine,diagnostic,scanner,code reader,dtc,abs,srs,vin,carfax,ev,hybrid,0-100
```

**სრული აღწერა:**
```
OBD360 turns your phone and an OBD2 adapter (ELM327, Bluetooth, BLE or Wi-Fi) into a car scanner. It shows only what your car and adapter really report — anything unsupported is marked as unavailable, never invented.

DIAGNOSTICS
• Read and clear check engine codes (DTC) with plain-language explanations
• ABS, airbag (SRS), transmission and other modules on supported cars (PRO)
• Live data and graphs, freeze frame, emissions readiness, Mode 06
• AI assistant: likely causes and a step-by-step check plan (PRO)

SERVICE
• Every service item tracked on its own: oil, filters, spark plugs, brake fluid, coolant, transmission fluid, brake pads
• Manufacturer intervals for Toyota/Lexus, Mercedes-Benz, Hyundai/Kia, BMW/MINI, VW/Audi — every interval can be changed
• Odometer read from the car where supported, reminders before an item is due

FUEL AND ELECTRIC
• Fuel consumption, trip history and cost
• Electric and hybrid cars: kWh/100 km, regeneration and battery data on supported models

MORE
• 0–100 km/h timing from the car's own speed, video with data overlay, leaderboard
• Pre-purchase check and inspection checklist
• VIN check: make, country and specs; auction history and photos for US and Canadian cars (PRO)
• PDF report and a shareable QR report for buyers

Free: fault codes, live data, fuel, service, 0–100 and VIN basics. PRO is a monthly subscription; cancel any time. Features depend on the vehicle, ECU, protocol and adapter. Do not use the app while driving.
```

---

## კანადა (ფრანგული, კვებეკი) — `fr-CA`

**სახელი (Play + App Store):** OBD360 : Diagnostic OBD2

**Apple subtitle:** Codes moteur et état de l'auto

**Google მოკლე აღწერა:** Lecteur OBD2 : lire et effacer les codes du témoin moteur, données en direct.

**Apple keywords:**
```
obd2,elm327,diagnostic,lecteur,temoin moteur,check engine,code,dtc,abs,coussin,vin,entretien,hybride
```

**სრული აღწერა:**
```
OBD360 transforme votre téléphone et un adaptateur OBD2 (ELM327 Bluetooth, BLE ou Wi-Fi) en lecteur de diagnostic. L'application affiche uniquement ce que le véhicule et l'adaptateur renvoient réellement : une valeur non prise en charge est indiquée comme indisponible, jamais inventée.

DIAGNOSTIC
• Lire et effacer les codes du témoin moteur avec une explication claire
• ABS, airbag, boîte de vitesses et autres calculateurs sur les véhicules compatibles (PRO)
• Données en direct et graphiques, freeze frame, préparation antipollution, Mode 06
• Assistant IA : causes probables et plan de contrôle (PRO)

ENTRETIEN
• Chaque élément suivi séparément : huile, filtres, bougies, liquide de frein, liquide de refroidissement, huile de boîte, plaquettes
• Intervalles constructeur Toyota/Lexus, Mercedes-Benz, Hyundai/Kia, BMW/MINI, VW/Audi, tous modifiables
• Kilométrage lu dans le véhicule si possible, rappels avant l'échéance

CARBURANT ET ÉLECTRIQUE
• Consommation (L/100 km), historique des trajets et coût
• Véhicules électriques et hybrides : kWh/100 km, récupération d'énergie et batterie sur les modèles compatibles

ET AUSSI
• Chrono 0–100 km/h basé sur la vitesse du véhicule, vidéo avec données, classement
• Contrôle avant achat et check-list
• VIN : marque, pays et caractéristiques ; historique d'enchères pour les véhicules nord-américains (PRO)
• Rapport PDF et rapport QR à partager

Gratuit : codes moteur, données en direct, consommation, entretien, 0–100 et VIN de base. PRO est un abonnement mensuel résiliable à tout moment. Les fonctions dépendent du véhicule, du calculateur et de l'adaptateur. N'utilisez pas l'application en conduisant. Langue de l'application : anglais.
```

---

## იაპონია — `ja`

**სახელი (Play + App Store):** OBD360：OBD2 車両診断

**Apple subtitle:** 故障コードと車の状態をチェック

**Google მოკლე აღწერა:** OBD2スキャナー：故障コードの読取・消去、ライブデータ、EVバッテリー、0-100計測。

**Apple keywords:**
```
obd2,elm327,故障診断,スキャナー,故障コード,エンジン警告灯,dtc,abs,エアバッグ,車検,整備,燃費,ハイブリッド,ev
```

**სრული აღწერა:**
```
OBD360は、スマートフォンとOBD2アダプター（ELM327 Bluetooth／BLE／Wi-Fi）で車両診断ができるアプリです。車とアダプターが実際に返す値だけを表示し、対応していない項目は「利用不可」と表示します。

診断
• 故障コード（DTC）の読み取りと消去、わかりやすい説明付き
• 対応車種でABS、エアバッグ、トランスミッションなどのモジュール診断（PRO）
• ライブデータとグラフ、フリーズフレーム、排ガス準備状態、Mode 06
• AIアシスタント：考えられる原因と点検手順（PRO）

整備
• オイル、フィルター、プラグ、ブレーキフルード、冷却水、ミッションオイル、ブレーキパッドを個別に管理
• トヨタ／レクサス、メルセデス・ベンツ、ヒョンデ／キア、BMW／MINI、VW／アウディのメーカー推奨間隔（変更可）
• 対応車では走行距離を車から読み取り、交換時期の前に通知

燃費とEV
• 燃費（L/100km）、走行履歴とコスト
• EV・ハイブリッド：kWh/100km、回生、バッテリーデータ（対応車種）

その他
• 車速を使った0-100km/h計測、データ付き動画、ランキング
• 中古車購入前チェックと点検リスト
• PDFレポートとQRレポートの共有

無料：故障コード、ライブデータ、燃費、整備、0-100計測。PROは月額サブスクリプションで、いつでも解約できます。機能は車両・ECU・アダプターにより異なります。運転中は操作しないでください。アプリの表示言語：英語。
```

---

## ჩინეთი — `zh-Hans`

**სახელი (Play + App Store):** OBD360：OBD2汽车诊断

**Apple subtitle:** 读取故障码，了解车况

**Google მოკლე აღწერა:** OBD2汽车诊断：读取和清除故障码、实时数据、电动车电池、百公里加速。

**Apple keywords:**
```
obd2,elm327,汽车诊断,故障码,发动机故障灯,解码器,dtc,abs,安全气囊,保养,油耗,新能源,混动,vin
```

**სრული აღწერა:**
```
OBD360 让你的手机配合 OBD2 适配器（ELM327 蓝牙、BLE 或 Wi-Fi）成为汽车诊断工具。应用只显示车辆和适配器真实返回的数据；不支持的项目会标记为“不可用”，绝不编造。

诊断
• 读取和清除故障码（DTC），并提供通俗解释
• 支持车型可诊断 ABS、安全气囊、变速箱等模块（PRO）
• 实时数据与图表、冻结帧、排放就绪状态、Mode 06
• AI 助手：可能原因和检查步骤（PRO）

保养
• 机油、滤清器、火花塞、刹车油、冷却液、变速箱油、刹车片分别记录
• 丰田/雷克萨斯、奔驰、现代/起亚、宝马/MINI、大众/奥迪的厂家保养间隔，可自行修改
• 支持车型可从车上读取里程，到期前提醒

油耗与新能源
• 油耗（L/100km）、行程记录与费用
• 纯电和混动：kWh/100km、能量回收和电池数据（支持车型）

更多
• 基于车速的 0–100 km/h 计时、带数据的视频、排行榜
• 购车前检查和检查清单
• PDF 报告和可分享的二维码报告

免费：故障码、实时数据、油耗、保养、0–100 计时。PRO 为按月订阅，可随时取消。功能取决于车辆、ECU 和适配器。驾驶时请勿操作。应用语言：英语。
```

---

## ინდოეთი (ინგლისური) — `en-IN`

**სახელი (Play + App Store):** OBD360: OBD2 Car Scanner

**Apple subtitle:** Engine codes, service, mileage

**Google მოკლე აღწერა:** OBD2 car scanner: read & clear engine fault codes, live data, service, mileage.

**Apple keywords:**
```
obd2,obd,elm327,engine,diagnostic,scanner,fault code,dtc,abs,airbag,service,mileage,cng,ev,hybrid
```

**სრული აღწერა:**
```
OBD360 turns your phone and an OBD2 adapter (ELM327, Bluetooth, BLE or Wi-Fi) into a car scanner. It shows only what your car and adapter really report — anything unsupported is marked as unavailable, never invented.

DIAGNOSTICS
• Read and clear engine fault codes (DTC) with plain-language explanations
• ABS, airbag (SRS), transmission and other modules on supported cars (PRO)
• Live data and graphs, freeze frame, emissions readiness, Mode 06
• AI assistant: likely causes and a step-by-step check plan (PRO)

SERVICE
• Every service item tracked on its own: oil, filters, spark plugs, brake fluid, coolant, transmission fluid, brake pads
• Manufacturer intervals for Toyota/Lexus, Mercedes-Benz, Hyundai/Kia, BMW/MINI, VW/Audi — every interval can be changed
• Odometer read from the car where supported, reminders before an item is due

FUEL AND ELECTRIC
• Mileage (fuel consumption), trip history and cost
• Electric and hybrid cars: kWh/100 km, regeneration and battery data on supported models

MORE
• 0–100 km/h timing from the car's own speed, video with data overlay, leaderboard
• Pre-purchase check and inspection checklist
• VIN check: make, country and specs; auction history and photos for US and Canadian cars (PRO)
• PDF report and a shareable QR report for buyers

Free: fault codes, live data, fuel, service, 0–100 and VIN basics. PRO is a monthly subscription; cancel any time. Features depend on the vehicle, ECU, protocol and adapter. Do not use the app while driving.
```

---

## ინდოეთი (ჰინდი) — `hi`

**სახელი (Play + App Store):** OBD360: OBD2 कार स्कैनर

**Apple subtitle:** इंजन कोड, सर्विस और माइलेज

**Google მოკლე აღწერა:** OBD2 कार स्कैनर: इंजन फॉल्ट कोड पढ़ें और मिटाएँ, लाइव डेटा, सर्विस रिमाइंडर।

**Apple keywords:**
```
obd2,elm327,कार स्कैनर,इंजन लाइट,फॉल्ट कोड,डायग्नोस्टिक,dtc,abs,एयरबैग,सर्विस,माइलेज,cng,ev
```

**სრული აღწერა:**
```
OBD360 आपके फ़ोन और OBD2 एडैप्टर (ELM327 ब्लूटूथ, BLE या Wi-Fi) को कार स्कैनर बना देता है। ऐप वही दिखाता है जो आपकी कार और एडैप्टर सच में भेजते हैं — जो उपलब्ध नहीं है उसे "उपलब्ध नहीं" लिखा जाता है, कभी गढ़ा नहीं जाता।

डायग्नोस्टिक
• इंजन फॉल्ट कोड (DTC) पढ़ें और मिटाएँ, आसान भाषा में मतलब के साथ
• समर्थित कारों में ABS, एयरबैग, ट्रांसमिशन और दूसरे मॉड्यूल (PRO)
• लाइव डेटा और ग्राफ़, फ्रीज़ फ्रेम, एमिशन रेडीनेस, Mode 06
• AI सहायक: संभावित कारण और जाँच की योजना (PRO)

सर्विस
• हर पार्ट अलग से: इंजन ऑयल, फ़िल्टर, स्पार्क प्लग, ब्रेक फ़्लूइड, कूलेंट, गियर ऑयल, ब्रेक पैड
• कंपनी के सर्विस अंतराल (Toyota, Hyundai/Kia, Mercedes-Benz, BMW, VW/Audi) — सब बदले जा सकते हैं
• समर्थित कारों में ओडोमीटर कार से, समय से पहले रिमाइंडर

ईंधन और इलेक्ट्रिक
• माइलेज, यात्रा इतिहास और खर्च
• इलेक्ट्रिक और हाइब्रिड: kWh/100 km और बैटरी डेटा (समर्थित मॉडल)

और भी
• कार की अपनी स्पीड से 0–100 km/h समय, डेटा वाला वीडियो
• सेकंड-हैंड कार ख़रीदने से पहले जाँच
• PDF रिपोर्ट और QR रिपोर्ट

मुफ़्त: फॉल्ट कोड, लाइव डेटा, माइलेज, सर्विस, 0–100। PRO मासिक सब्सक्रिप्शन है, कभी भी रद्द करें। सुविधाएँ कार, ECU और एडैप्टर पर निर्भर हैं। गाड़ी चलाते समय ऐप न चलाएँ। ऐप की भाषा: अंग्रेज़ी।
```

---

## აშშ — `en-US`

**სახელი (Play + App Store):** OBD360: OBD2 Car Scanner

**Apple subtitle:** Check engine codes, car health

**Google მოკლე აღწერა:** OBD2 car scanner: read & clear check engine codes, live data, EV battery, 0–60.

**Apple keywords:**
```
obd2,obd,elm327,check engine,diagnostic,scanner,code reader,dtc,abs,srs,vin,ev,hybrid,mileage,0-60
```

**სრული აღწერა:**
```
OBD360 turns your phone and an OBD2 adapter (ELM327, Bluetooth, BLE or Wi-Fi) into a car scanner. It shows only what your car and adapter really report — anything unsupported is marked as unavailable, never invented.

DIAGNOSTICS
• Read and clear check engine codes (DTC) with plain-language explanations
• ABS, airbag (SRS), transmission and other modules on supported cars (PRO)
• Live data and graphs, freeze frame, emissions readiness, Mode 06
• AI assistant: likely causes and a step-by-step check plan (PRO)

SERVICE
• Every service item tracked on its own: oil, filters, spark plugs, brake fluid, coolant, transmission fluid, brake pads
• Manufacturer intervals for Toyota/Lexus, Mercedes-Benz, Hyundai/Kia, BMW/MINI, VW/Audi — every interval can be changed
• Odometer read from the car where supported, reminders before an item is due

FUEL AND ELECTRIC
• Fuel consumption, trip history and cost
• Electric and hybrid cars: kWh/100 km, regeneration and battery data on supported models

MORE
• 0–60 mph / 0–100 km/h timing from the car's own speed, video with data overlay, leaderboard
• Pre-purchase check and inspection checklist
• VIN check: make, country and specs; auction history and photos for US and Canadian cars (PRO)
• PDF report and a shareable QR report for buyers

Free: fault codes, live data, fuel, service, 0–60 and VIN basics. PRO is a monthly subscription; cancel any time. Features depend on the vehicle, ECU, protocol and adapter. Do not use the app while driving.
```

---

## დიდი ბრიტანეთი — `en-GB`

**სახელი (Play + App Store):** OBD360: OBD2 Car Scanner

**Apple subtitle:** Engine fault codes, car health

**Google მოკლე აღწერა:** OBD2 car scanner: read & clear engine fault codes, live data, EV battery, 0–100.

**Apple keywords:**
```
obd2,eobd,elm327,engine light,diagnostic,scanner,fault code,dtc,abs,airbag,vin,ev,hybrid,service,mot
```

**სრული აღწერა:**
```
OBD360 turns your phone and an OBD2 adapter (ELM327, Bluetooth, BLE or Wi-Fi) into a car scanner. It shows only what your car and adapter really report — anything unsupported is marked as unavailable, never invented.

DIAGNOSTICS
• Read and clear engine fault codes (DTC) with plain-language explanations
• ABS, airbag (SRS), transmission and other modules on supported cars (PRO)
• Live data and graphs, freeze frame, emissions readiness, Mode 06
• AI assistant: likely causes and a step-by-step check plan (PRO)

SERVICE
• Every service item tracked on its own: oil, filters, spark plugs, brake fluid, coolant, transmission fluid, brake pads
• Manufacturer intervals for Toyota/Lexus, Mercedes-Benz, Hyundai/Kia, BMW/MINI, VW/Audi — every interval can be changed
• Odometer read from the car where supported, reminders before an item is due

FUEL AND ELECTRIC
• Fuel consumption, trip history and cost
• Electric and hybrid cars: kWh/100 km, regeneration and battery data on supported models

MORE
• 0–100 km/h timing from the car's own speed, video with data overlay, leaderboard
• Pre-purchase check and inspection checklist
• VIN check: make, country and specs; auction history and photos for US and Canadian cars (PRO)
• PDF report and a shareable QR report for buyers

Free: fault codes, live data, fuel, service, 0–100 and VIN basics. PRO is a monthly subscription; cancel any time. Features depend on the vehicle, ECU, protocol and adapter. Do not use the app while driving.
```

---

## გერმანია — `de-DE`

**სახელი (Play + App Store):** OBD360: OBD2 Auto Diagnose

**Apple subtitle:** Fehlercodes & Fahrzeugzustand

**Google მოკლე აღწერა:** OBD2 Diagnose: Fehlercodes lesen und löschen, Live-Daten, E-Auto-Akku, 0–100.

**Apple keywords:**
```
obd2,eobd,elm327,diagnose,fehlerspeicher,fehlercode,motorleuchte,abs,airbag,vin,service,hybrid
```

**სრული აღწერა:**
```
OBD360 macht aus Ihrem Smartphone und einem OBD2-Adapter (ELM327 Bluetooth, BLE oder WLAN) ein Diagnosegerät. Die App zeigt nur, was Fahrzeug und Adapter wirklich liefern – nicht unterstützte Werte werden als nicht verfügbar markiert, nie erfunden.

DIAGNOSE
• Fehlerspeicher lesen und löschen, mit verständlicher Erklärung
• ABS, Airbag, Getriebe und weitere Steuergeräte bei unterstützten Fahrzeugen (PRO)
• Live-Daten und Diagramme, Freeze Frame, Abgas-Bereitschaft, Mode 06
• KI-Assistent: wahrscheinliche Ursachen und Prüfplan (PRO)

SERVICE
• Jedes Wartungsteil einzeln: Öl, Filter, Zündkerzen, Bremsflüssigkeit, Kühlmittel, Getriebeöl, Bremsbeläge
• Herstellerintervalle für Toyota/Lexus, Mercedes-Benz, Hyundai/Kia, BMW/MINI, VW/Audi – alle anpassbar
• Kilometerstand aus dem Fahrzeug, wo verfügbar, Erinnerungen vor Fälligkeit

KRAFTSTOFF UND ELEKTRO
• Verbrauch (L/100 km), Fahrtenverlauf und Kosten
• E-Autos und Hybride: kWh/100 km, Rekuperation und Akkudaten bei unterstützten Modellen

AUSSERDEM
• 0–100 km/h Messung mit der Fahrzeuggeschwindigkeit, Video mit Daten, Rangliste
• Gebrauchtwagen-Check und Prüfliste
• VIN: Marke, Herkunftsland und Daten; Auktionshistorie für nordamerikanische Fahrzeuge (PRO)
• PDF-Bericht und teilbarer QR-Bericht

Kostenlos: Fehlercodes, Live-Daten, Verbrauch, Service, 0–100 und VIN-Basisdaten. PRO ist ein Monatsabo, jederzeit kündbar. Funktionen hängen von Fahrzeug, Steuergerät und Adapter ab. App nicht während der Fahrt bedienen. App-Sprache: Englisch.
```

---

## ესპანეთი — `es-ES`

**სახელი (Play + App Store):** OBD360: Diagnosis OBD2

**Apple subtitle:** Códigos de avería y estado

**Google მოკლე აღწერა:** Escáner OBD2: lee y borra códigos de avería, datos en vivo, batería VE, 0–100.

**Apple keywords:**
```
obd2,eobd,elm327,diagnosis,escaner,codigo averia,testigo motor,dtc,abs,airbag,vin,revision,hibrido
```

**სრული აღწერა:**
```
OBD360 convierte tu móvil y un adaptador OBD2 (ELM327 Bluetooth, BLE o Wi-Fi) en una máquina de diagnosis. Solo muestra lo que el coche y el adaptador devuelven de verdad: lo que no está disponible se indica así, nunca se inventa.

DIAGNOSIS
• Leer y borrar códigos de avería con explicación clara
• ABS, airbag, cambio y otras centralitas en coches compatibles (PRO)
• Datos en vivo y gráficos, freeze frame, preparación de emisiones, Mode 06
• Asistente IA: causas probables y plan de revisión (PRO)

MANTENIMIENTO
• Cada elemento por separado: aceite, filtros, bujías, líquido de frenos, refrigerante, aceite de caja, pastillas
• Intervalos del fabricante para Toyota/Lexus, Mercedes-Benz, Hyundai/Kia, BMW/MINI, VW/Audi, todos editables
• Kilometraje leído del coche cuando es posible y avisos antes del vencimiento

COMBUSTIBLE Y ELÉCTRICO
• Consumo (L/100 km), historial de viajes y coste
• Eléctricos e híbridos: kWh/100 km, regeneración y batería en modelos compatibles

ADEMÁS
• Cronómetro 0–100 km/h con la velocidad del coche, vídeo con datos, clasificación
• Revisión antes de comprar y lista de comprobación
• Informe PDF e informe QR para compartir

Gratis: códigos de avería, datos en vivo, consumo, mantenimiento y 0–100. PRO es una suscripción mensual que puedes cancelar cuando quieras. Las funciones dependen del vehículo, la centralita y el adaptador. No uses la app mientras conduces. Idioma de la app: inglés.
```

---

## რუსულენოვანი — `ru`

**სახელი (Play + App Store):** OBD360: OBD2 диагностика

**Apple subtitle:** Коды ошибок и состояние авто

**Google მოკლე აღწერა:** OBD2 сканер: чтение и сброс ошибок, данные в реальном времени, батарея, 0–100.

**Apple keywords:**
```
obd2,elm327,диагностика,сканер,ошибки,чек,dtc,abs,airbag,vin,то,пробег,расход,гибрид,электромобиль
```

**სრული აღწერა:**
```
OBD360 превращает телефон и адаптер OBD2 (ELM327 Bluetooth, BLE или Wi-Fi) в автосканер. Приложение показывает только то, что реально передают автомобиль и адаптер: неподдерживаемое помечается как недоступное и никогда не выдумывается.

ДИАГНОСТИКА
• Чтение и сброс кодов ошибок с понятным объяснением
• ABS, подушки безопасности, коробка и другие блоки на поддерживаемых авто (PRO)
• Данные в реальном времени и графики, стоп-кадр, готовность систем, Mode 06
• ИИ-помощник: вероятные причины и план проверки (PRO)

ОБСЛУЖИВАНИЕ
• Каждая позиция отдельно: масло, фильтры, свечи, тормозная жидкость, антифриз, масло КПП, колодки
• Интервалы производителя Toyota/Lexus, Mercedes-Benz, Hyundai/Kia, BMW/MINI, VW/Audi — все можно изменить
• Пробег из автомобиля, где это возможно, напоминания заранее

РАСХОД И ЭЛЕКТРО
• Расход (л/100 км), история поездок и стоимость
• Электромобили и гибриды: кВт·ч/100 км, рекуперация и данные батареи на поддерживаемых моделях

А ТАКЖЕ
• Замер 0–100 км/ч по скорости автомобиля, видео с данными, рейтинг
• Проверка перед покупкой и чек-лист
• VIN: марка, страна и характеристики; история аукционов для авто из США и Канады (PRO)
• PDF-отчёт и QR-отчёт для покупателя

Бесплатно: коды ошибок, данные, расход, ТО, 0–100 и базовый VIN. PRO — ежемесячная подписка, отменить можно в любой момент. Функции зависят от авто, блока и адаптера. Не пользуйтесь приложением за рулём. Язык приложения: английский.
```

---

## თურქეთი — `tr`

**სახელი (Play + App Store):** OBD360: OBD2 Arıza Tespit

**Apple subtitle:** Arıza kodları ve araç durumu

**Google მოკლე აღწერა:** OBD2 arıza tespit: arıza kodlarını oku ve sil, canlı veri, EV bataryası, 0–100.

**Apple keywords:**
```
obd2,elm327,arıza tespit,arıza kodu,motor lambası,dtc,abs,airbag,vin,bakım,yakıt,hibrit,elektrikli
```

**სრული აღწერა:**
```
OBD360, telefonunuzu ve bir OBD2 adaptörünü (ELM327 Bluetooth, BLE veya Wi-Fi) araç arıza tespit cihazına dönüştürür. Uygulama yalnızca aracın ve adaptörün gerçekten gönderdiği verileri gösterir; desteklenmeyenler "kullanılamıyor" olarak işaretlenir, asla uydurulmaz.

ARIZA TESPİT
• Arıza kodlarını (DTC) okuma ve silme, anlaşılır açıklamayla
• Desteklenen araçlarda ABS, airbag, şanzıman ve diğer modüller (PRO)
• Canlı veri ve grafikler, freeze frame, emisyon hazırlığı, Mode 06
• Yapay zekâ asistanı: olası nedenler ve kontrol planı (PRO)

BAKIM
• Her parça ayrı ayrı: yağ, filtreler, bujiler, fren hidroliği, antifriz, şanzıman yağı, balatalar
• Toyota/Lexus, Mercedes-Benz, Hyundai/Kia, BMW/MINI, VW/Audi üretici aralıkları, hepsi değiştirilebilir
• Destekleniyorsa kilometre araçtan okunur, zamanı gelmeden hatırlatma

YAKIT VE ELEKTRİKLİ
• Yakıt tüketimi (L/100 km), yolculuk geçmişi ve maliyet
• Elektrikli ve hibrit: kWh/100 km, rejenerasyon ve batarya verisi (desteklenen modeller)

DAHA FAZLASI
• Aracın kendi hızıyla 0–100 km/s ölçümü, verili video, sıralama
• Satın alma öncesi kontrol ve ekspertiz listesi
• PDF rapor ve paylaşılabilir QR rapor

Ücretsiz: arıza kodları, canlı veri, yakıt, bakım ve 0–100. PRO aylık abonelik, istediğiniz zaman iptal edebilirsiniz. Özellikler araca, ECU'ya ve adaptöre bağlıdır. Sürüş sırasında uygulamayı kullanmayın. Uygulama dili: İngilizce.
```
