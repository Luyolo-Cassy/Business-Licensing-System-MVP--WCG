namespace BusinessLicensing_Practice.Services
{
    public static class LicenceApplicationCatalog
    {
        private static readonly IReadOnlyList<DocumentRequirement> StandardDocuments =
        [
            Document("Certificate of Incorporation", "Proof that the business is legally registered."),
            Document("Proof of Address", "A recent document confirming the business address."),
            Document("Tax Clearance Certificate", "Current proof of the business's tax compliance status."),
            Document("Owner ID Document", "A clear copy of the owner's identification document.")
        ];

        private static readonly IReadOnlyList<DocumentRequirement> FoodDocuments =
        [
            .. StandardDocuments,
            Document("Certificate of Acceptability Application", "Proof of application for a Certificate of Acceptability.")
        ];

        private static readonly IReadOnlyList<DocumentRequirement> EntertainmentDocuments =
        [
            .. StandardDocuments,
            Document("Proof of Soundproofing", "Evidence of appropriate soundproofing or noise-control measures.")
        ];

        public static readonly IReadOnlyList<LicenceDefinition> Licences =
        [
            new("sale-of-meals", "Sale of Meals Licence", "Prepare and sell meals for immediate consumption.", "bi-shop",
            [
                Text("foodDescription", "Description of food or meals sold"),
                Choice("foodHandling", "How will food be handled?", "Pre-packed", "Packed on premises", "Processed", "Prepared on premises"),
                Text("tradingInformation", "Trading arrangements and service method")
            ], FoodDocuments),
            new("sale-of-perishable-foodstuffs", "Sale of Perishable Foodstuffs Licence", "Sell food requiring refrigeration or controlled storage.", "bi-basket",
            [
                Text("foodDescription", "Description of perishable foodstuffs sold"),
                Choice("foodHandling", "How will food be handled?", "Pre-packed", "Packed on premises", "Processed", "Prepared on premises"),
                Text("coldStorage", "Cold-storage and temperature-control arrangements")
            ], FoodDocuments),
            new("health-facility", "Health Facility Licence", "Operate facilities such as saunas, massage establishments or Turkish baths.", "bi-heart-pulse",
            [
                YesNo("firstAidKit", "Is a first-aid kit available?"),
                YesNo("firstAidKnowledge", "Is a person with first-aid knowledge present?"),
                Text("sterilisation", "Describe equipment sterilisation procedures"),
                Text("wastewaterDisposal", "Describe wastewater disposal arrangements")
            ], StandardDocuments),
            new("entertainment-venue", "Entertainment Venue Licence", "Operate venues such as cinemas, theatres or nightclubs.", "bi-music-note-beamed",
            [
                Choice("entertainmentType", "Type of entertainment", "Cinema", "Theatre", "Disco / club", "Karaoke", "Live performance", "Other"),
                Text("machinesAndTables", "Machines, games or pool tables and quantities"),
                YesNo("liveArtists", "Will live artists or DJs be used?"),
                Text("performanceTimes", "Performance and entertainment times"),
                Text("noiseControl", "Noise-control measures"),
                Text("toiletFacilities", "Toilet facilities"),
                YesNo("smokingArea", "Is there a designated smoking area?")
            ], EntertainmentDocuments),
            new("gaming-amusement", "Gaming / Amusement Licence", "Operate gaming or amusement activities such as billiards or slot machines.", "bi-controller",
            [
                Text("activityType", "Type of gaming or amusement activity"),
                Text("machineCount", "Number and type of machines, games or tables"),
                Text("supervision", "Supervision and access-control arrangements"),
                Text("operatingTimes", "Operating times")
            ], EntertainmentDocuments),
            new("adult-premises-escort-services", "Adult Premises / Escort Services Licence", "Operate regulated adult entertainment or escort service businesses.", "bi-shield-lock",
            [
                Choice("premisesType", "Type of regulated activity", "Adult premises", "Escort service", "Both"),
                Text("managementControls", "Management and access-control measures"),
                Text("operatingTimes", "Operating times"),
                Text("safetyMeasures", "Safety and security measures")
            ], StandardDocuments),
            new("hawker-street-trading", "Hawker / Street Trading Licence", "Operate a street trading or mobile food vending business.", "bi-shop-window",
            [
                Text("goodsSold", "Meals or perishable foodstuffs to be sold"),
                Text("tradingLocation", "Trading location or route"),
                Text("foodStorage", "Food storage and transport arrangements"),
                Text("wasteDisposal", "Waste and wastewater disposal arrangements"),
                Text("tradingTimes", "Trading days and hours")
            ], FoodDocuments)
        ];

        public static readonly IReadOnlyList<string> Municipalities =
        [
            "Beaufort West Municipality", "Bergrivier Municipality", "Bitou Municipality",
            "Breede Valley Municipality", "Cape Agulhas Municipality", "Cederberg Municipality",
            "City of Cape Town", "Drakenstein Municipality", "George Municipality",
            "Hessequa Municipality", "Kannaland Municipality", "Knysna Municipality",
            "Laingsburg Municipality", "Langeberg Municipality", "Matzikama Municipality",
            "Mossel Bay Municipality", "Oudtshoorn Municipality", "Overstrand Municipality",
            "Prince Albert Municipality", "Saldanha Bay Municipality", "Stellenbosch Municipality",
            "Swartland Municipality", "Swellendam Municipality", "Theewaterskloof Municipality",
            "Witzenberg Municipality"
        ];

        public static LicenceDefinition? Find(string licenceName) =>
            Licences.FirstOrDefault(licence => licence.Name == licenceName);

        private static LicenceQuestion Text(string key, string label) => new(key, label, QuestionType.Text, true, []);
        private static LicenceQuestion YesNo(string key, string label) => new(key, label, QuestionType.YesNo, true, ["Yes", "No"]);
        private static LicenceQuestion Choice(string key, string label, params string[] options) => new(key, label, QuestionType.Choice, true, options);
        private static DocumentRequirement Document(string type, string description) => new(type, description, true);
    }

    public sealed record LicenceDefinition(
        string Id,
        string Name,
        string Description,
        string IconClass,
        IReadOnlyList<LicenceQuestion> Questions,
        IReadOnlyList<DocumentRequirement> Documents);

    public sealed record LicenceQuestion(
        string Key,
        string Label,
        QuestionType Type,
        bool Required,
        IReadOnlyList<string> Options);

    public sealed record DocumentRequirement(string DocumentType, string Description, bool Required);

    public enum QuestionType
    {
        Text,
        YesNo,
        Choice
    }
}
