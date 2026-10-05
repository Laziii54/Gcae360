using System.Text.Json;

namespace CarScanner.Services;

public enum InspectionSeverity
{
    Minor,
    Major,
    Dangerous
}

public enum InspectionAnswer
{
    None = 0,
    Ok = 1,
    Problem = 2,
    Unknown = 3
}

public sealed record InspectionCategory(int Index, string Ka, string En, string IconPath);

public sealed record InspectionQuestion(
    string Id,
    int Category,
    InspectionSeverity Severity,
    string Ka,
    string En,
    string ShortKa,
    string ShortEn,
    string HowKa,
    string HowEn,
    string FixKa,
    string FixEn);

/// <summary>
/// Self-check before the official technical inspection. The nine categories follow
/// the order used by Georgian inspection centers (Government Resolution No. 509).
/// Every question is phrased so that "OK" is the passing answer.
/// </summary>
public static class InspectionChecklist
{
    public static IReadOnlyList<InspectionCategory> Categories { get; } = new[]
    {
        new InspectionCategory(1, "იდენტიფიკაცია", "Identification", "M4,5 L20,5 L20,19 L4,19 Z M8,10 L8.01,10 M11,10 L16,10 M8,14 L16,14"),
        new InspectionCategory(2, "სამუხრუჭე სისტემა", "Brakes", "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8"),
        new InspectionCategory(3, "საჭის მართვა", "Steering", "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,10 A2,2 0 1 0 12,14 A2,2 0 1 0 12,10 M12,14 L12,21 M10.2,11 L3.5,9 M13.8,11 L20.5,9"),
        new InspectionCategory(4, "ხილვადობა", "Visibility", "M2,12 C5,6 9,5 12,5 C15,5 19,6 22,12 C19,18 15,19 12,19 C9,19 5,18 2,12 Z M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9"),
        new InspectionCategory(5, "განათება", "Lights", "M9,18 L15,18 M10,21 L14,21 M12,3 C8.7,3 6,5.7 6,9 C6,11 7,12.4 8,13.5 C8.8,14.3 9,15 9,16 L15,16 C15,15 15.2,14.3 16,13.5 C17,12.4 18,11 18,9 C18,5.7 15.3,3 12,3 Z"),
        new InspectionCategory(6, "სავალი ნაწილი და საბურავები", "Axles, wheels and tires", "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7"),
        new InspectionCategory(7, "შასი და ძარა", "Chassis and body", "M3,17 L21,17 M5,17 L5,9 L8,6 L16,6 L19,9 L19,17 M8,13 L16,13"),
        new InspectionCategory(8, "უსაფრთხოება", "Safety equipment", "M12,3 L20,6 L20,12 C20,17 16.5,20 12,21 C7.5,20 4,17 4,12 L4,6 Z"),
        new InspectionCategory(9, "ეკოლოგია", "Emissions and leaks", "M5,19 C5,11 10,5 19,5 C19,14 13,19 5,19 Z M5,19 L12,12"),
    };

    private static InspectionQuestion Q(
        string id, int category, InspectionSeverity severity,
        string ka, string en, string shortKa, string shortEn,
        string howKa, string howEn, string fixKa = "", string fixEn = "") =>
        new(id, category, severity, ka, en, shortKa, shortEn, howKa, howEn, fixKa, fixEn);

    private const InspectionSeverity Minor = InspectionSeverity.Minor;
    private const InspectionSeverity Major = InspectionSeverity.Major;
    private const InspectionSeverity Dangerous = InspectionSeverity.Dangerous;

    public static IReadOnlyList<InspectionQuestion> Questions { get; } = new[]
    {
        // 1. Identification
        Q("id_plates", 1, Major,
            "სანომრე ნიშნები ორივე მხარეს არის, მთელია და კარგად იკითხება?",
            "Are both number plates present, undamaged and easy to read?",
            "სანომრე ნიშანი დაზიანებულია ან არ იკითხება", "Number plate damaged or unreadable",
            "გადი 3–4 მეტრით და წაიკითხე. ნომერი არ უნდა იყოს გადაღუნული, გახუნებული, ჭუჭყით ან ჩარჩოთი დაფარული.",
            "Step back 3–4 m and read it. The plate must not be bent, faded, dirty or covered by a frame.",
            "გაწმინდე, გაასწორე ან შეცვალე სანომრე ნიშანი.", "Clean, straighten or replace the plate."),
        Q("id_vin", 1, Major,
            "ძარაზე VIN კოდი იკითხება და ემთხვევა სარეგისტრაციო მოწმობას?",
            "Is the VIN on the body readable and does it match the registration certificate?",
            "VIN არ იკითხება ან არ ემთხვევა მოწმობას", "VIN unreadable or does not match the certificate",
            "VIN ჩვეულებრივ წერია საქარე მინის ქვედა კუთხეში, მძღოლის კარის ზღურბლზე ან ძრავის ფარზე. შეადარე მოწმობას ყველა 17 სიმბოლო. OBD შემოწმება ECU-ში ჩაწერილ VIN-საც წაიკითხავს.",
            "The VIN is usually at the bottom corner of the windshield, on the driver's door sill or on the firewall. Compare all 17 characters with the certificate. The OBD check also reads the VIN stored in the ECU.",
            "შეუსაბამობის მიზეზი ტექცენტრამდე გაარკვიე რეგისტრაციის სამსახურში.", "Resolve the mismatch with the registration office before the inspection."),

        // 2. Brakes
        Q("br_pedal", 2, Dangerous,
            "სამუხრუჭე პედალი მაგარია და დაჭერისას ნელ-ნელა არ იძირება?",
            "Is the brake pedal firm and does it hold without slowly sinking?",
            "სამუხრუჭე პედალი რბილია ან იძირება", "Brake pedal is soft or sinks",
            "ძრავა ჩართულია. დააწექი პედალს საშუალო ძალით და დაიჭირე 10 წამი. თუ რბილია ან ნელ-ნელა იძირება, სისტემაში ჰაერი ან გაჟონვაა.",
            "With the engine running, press the pedal with moderate force and hold it for 10 s. A soft or sinking pedal means air or a leak in the system.",
            "სასწრაფოდ შეამოწმე სამუხრუჭე სისტემა ხელოსანთან.", "Have the brake system checked immediately."),
        Q("br_fluid", 2, Major,
            "სამუხრუჭე სითხის დონე MIN და MAX ნიშნულებს შორისაა?",
            "Is the brake fluid level between MIN and MAX?",
            "სამუხრუჭე სითხე MIN-ზე დაბალია", "Brake fluid below MIN",
            "ავზი კაპოტის ქვეშაა, ჩვეულებრივ მძღოლის მხარეს, ყვითელი თავსახურით. დონე ავზის გვერდზე ჩანს.",
            "The reservoir is under the hood, usually on the driver's side with a yellow cap. The level is visible on its side.",
            "დაამატე სითხე და გაარკვიე, რატომ იკლებს (ხუნდების ცვეთა ან გაჟონვა).", "Top up and find out why it dropped (pad wear or a leak)."),
        Q("br_lines", 2, Dangerous,
            "სამუხრუჭე მილებსა და შლანგებზე გაჟონვა, ბზარი ან გაბერილობა არ ჩანს?",
            "Are the brake pipes and hoses free of leaks, cracks and bulges?",
            "სამუხრუჭე შლანგი ან მილი დაზიანებულია", "Brake hose or pipe damaged",
            "თითოეულ ბორბალთან შეხედე რეზინის შლანგს: არ უნდა იყოს დაბზარული, გაბერილი ან სველი. ბორბლის შიგნით სველი ლაქაც გაჟონვის ნიშანია.",
            "Look at the rubber hose at each wheel: it must not be cracked, swollen or wet. A wet patch inside the wheel also means a leak.",
            "შეცვალე დაზიანებული შლანგი ან მილი.", "Replace the damaged hose or pipe."),
        Q("br_pads", 2, Major,
            "ხუნდები და დისკები ზედმეტად გაცვეთილი არ არის?",
            "Are the brake pads and discs within their wear limits?",
            "ხუნდები ან დისკები გაცვეთილია", "Brake pads or discs worn",
            "ბორბლის სხივებს შორის დახედე: ხუნდის ხახუნის ფენა 3 მმ-ზე სქელი უნდა იყოს, დისკზე ღრმა ღარები და კიდეზე მაღალი ქიმი არ უნდა იყოს. დამუხრუჭებისას ღრჭიალი ცვეთის ნიშანია.",
            "Look between the wheel spokes: pad friction material should be thicker than 3 mm, with no deep grooves on the disc or a high lip at its edge. Squealing when braking is a wear sign.",
            "შეცვალე ხუნდები, საჭიროების შემთხვევაში დისკებიც.", "Replace the pads, and the discs if needed."),
        Q("br_pull", 2, Major,
            "ძლიერი დამუხრუჭებისას მანქანა გვერდზე არ იწევს და საჭე არ ცახცახებს?",
            "When braking hard, does the car stay straight without the steering wheel shaking?",
            "დამუხრუჭებისას მანქანა გვერდზე იწევს", "Car pulls to one side when braking",
            "ცარიელ, სწორ გზაზე 40–50 კმ/სთ-ზე ძლიერად დაამუხრუჭე და საჭე მსუბუქად დაიჭირე. გვერდზე გაწევა მარცხენა და მარჯვენა ბორბლის ძალის სხვაობაზე მიუთითებს. სტენდზე ერთ ღერძზე ეს სხვაობა 30%-ს არ უნდა აღემატებოდეს.",
            "On an empty straight road, brake hard from 40–50 km/h holding the wheel lightly. Pulling means unequal left/right braking force. On the test bench this difference on one axle must not exceed 30%.",
            "შეამოწმე სუპორტები, ხუნდები და შლანგები იმ მხარეს, საითაც იწევს.", "Check the calipers, pads and hoses on the side it pulls to."),
        Q("br_park", 2, Major,
            "ხელის მუხრუჭი მანქანას დაღმართზე აჩერებს?",
            "Does the parking brake hold the car on a slope?",
            "ხელის მუხრუჭი მანქანას ვერ აჩერებს", "Parking brake does not hold",
            "დადექი დაღმართზე, აწიე ხელის მუხრუჭი და გადართე ნეიტრალზე (ავტომატიკაზე N). მანქანა ადგილიდან არ უნდა დაიძრას.",
            "Stop on a slope, apply the parking brake and select neutral (N on an automatic). The car must not move.",
            "დაარეგულირე ან შეაკეთე ხელის მუხრუჭი.", "Adjust or repair the parking brake."),
        Q("br_lamp", 2, Major,
            "ABS-ის და მუხრუჭის გამაფრთხილებელი ნათურები ძრავის ჩართვის შემდეგ ქრება?",
            "Do the ABS and brake warning lights go out after the engine starts?",
            "ABS-ის ან მუხრუჭის ნათურა ანთია", "ABS or brake warning light is on",
            "ანთების ჩართვისას ნათურები წამიერად ინთება და ძრავის ჩართვის შემდეგ უნდა ჩაქრეს. PRO ვერსიაში მოდულების დიაგნოსტიკა ABS-ის შეცდომებს წაიკითხავს.",
            "The lights come on briefly with the ignition and must go out once the engine runs. In PRO, module diagnostics reads the ABS fault codes.",
            "წაიკითხე ABS მოდულის შეცდომები და აღმოფხვერი მიზეზი.", "Read the ABS module codes and fix the cause."),

        // 3. Steering
        Q("st_play", 3, Major,
            "საჭის თავისუფალი სვლა (ლუფტი) დიდი არ არის?",
            "Is the steering free play small?",
            "საჭის ლუფტი დიდია", "Excessive steering play",
            "ძრავა ჩართულია, ბორბლები სწორად დგას. ნელა მოაბრუნე საჭე ერთ მხარეს, სანამ ბორბალი არ დაიძვრება. მსუბუქ მანქანაზე ეს დაახლოებით 10°-ზე ნაკლები უნდა იყოს (საჭის რკალზე ≈ 2 თითი).",
            "Engine running, wheels straight. Slowly turn the steering wheel until the road wheel starts to move. On a car this should be under about 10° (≈ two fingers on the rim).",
            "შეამოწმე საჭის წევები, ბოლოები და რეიკა.", "Check the tie rods, tie-rod ends and steering rack."),
        Q("st_noise", 3, Major,
            "საჭის ტრიალისას და ორმოებზე კაკუნი, ჭახუნი ან გაჭედვა არ არის?",
            "Is the steering free of knocks, clunks and tight spots?",
            "საჭის მექანიზმში კაკუნი ან გაჭედვაა", "Knocking or binding in the steering",
            "ადგილზე ბოლომდე მოატრიალე საჭე ორივე მხარეს და მოუსმინე. ორმოებზე საჭეში კაკუნი წევების ან საჭის ბოლოების ცვეთაზე მიუთითებს.",
            "Turn the wheel fully both ways while stationary and listen. Knocks felt in the wheel over bumps point to worn rods or ends.",
            "შეცვალე გაცვეთილი საჭის დეტალები.", "Replace the worn steering parts."),
        Q("st_assist", 3, Major,
            "საჭის გამაძლიერებელი მუშაობს და მისი გამაფრთხილებელი ნათურა არ ანთია?",
            "Does the power steering work, with its warning light off?",
            "საჭის გამაძლიერებელი გაუმართავია", "Power steering fault",
            "საჭე ადგილზე მსუბუქად უნდა ტრიალებდეს. ჰიდრავლიკაზე შეამოწმე სითხის დონე და გაჟონვა, ელექტრო გამაძლიერებელზე (EPS) დაფაზე საჭის ნათურა არ უნდა ანთოს.",
            "The wheel should turn lightly at standstill. With hydraulic steering check the fluid level and leaks; with electric steering (EPS) no steering warning light may be on.",
            "შეამოწმე ტუმბო, სითხე ან EPS-ის შეცდომები.", "Check the pump and fluid, or the EPS fault codes."),

        // 4. Visibility
        Q("vi_glass", 4, Major,
            "საქარე მინაზე, მძღოლის მხარეს, ბზარი ან ნახეთქი არ არის?",
            "Is the windshield free of cracks and chips on the driver's side?",
            "საქარე მინაზე ბზარია მძღოლის მხარეს", "Windshield crack on the driver's side",
            "დაჯექი მძღოლის ადგილას და დახედე მინამწმენდის მოქმედების ზონას. ამ ზონაში ბზარი დაუშვებელია, პატარა ნახეთქიც კი. მგზავრის მხარეს პატარა ნახეთქი, როგორც წესი, პრობლემა არ არის.",
            "Sit in the driver's seat and look at the wiper area. No crack is allowed there, not even a small chip. A small chip on the passenger side is usually not a problem.",
            "შეცვალე მინა ან გაარემონტე ნახეთქი, სანამ გაიზრდება.", "Replace the windshield or repair the chip before it spreads."),
        Q("vi_wipers", 4, Major,
            "მინამწმენდები კარგად წმენდს და სარეცხი წყალს ასხამს?",
            "Do the wipers clear the glass and does the washer spray?",
            "მინამწმენდი ან სარეცხი არ მუშაობს", "Wipers or washer not working",
            "ჩართე მინამწმენდის ყველა სიჩქარე და სარეცხი. ჯაგრისი ზოლებს არ უნდა ტოვებდეს, ავზში სითხე უნდა იყოს.",
            "Try every wiper speed and the washer. The blades must not leave streaks and the reservoir must have fluid.",
            "შეცვალე ჯაგრისები, შეავსე ან შეაკეთე სარეცხი.", "Replace the blades and refill or repair the washer."),
        Q("vi_mirrors", 4, Major,
            "გარე და შიდა სარკეები მთელია და მყარად დგას?",
            "Are the outside and inside mirrors intact and firmly fixed?",
            "სარკე გატეხილია ან არ არის დამაგრებული", "Mirror broken or loose",
            "სარკე არ უნდა იყოს გატეხილი, და მოძრაობისას არ უნდა ცახცახებდეს ან თავისით ტრიალებდეს.",
            "No mirror may be broken, and none may shake or move by itself while driving.",
            "შეცვალე სარკე ან მისი მინა.", "Replace the mirror or its glass."),
        Q("vi_tint", 4, Major,
            "წინა მინა და წინა გვერდითი მინები ფირით ან ზედმეტად დაბურული არ არის?",
            "Are the windshield and front side windows free of film and excessive tint?",
            "მინები ნორმაზე მეტადაა დაბურული", "Windows tinted beyond the limit",
            "ფირი ან ქარხნულზე მუქი დაბურვა წინა მინაზე და მძღოლისა და მგზავრის გვერდით მინებზე ნორმის გადაჭარბებად ითვლება. ტექცენტრში გამჭვირვალობას ხელსაწყოთი ზომავენ.",
            "Film or darker-than-factory tint on the windshield or the front side windows counts as exceeding the limit. The center measures light transmission with a meter.",
            "მოაშორე ფირი წინა და წინა გვერდითი მინებიდან.", "Remove the film from the windshield and front side windows."),

        // 5. Lights
        Q("li_low", 5, Major,
            "ახლო შუქი ორივე ფარში ანთია?",
            "Do both dipped (low) beams work?",
            "ახლო შუქი არ ანთია", "Low beam not working",
            "ჩართე ახლო შუქი და მანქანის წინ გადი, ან კედლის ანარეკლში ნახე.",
            "Switch on the low beams and look from the front, or at the reflection on a wall.",
            "შეცვალე ნათურა.", "Replace the bulb."),
        Q("li_high", 5, Major,
            "შორი შუქი ორივე ფარში ანთია?",
            "Do both main (high) beams work?",
            "შორი შუქი არ ანთია", "High beam not working",
            "ჩართე შორი შუქი. დაფაზე ლურჯი ნათურაც უნდა ანთოს.",
            "Switch on the high beams. The blue indicator on the dash must also come on.",
            "შეცვალე ნათურა.", "Replace the bulb."),
        Q("li_aim", 5, Major,
            "ფარების შუშა მთელია და შუქი სწორად, დაბლა ანათებს?",
            "Are the headlight lenses intact and correctly aimed downwards?",
            "ფარი გატეხილია ან არასწორად ანათებს", "Headlight broken or badly aimed",
            "ღამით დადექი კედლიდან 5 მ-ში და ჩართე ახლო შუქი. შუქის ზედა საზღვარი ფარის ცენტრზე ოდნავ დაბლა უნდა იყოს. ფარის შუშა არ უნდა იყოს გატეხილი ან ძლიერ გაყვითლებული.",
            "At night, stop 5 m from a wall with low beams on. The top edge of the light should be slightly below the headlight center. The lens must not be broken or badly yellowed.",
            "დაარეგულირე ფარები, გააპრიალე ან შეცვალე შუშა.", "Adjust the headlights and polish or replace the lens."),
        Q("li_turn", 5, Major,
            "მოხვევის მაჩვენებლები (წინ, უკან, გვერდზე) ყველა მუშაობს?",
            "Do all turn signals (front, rear and side) work?",
            "მოხვევის მაჩვენებელი არ მუშაობს", "Turn signal not working",
            "ჩართე მარცხენა, შემდეგ მარჯვენა და შემოუარე მანქანას. ძალიან სწრაფი ციმციმი დამწვარ ნათურაზე მიუთითებს.",
            "Switch on left, then right, and walk around the car. Very fast flashing means a bulb is out.",
            "შეცვალე ნათურა.", "Replace the bulb."),
        Q("li_hazard", 5, Major,
            "ავარიული შუქ-სიგნალიზაცია მუშაობს?",
            "Do the hazard lights work?",
            "ავარიული სიგნალიზაცია არ მუშაობს", "Hazard lights not working",
            "დააჭირე სამკუთხედის ღილაკს. ყველა მაჩვენებელი ერთად უნდა ციმციმებდეს.",
            "Press the triangle button. All indicators must flash together.",
            "შეამოწმე ღილაკი და რელე.", "Check the switch and the relay."),
        Q("li_brake", 5, Major,
            "სამივე სტოპ-შუქი ანთია დამუხრუჭებისას?",
            "Do all three brake lights come on when braking?",
            "სტოპ-შუქი არ ანთია", "Brake light not working",
            "ტელეფონი დადე მანქანის უკან და ვიდეო ჩაწერე, როცა პედალს აწვები, ან საღამოს კედლის ანარეკლში ნახე. მესამე, ზედა სტოპიც უნდა ანთოს.",
            "Put your phone behind the car and record a video while pressing the pedal, or check the reflection on a wall in the evening. The third, high brake light must work too.",
            "შეცვალე ნათურა ან პედლის ჩამრთველი.", "Replace the bulb or the pedal switch."),
        Q("li_reverse", 5, Minor,
            "უკანა სვლის შუქი ანთია?",
            "Does the reversing light work?",
            "უკანა სვლის შუქი არ ანთია", "Reversing light not working",
            "ანთება ჩართულია, ფეხი მუხრუჭზე, ჩართე უკანა სვლა და ნახე ანარეკლში ან ვიდეოში.",
            "Ignition on, foot on the brake, select reverse and check a reflection or a video.",
            "შეცვალე ნათურა.", "Replace the bulb."),
        Q("li_plate", 5, Minor,
            "სანომრე ნიშნის შუქი ანთია?",
            "Does the number plate light work?",
            "სანომრე ნიშნის შუქი არ ანთია", "Number plate light not working",
            "ჩართე გაბარიტები და შეხედე უკანა ნომერს.",
            "Switch on the side lights and look at the rear plate.",
            "შეცვალე ნათურა.", "Replace the bulb."),
        Q("li_fog", 5, Minor,
            "ნისლსაწინააღმდეგო ფარები (თუ აქვს) მუშაობს?",
            "Do the fog lights (if fitted) work?",
            "ნისლის ფარი არ მუშაობს", "Fog light not working",
            "თუ მანქანას არ აქვს, აირჩიე „რიგზეა“.",
            "If the car has none, choose \"OK\".",
            "შეცვალე ნათურა.", "Replace the bulb."),

        // 6. Axles, wheels, tires, suspension
        Q("ti_tread", 6, Major,
            "ყველა საბურავის პროტექტორი 1.6 მმ-ზე ღრმაა?",
            "Is the tread depth above 1.6 mm on every tire?",
            "საბურავის პროტექტორი გაცვეთილია", "Tire tread worn out",
            "ღარში არის პატარა ამონაშვერები (TWI). თუ პროტექტორი მათ დონემდეა გაცვეთილი, საბურავი შესაცვლელია. შეამოწმე მთელ სიგანეზე, არათანაბარი ცვეთა სავალ ნაწილზე მიუთითებს.",
            "The grooves contain small bars (TWI). If the tread is worn down to them, the tire must be replaced. Check across the whole width; uneven wear points to alignment or suspension issues.",
            "შეცვალე საბურავები.", "Replace the tires."),
        Q("ti_damage", 6, Dangerous,
            "საბურავებზე გვერდითი გაბერილობა (\"ბაბოჩკა\"), ღრმა ჭრილი ან ბზარი არ არის?",
            "Are the tires free of sidewall bulges, deep cuts and cracks?",
            "საბურავი დაზიანებულია (გაბერილობა ან ჭრილი)", "Tire damaged (bulge or cut)",
            "შეხედე თითოეული საბურავის ორივე გვერდს. გაბერილობა ნიშნავს, რომ კორდი გაწყვეტილია და საბურავი შეიძლება გასკდეს.",
            "Check both sidewalls of each tire. A bulge means broken cords and the tire may burst.",
            "სასწრაფოდ შეცვალე საბურავი.", "Replace the tire immediately."),
        Q("ti_axle", 6, Major,
            "ერთ ღერძზე ერთი ზომის და ერთნაირი ნახატის საბურავებია?",
            "Do tires on the same axle have the same size and tread pattern?",
            "ერთ ღერძზე სხვადასხვა საბურავია", "Mismatched tires on one axle",
            "შეადარე ზომა საბურავის გვერდზე (მაგ. 205/55 R16) და ნახატი. სათადარიგო ვიწრო ბორბალი (\"დოკატკა\") ამისთვის არ ითვლება.",
            "Compare the size on the sidewall (e.g. 205/55 R16) and the pattern. A narrow space-saver spare does not count.",
            "დააყენე ერთნაირი წყვილი ერთ ღერძზე.", "Fit a matching pair on the axle."),
        Q("ti_shocks", 6, Major,
            "ამორტიზატორები არ ჟონავს და რხევა მალე წყდება?",
            "Are the shock absorbers dry and do they stop bouncing quickly?",
            "ამორტიზატორი გაცვეთილია ან ჟონავს", "Shock absorber worn or leaking",
            "დააწექი მანქანის თითო კუთხეს და გაუშვი: 1–2 რხევის შემდეგ უნდა გაჩერდეს. ამორტიზატორზე ზეთიანი ლაქა გაჟონვის ნიშანია.",
            "Push down each corner and let go: it should settle after 1–2 bounces. An oily shock body means it is leaking.",
            "შეცვალე ამორტიზატორები წყვილად.", "Replace the shock absorbers in pairs."),
        Q("ti_knock", 6, Major,
            "ორმოებზე კაკუნი არ ისმის და ბორბლებიდან ზუზუნი არ არის?",
            "No knocks over bumps and no humming from the wheels?",
            "სავალ ნაწილში კაკუნი ან ზუზუნია", "Knocking or humming from the running gear",
            "ნელა გაიარე უსწორმასწორო გზა. კაკუნი ბერკეტების, შარავოების ან სტოიკების ცვეთაზე მიუთითებს, სიჩქარესთან ერთად მზარდი ზუზუნი კი ბორბლის საკისარზე.",
            "Drive slowly over a rough road. Knocks point to worn arms, ball joints or links; humming that rises with speed points to a wheel bearing.",
            "შეამოწმე და შეცვალე გაცვეთილი დეტალები.", "Inspect and replace the worn parts."),
        Q("ti_springs", 6, Major,
            "ზამბარები მთელია და მანქანა ერთ მხარეს არ არის ჩაჯდომილი?",
            "Are the springs intact, with the car level on both sides?",
            "ზამბარა გატეხილია ან მანქანა ჩაჯდომილია", "Broken spring or car sits low on one side",
            "ბრტყელ ადგილას შეადარე ფრთისა და ბორბლის შორის მანძილი ოთხივე კუთხეში. გატეხილი ზამბარა ხშირად ბორბლის შიგნიდან ჩანს.",
            "On level ground, compare the gap between wheel and arch at all four corners. A broken spring is often visible from inside the wheel.",
            "შეცვალე ზამბარები წყვილად.", "Replace the springs in pairs."),

        // 7. Chassis and body
        Q("ch_rust", 7, Major,
            "ზღურბლებზე, იატაკსა და მზიდ ნაწილებზე ღრმა ჟანგი ან ხვრელი არ არის?",
            "Are the sills, floor and load-bearing parts free of deep rust and holes?",
            "მზიდ ნაწილებზე ღრმა ჟანგია", "Deep rust on load-bearing parts",
            "შეხედე ზღურბლებს კარების ქვემოთ, ბორბლის თაღებს და ქვემოდან იატაკს. ზედაპირული ჟანგი პრობლემა არ არის, ხვრელი ან ჩამოფშვნილი ლითონი კი პრობლემაა.",
            "Check the sills below the doors, the wheel arches and the floor from underneath. Surface rust is fine; holes or flaking metal are not.",
            "გაამაგრე ან შეადუღე დაზიანებული ადგილები.", "Repair or weld the affected areas."),
        Q("ch_exhaust", 7, Major,
            "გამონაბოლქვის მილი მყარადაა დამაგრებული და ქვემოთ არ კიდია?",
            "Is the exhaust firmly mounted and not hanging down?",
            "გამონაბოლქვის მილი მოშვებულია", "Exhaust loose or hanging",
            "ცივ მილს ხელით ოდნავ შეარხევ. კაკუნი ან ჩამოკიდება საკიდის ან მილის დაზიანებაა.",
            "Gently shake the cold exhaust by hand. Knocking or sagging means a broken hanger or pipe.",
            "შეცვალე საკიდები ან დაზიანებული მილი.", "Replace the hangers or the damaged pipe."),
        Q("ch_fuel", 7, Dangerous,
            "საწვავის სუნი ან საწვავის გაჟონვა არ არის?",
            "No fuel smell and no fuel leaks?",
            "საწვავი ჟონავს", "Fuel leak",
            "შეხედე ავზს და მილებს მანქანის ქვეშ, უკანა ბორბლებთან, ასევე ძრავის განყოფილებას. საწვავის ლაქა ან მუდმივი სუნი საშიშია.",
            "Look at the tank and lines under the car near the rear wheels and in the engine bay. Fuel drips or a constant smell are dangerous.",
            "არ იმოძრაო და სასწრაფოდ შეაკეთე გაჟონვა.", "Do not drive; repair the leak immediately."),
        Q("ch_body", 7, Major,
            "ბამპერები და ძარის ნაწილები მყარადაა დამაგრებული, ბასრი კიდეების გარეშე?",
            "Are bumpers and body panels secure, without sharp edges?",
            "ბამპერი ან ძარის ნაწილი მოშვებულია", "Bumper or body panel loose",
            "შეარხიე ბამპერები. არ უნდა ირყეოდეს, და გატეხილ ადგილებზე ბასრი კიდე არ უნდა იყოს.",
            "Shake the bumpers. They must not move, and broken areas must not have sharp edges.",
            "დაამაგრე ან შეცვალე დაზიანებული ნაწილი.", "Secure or replace the damaged part."),

        // 8. Safety equipment
        Q("sa_belts", 8, Major,
            "ყველა ღვედი იკვრება, იხსნება და მკვეთრად მოქაჩვისას იჭერს?",
            "Does every seat belt latch, release and lock when pulled sharply?",
            "უსაფრთხოების ღვედი გაუმართავია", "Seat belt faulty",
            "თითოეული ღვედი ბოლომდე გამოსწიე, შეკარი და მკვეთრად მოქაჩე, უნდა გაიჭედოს. ღვედი არ უნდა იყოს გახეული ან ძლიერ ჩაცვეთილი.",
            "Pull each belt out fully, buckle it and tug sharply: it must lock. The webbing must not be torn or badly frayed.",
            "შეცვალე ღვედი ან ბალთა.", "Replace the belt or the buckle."),
        Q("sa_horn", 8, Major,
            "საყვირი მუშაობს?",
            "Does the horn work?",
            "საყვირი არ მუშაობს", "Horn not working",
            "მოკლედ დააჭირე საჭის ცენტრს.",
            "Briefly press the center of the steering wheel.",
            "შეამოწმე საყვირი და მისი დამცველი.", "Check the horn and its fuse."),
        Q("sa_seats", 8, Major,
            "სავარძლები მყარადაა დამაგრებული და არ ირყევა?",
            "Are the seats firmly fixed with no rocking?",
            "სავარძელი მოშვებულია", "Seat loose",
            "დაჯექი და წინ-უკან შეირხიე. სავარძელი ლიანდაგზე უნდა იყოს დაჭერილი.",
            "Sit down and rock back and forth. The seat must be locked on its rails.",
            "დაამაგრე სავარძელი.", "Secure the seat."),
        Q("sa_airbag", 8, Major,
            "აირბეგის (SRS) ნათურა ძრავის ჩართვის შემდეგ ქრება?",
            "Does the airbag (SRS) light go out after the engine starts?",
            "აირბეგის ნათურა ანთია", "Airbag light is on",
            "ანთების ჩართვისას ნათურა რამდენიმე წამით ინთება და უნდა ჩაქრეს. PRO ვერსიაში მოდულების დიაგნოსტიკა აირბეგის შეცდომებს წაიკითხავს.",
            "The light comes on for a few seconds with the ignition and must go out. In PRO, module diagnostics reads the airbag fault codes.",
            "წაიკითხე აირბეგის მოდულის შეცდომები და აღმოფხვერი მიზეზი.", "Read the airbag module codes and fix the cause."),
        Q("sa_ext", 8, Major,
            "მანქანაში არის ცეცხლმაქრი, რომლის ვადა არ გასულა?",
            "Is there a fire extinguisher that is still in date?",
            "ცეცხლმაქრი არ არის ან ვადაგასულია", "Fire extinguisher missing or expired",
            "ვადა წერია ცეცხლმაქრის ეტიკეტზე. თუ მანომეტრი აქვს, ისარი მწვანე ზონაში უნდა იყოს.",
            "The expiry date is on the label. If it has a gauge, the needle must be in the green zone.",
            "იყიდე ახალი ან დაამუხტინე.", "Buy a new one or have it recharged."),
        Q("sa_triangle", 8, Major,
            "მანქანაში არის საავარიო გაჩერების ნიშანი (სამკუთხედი)?",
            "Is there a warning triangle in the car?",
            "საავარიო სამკუთხედი არ არის", "Warning triangle missing",
            "ჩვეულებრივ საბარგულშია, ხშირად საბარგულის კარზე დამაგრებული.",
            "It is usually in the boot, often clipped to the tailgate.",
            "იყიდე სამკუთხედი.", "Buy a warning triangle."),
        Q("sa_firstaid", 8, Major,
            "მანქანაში არის სამედიცინო აფთიაქი?",
            "Is there a first-aid kit in the car?",
            "სამედიცინო აფთიაქი არ არის", "First-aid kit missing",
            "შეამოწმე, რომ სრული იყოს და ვადაგასული საშუალებები არ ჰქონდეს.",
            "Make sure it is complete and nothing in it has expired.",
            "იყიდე აფთიაქი.", "Buy a first-aid kit."),

        // 9. Emissions and leaks
        Q("en_smoke", 9, Major,
            "მკვეთრ გაზზე შავი, ლურჯი ან სქელი თეთრი კვამლი არ გამოდის?",
            "No black, blue or thick white smoke when revving hard?",
            "გამონაბოლქვი ძლიერად ბოლავს", "Heavy exhaust smoke",
            "ძრავა გახურებულია. ვიღაცამ მკვეთრად დააწვეს გაზს, შენ კი მილს შეხედე. შავი კვამლი მდიდარ ნარევზე ან დიზელის ჭვარტლზე მიუთითებს, ლურჯი ზეთის წვაზე, სქელი თეთრი ანტიფრიზის წვაზე.",
            "With a warm engine, have someone rev it hard while you watch the tailpipe. Black means a rich mixture or diesel soot, blue means burning oil, thick white means burning coolant.",
            "დაადგინე მიზეზი ხელოსანთან.", "Have the cause diagnosed."),
        Q("en_cat", 9, Major,
            "კატალიზატორი (დიზელზე DPF ფილტრიც) ადგილზეა და ამოღებული არ არის?",
            "Is the catalytic converter (and DPF on a diesel) still fitted?",
            "კატალიზატორი ან DPF ამოღებულია", "Catalyst or DPF removed",
            "თუ არ იცი, ჰკითხე ხელოსანს ან შეხედე მილს ქვემოდან. ამოღებული კატალიზატორით CO-ს ნორმა, როგორც წესი, ვერ დაკმაყოფილდება.",
            "If unsure, ask a mechanic or look under the car. Without a catalyst the CO limit usually cannot be met.",
            "დააბრუნე კატალიზატორი ან DPF.", "Refit the catalyst or DPF."),
        Q("en_leaks", 9, Major,
            "ძრავიდან, კოლოფიდან ან რადიატორიდან სითხე არ წვეთავს?",
            "No oil, transmission fluid or coolant dripping?",
            "სითხე ჟონავს", "Fluid leak",
            "„მუყაოს ტესტი“: ღამით მანქანის ქვეშ მუყაო დადე და დილით ლაქები ნახე. შავი ან ყავისფერი ზეთია, მწვანე ან ვარდისფერი ანტიფრიზი, წითელი კოლოფის ზეთი.",
            "Cardboard test: leave cardboard under the car overnight and check it in the morning. Black or brown is oil, green or pink is coolant, red is transmission fluid.",
            "შეაკეთე გაჟონვა.", "Repair the leak."),
        Q("en_noise", 9, Major,
            "გამონაბოლქვი ჩვეულებრივზე ხმამაღალი ან ხრიალა არ არის?",
            "Is the exhaust no louder than normal and free of rattles?",
            "გამონაბოლქვი ხმამაღალია", "Exhaust too loud",
            "ხმამაღალი ან აჩქარებისას მზარდი ხმა მაყუჩის ან მილის გახვრეტაზე მიუთითებს.",
            "A loud or rising noise when accelerating points to a holed silencer or pipe.",
            "შეაკეთე ან შეცვალე მაყუჩი.", "Repair or replace the silencer."),
    };

    public static IReadOnlyList<InspectionQuestion> ForCategory(int category) =>
        Questions.Where(q => q.Category == category).ToList();
}

/// <summary>Keeps the user's answers between app launches.</summary>
public static class InspectionAnswerStore
{
    private const string Key = "technical_inspection_answers_v1";

    public static Dictionary<string, InspectionAnswer> Load()
    {
        try
        {
            var json = Preferences.Default.Get(Key, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return new();
            var raw = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new();
            return raw.ToDictionary(x => x.Key, x => (InspectionAnswer)x.Value);
        }
        catch
        {
            return new();
        }
    }

    public static void Save(Dictionary<string, InspectionAnswer> answers)
    {
        try
        {
            var raw = answers.Where(x => x.Value != InspectionAnswer.None).ToDictionary(x => x.Key, x => (int)x.Value);
            Preferences.Default.Set(Key, JsonSerializer.Serialize(raw));
        }
        catch
        {
            // Answers stay in memory for this session.
        }
    }

    public static void Clear()
    {
        try { Preferences.Default.Remove(Key); } catch { }
    }
}
