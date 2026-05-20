namespace ChatCRM.Infrastructure.Services
{
    /// <summary>
    /// Best-effort country lookup based on the leading digits of an E.164-style phone number.
    /// Not perfect (e.g. NANP region 1 → US/Canada/Caribbean all share prefix 1), but good enough
    /// to populate the Country column without forcing manual input.
    /// </summary>
    public static class PhoneCountryDetector
    {
        // Sorted longest-first inside Detect by iterating prefix lengths descending.
        private static readonly Dictionary<string, string> Codes = new()
        {
            ["1"]   = "United States",
            ["7"]   = "Russia",
            ["20"]  = "Egypt",
            ["27"]  = "South Africa",
            ["30"]  = "Greece",
            ["31"]  = "Netherlands",
            ["32"]  = "Belgium",
            ["33"]  = "France",
            ["34"]  = "Spain",
            ["36"]  = "Hungary",
            ["39"]  = "Italy",
            ["40"]  = "Romania",
            ["41"]  = "Switzerland",
            ["43"]  = "Austria",
            ["44"]  = "United Kingdom",
            ["45"]  = "Denmark",
            ["46"]  = "Sweden",
            ["47"]  = "Norway",
            ["48"]  = "Poland",
            ["49"]  = "Germany",
            ["52"]  = "Mexico",
            ["54"]  = "Argentina",
            ["55"]  = "Brazil",
            ["57"]  = "Colombia",
            ["58"]  = "Venezuela",
            ["60"]  = "Malaysia",
            ["61"]  = "Australia",
            ["62"]  = "Indonesia",
            ["63"]  = "Philippines",
            ["64"]  = "New Zealand",
            ["65"]  = "Singapore",
            ["66"]  = "Thailand",
            ["81"]  = "Japan",
            ["82"]  = "South Korea",
            ["84"]  = "Vietnam",
            ["86"]  = "China",
            ["90"]  = "Turkey",
            ["91"]  = "India",
            ["92"]  = "Pakistan",
            ["93"]  = "Afghanistan",
            ["94"]  = "Sri Lanka",
            ["95"]  = "Myanmar",
            ["98"]  = "Iran",
            ["212"] = "Morocco",
            ["213"] = "Algeria",
            ["216"] = "Tunisia",
            ["218"] = "Libya",
            ["220"] = "Gambia",
            ["221"] = "Senegal",
            ["233"] = "Ghana",
            ["234"] = "Nigeria",
            ["249"] = "Sudan",
            ["251"] = "Ethiopia",
            ["254"] = "Kenya",
            ["255"] = "Tanzania",
            ["256"] = "Uganda",
            ["351"] = "Portugal",
            ["353"] = "Ireland",
            ["356"] = "Malta",
            ["358"] = "Finland",
            ["370"] = "Lithuania",
            ["371"] = "Latvia",
            ["372"] = "Estonia",
            ["373"] = "Moldova",
            ["374"] = "Armenia",
            ["375"] = "Belarus",
            ["380"] = "Ukraine",
            ["420"] = "Czech Republic",
            ["421"] = "Slovakia",
            ["852"] = "Hong Kong",
            ["853"] = "Macau",
            ["855"] = "Cambodia",
            ["880"] = "Bangladesh",
            ["886"] = "Taiwan",
            ["960"] = "Maldives",
            ["961"] = "Lebanon",
            ["962"] = "Jordan",
            ["963"] = "Syria",
            ["964"] = "Iraq",
            ["965"] = "Kuwait",
            ["966"] = "Saudi Arabia",
            ["967"] = "Yemen",
            ["968"] = "Oman",
            ["970"] = "Palestine",
            ["971"] = "United Arab Emirates",
            ["972"] = "Israel",
            ["973"] = "Bahrain",
            ["974"] = "Qatar"
        };

        public static string? Detect(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;

            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return null;

            for (int len = 4; len >= 1; len--)
            {
                if (digits.Length < len) continue;
                var prefix = digits[..len];
                if (Codes.TryGetValue(prefix, out var country)) return country;
            }
            return null;
        }
    }
}
