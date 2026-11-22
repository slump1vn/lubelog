using CarCareTracker.Models;

namespace CarCareTracker.Logic
{
    /// <summary>
    /// Reference data for the food label analysis. Every threshold and every remark comes from a published source,
    /// named next to it. When anything here changes, raise Version: each saved scan records the version it used.
    /// </summary>
    public static class FoodReference
    {
        public const string Version = "2026-11-20";

        public record Source(string Name, string Url);
        public static class Sources
        {
            public static readonly Source FsaFrontOfPack = new("UK FSA / DHSC, Front of pack nutrition labelling guidance", "https://assets.publishing.service.gov.uk/media/5a80cd03ed915d74e33fc7c5/FoP_Nutrition_labelling_UK_guidance.pdf");
            public static readonly Source WhoFats2023 = new("WHO, Saturated and trans fatty acid intake guideline (2023)", "https://www.who.int/news/item//17-07-2023-who-updates-guidelines-on-fats-and-carbohydrates");
            public static readonly Source WhoSugars2015 = new("WHO, Guideline: sugars intake for adults and children (2015)", "https://www.who.int/publications/i/item/9789241549028");
            public static readonly Source WhoSodium2012 = new("WHO, Guideline: sodium intake for adults and children (2012)", "https://www.who.int/publications/i/item/9789241504836");
            public static readonly Source WhoSweeteners2023 = new("WHO, Use of non-sugar sweeteners guideline (2023)", "https://www.who.int/publications/i/item/9789240073616");
            public static readonly Source WhoReplace = new("WHO REPLACE trans fat", "https://www.who.int/teams/nutrition-and-food-safety/replace-trans-fat");
            public static readonly Source EuAdditives = new("EU Regulation 1333/2008 on food additives", "https://eur-lex.europa.eu/eli/reg/2008/1333/oj");
            public static readonly Source EuFoodInformation = new("EU Regulation 1169/2011 on food information", "https://eur-lex.europa.eu/eli/reg/2011/1169/oj");
            public static readonly Source EuTitaniumDioxide = new("EU Regulation 2022/63 (E171)", "https://eur-lex.europa.eu/eli/reg/2022/63/oj");
            public static readonly Source IarcAspartame = new("IARC/JECFA, Aspartame assessment (2023)", "https://www.iarc.who.int/news-events/aspartame-hazard-and-risk-assessment-results-released/");
            public static readonly Source IarcList = new("IARC, Agents classified by the IARC Monographs", "https://monographs.iarc.who.int/agents-classified-by-the-iarc/");
            public static readonly Source EfsaNitrites = new("EFSA, Re-evaluation of nitrites (2017)", "https://www.efsa.europa.eu/en/efsajournal/pub/4786");
            public static readonly Source EfsaGlutamate = new("EFSA, Re-evaluation of glutamates (2017)", "https://www.efsa.europa.eu/en/efsajournal/pub/4910");
            public static readonly Source EfsaPhosphates = new("EFSA, Re-evaluation of phosphates (2019)", "https://www.efsa.europa.eu/en/efsajournal/pub/5674");
            public static readonly Source EfsaCaffeine = new("EFSA, Scientific opinion on the safety of caffeine (2015)", "https://www.efsa.europa.eu/en/efsajournal/pub/4102");
            public static readonly Source FdaBenzene = new("US FDA, Benzene in soft drinks", "https://www.fda.gov/food/environmental-contaminants-food/data-benzene-soft-drinks-and-other-beverages");
            public static readonly Source Codex = new("Codex CXG 36-1989, Class names and International Numbering System (INS)", "https://www.fao.org/fao-who-codexalimentarius/codex-texts/guidelines/en/");
        }

        /// <summary>UK FSA front-of-pack thresholds per 100 g (food) or 100 ml (drink): green at or below Low, red above High.</summary>
        public record LightThreshold(decimal Low, decimal High);
        public static readonly IReadOnlyDictionary<string, LightThreshold> FoodThresholds = new Dictionary<string, LightThreshold>
        {
            ["fat"] = new(3m, 17.5m),
            ["saturates"] = new(1.5m, 5m),
            ["sugars"] = new(5m, 22.5m),
            ["salt"] = new(0.3m, 1.5m)
        };
        public static readonly IReadOnlyDictionary<string, LightThreshold> DrinkThresholds = new Dictionary<string, LightThreshold>
        {
            ["fat"] = new(1.5m, 8.75m),
            ["saturates"] = new(0.75m, 2.5m),
            ["sugars"] = new(2.5m, 11.25m),
            ["salt"] = new(0.3m, 0.75m)
        };
        /// <summary>
        /// FSA rule for foods sold in portions over 100 g: red when one portion gives more than 30% of the adult
        /// reference intake (EU 1169/2011 Annex XIII: fat 70 g, saturates 20 g, sugars 90 g, salt 6 g).
        /// </summary>
        public const decimal PortionRuleMinimum = 100m;
        public static readonly IReadOnlyDictionary<string, decimal> PortionRedAbove = new Dictionary<string, decimal>
        {
            ["fat"] = 21m,
            ["saturates"] = 6m,
            ["sugars"] = 27m,
            ["salt"] = 1.8m
        };
        /// <summary>
        /// WHO daily upper limits for an adult eating 2000 kcal: total fat at most 30% of energy, saturated fat under
        /// 10%, trans fat under 1%, free sugars under 10% (50 g), salt under 5 g. Label "sugars" also counts natural
        /// sugars, so the sugars share is an upper estimate of free sugars.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, decimal> DailyLimits = new Dictionary<string, decimal>
        {
            ["fat"] = 67m,
            ["saturates"] = 22m,
            ["sugars"] = 50m,
            ["salt"] = 5m,
            ["trans"] = 2.2m
        };
        public static readonly IReadOnlyDictionary<string, Source> DailyLimitSources = new Dictionary<string, Source>
        {
            ["fat"] = Sources.WhoFats2023,
            ["saturates"] = Sources.WhoFats2023,
            ["sugars"] = Sources.WhoSugars2015,
            ["salt"] = Sources.WhoSodium2012,
            ["trans"] = Sources.WhoFats2023
        };

        /// <summary>Functional classes (Codex CXG 36-1989) with their Vietnamese names.</summary>
        public static readonly IReadOnlyDictionary<string, (string Vi, string En)> Classes = new Dictionary<string, (string, string)>
        {
            ["colour"] = ("Phẩm màu", "Colour"),
            ["preservative"] = ("Chất bảo quản", "Preservative"),
            ["antioxidant"] = ("Chất chống oxy hóa", "Antioxidant"),
            ["sweetener"] = ("Chất tạo ngọt", "Sweetener"),
            ["flavour_enhancer"] = ("Chất điều vị", "Flavour enhancer"),
            ["emulsifier"] = ("Chất nhũ hóa", "Emulsifier"),
            ["stabiliser"] = ("Chất ổn định", "Stabiliser"),
            ["thickener"] = ("Chất làm dày", "Thickener"),
            ["acidity_regulator"] = ("Chất điều chỉnh độ acid", "Acidity regulator"),
            ["raising_agent"] = ("Chất tạo xốp", "Raising agent"),
            ["anticaking"] = ("Chất chống đông vón", "Anti-caking agent"),
            ["glazing"] = ("Chất làm bóng", "Glazing agent"),
            ["humectant"] = ("Chất giữ ẩm", "Humectant"),
            ["flour_treatment"] = ("Chất xử lý bột", "Flour treatment agent"),
            ["enzyme"] = ("Enzyme", "Enzyme"),
            ["modified_starch"] = ("Tinh bột biến tính", "Modified starch")
        };

        private static FoodNote Note(FoodNoteLevel level, string vi, string en, Source source) => new FoodNote { Level = level, TextVi = vi, TextEn = en, SourceName = source.Name, SourceUrl = source.Url };

        private static readonly FoodNote SouthamptonColour = Note(FoodNoteLevel.Caution,
            "EU bắt buộc ghi cảnh báo: \"có thể ảnh hưởng xấu đến hoạt động và sự chú ý của trẻ em\".",
            "The EU requires the warning \"may have an adverse effect on activity and attention in children\".",
            Sources.EuAdditives);
        private static readonly FoodNote SulphiteAllergen = Note(FoodNoteLevel.Caution,
            "Sulfit là chất gây dị ứng phải khai báo khi trên 10 mg/kg; người hen suyễn có thể nhạy cảm.",
            "Sulphites are a declarable allergen above 10 mg/kg; people with asthma can be sensitive.",
            Sources.EuFoodInformation);
        private static readonly FoodNote NitriteNote = Note(FoodNoteLevel.Caution,
            "Hay dùng trong thịt chế biến. IARC xếp thịt chế biến vào nhóm 1 (gây ung thư đại trực tràng); EFSA (2017) đặt mức ăn vào chấp nhận được cho nitrit.",
            "Common in processed meat. IARC classifies processed meat as Group 1 (colorectal cancer); EFSA (2017) set an acceptable daily intake for nitrites.",
            Sources.EfsaNitrites);
        private static readonly FoodNote PhosphateNote = Note(FoodNoteLevel.Info,
            "EFSA (2019) đặt mức ăn vào chấp nhận được chung cho phosphat là 40 mg phospho/kg cân nặng/ngày; người bệnh thận nên hỏi bác sĩ.",
            "EFSA (2019) set a group acceptable daily intake of 40 mg phosphorus/kg body weight for phosphates; people with kidney disease should ask their doctor.",
            Sources.EfsaPhosphates);
        private static readonly FoodNote PolyolNote = Note(FoodNoteLevel.Info,
            "Đường rượu (polyol): EU bắt buộc ghi \"ăn nhiều có thể gây nhuận tràng\" khi chiếm trên 10% sản phẩm.",
            "Polyol: the EU requires \"excessive consumption may produce laxative effects\" when it exceeds 10% of the product.",
            Sources.EuFoodInformation);
        private static readonly FoodNote GlutamateNote = Note(FoodNoteLevel.Info,
            "Được phép dùng. EFSA (2017) đặt mức ăn vào chấp nhận được chung cho glutamat là 30 mg/kg cân nặng/ngày.",
            "Permitted. EFSA (2017) set a group acceptable daily intake of 30 mg/kg body weight for glutamates.",
            Sources.EfsaGlutamate);

        public record Additive(string Code, string Vi, string En, string Class, params FoodNote[] Notes);
        /// <summary>Common additives by INS number (E number without the E). Codes not listed are shown as unknown, not as bad.</summary>
        public static readonly IReadOnlyDictionary<string, Additive> Additives = new List<Additive>
        {
            new("100", "Curcumin (nghệ)", "Curcumin", "colour"),
            new("101", "Riboflavin (vitamin B2)", "Riboflavin", "colour"),
            new("102", "Tartrazine", "Tartrazine", "colour", SouthamptonColour),
            new("104", "Quinoline yellow", "Quinoline yellow", "colour", SouthamptonColour),
            new("110", "Sunset yellow FCF", "Sunset yellow FCF", "colour", SouthamptonColour),
            new("120", "Carmine (từ côn trùng)", "Carmine", "colour"),
            new("122", "Carmoisine (azorubine)", "Carmoisine", "colour", SouthamptonColour),
            new("124", "Ponceau 4R", "Ponceau 4R", "colour", SouthamptonColour),
            new("129", "Allura red AC", "Allura red AC", "colour", SouthamptonColour),
            new("133", "Brilliant blue FCF", "Brilliant blue FCF", "colour"),
            new("141", "Phức đồng chlorophyll", "Copper chlorophyll complexes", "colour"),
            new("150a", "Caramen nhóm I", "Plain caramel", "colour"),
            new("150b", "Caramen nhóm II", "Caustic sulphite caramel", "colour"),
            new("150c", "Caramen nhóm III (amoni)", "Ammonia caramel", "colour"),
            new("150d", "Caramen nhóm IV (amoni sulfit)", "Sulphite ammonia caramel", "colour",
                Note(FoodNoteLevel.Info, "Có thể chứa vết 4-MEI, chất IARC xếp nhóm 2B (có thể gây ung thư, bằng chứng hạn chế).", "May contain traces of 4-MEI, which IARC classifies as Group 2B (possibly carcinogenic, limited evidence).", Sources.IarcList)),
            new("160a", "Beta-caroten", "Beta-carotene", "colour"),
            new("160b", "Điều màu (annatto)", "Annatto", "colour"),
            new("160c", "Chiết xuất ớt (paprika)", "Paprika extract", "colour"),
            new("162", "Đỏ củ dền", "Beetroot red", "colour"),
            new("163", "Anthocyanin", "Anthocyanins", "colour"),
            new("170", "Calci carbonat", "Calcium carbonate", "colour"),
            new("171", "Titan dioxid", "Titanium dioxide", "colour",
                Note(FoodNoteLevel.Caution, "Bị cấm làm phụ gia thực phẩm ở EU từ 2022 vì EFSA không loại trừ được nguy cơ gây độc gen.", "Banned as a food additive in the EU since 2022 because EFSA could not rule out genotoxicity.", Sources.EuTitaniumDioxide)),
            new("200", "Acid sorbic", "Sorbic acid", "preservative"),
            new("202", "Kali sorbat", "Potassium sorbate", "preservative"),
            new("210", "Acid benzoic", "Benzoic acid", "preservative"),
            new("211", "Natri benzoat", "Sodium benzoate", "preservative"),
            new("212", "Kali benzoat", "Potassium benzoate", "preservative"),
            new("220", "Lưu huỳnh dioxid", "Sulphur dioxide", "preservative", SulphiteAllergen),
            new("221", "Natri sulfit", "Sodium sulphite", "preservative", SulphiteAllergen),
            new("222", "Natri bisulfit", "Sodium bisulphite", "preservative", SulphiteAllergen),
            new("223", "Natri metabisulfit", "Sodium metabisulphite", "preservative", SulphiteAllergen),
            new("224", "Kali metabisulfit", "Potassium metabisulphite", "preservative", SulphiteAllergen),
            new("228", "Kali bisulfit", "Potassium bisulphite", "preservative", SulphiteAllergen),
            new("234", "Nisin", "Nisin", "preservative"),
            new("249", "Kali nitrit", "Potassium nitrite", "preservative", NitriteNote),
            new("250", "Natri nitrit", "Sodium nitrite", "preservative", NitriteNote),
            new("251", "Natri nitrat", "Sodium nitrate", "preservative", NitriteNote),
            new("252", "Kali nitrat", "Potassium nitrate", "preservative", NitriteNote),
            new("260", "Acid acetic", "Acetic acid", "acidity_regulator"),
            new("270", "Acid lactic", "Lactic acid", "acidity_regulator"),
            new("281", "Natri propionat", "Sodium propionate", "preservative"),
            new("282", "Calci propionat", "Calcium propionate", "preservative"),
            new("296", "Acid malic", "Malic acid", "acidity_regulator"),
            new("300", "Acid ascorbic (vitamin C)", "Ascorbic acid", "antioxidant"),
            new("301", "Natri ascorbat", "Sodium ascorbate", "antioxidant"),
            new("304", "Ascorbyl palmitat", "Ascorbyl palmitate", "antioxidant"),
            new("306", "Tocopherol (vitamin E)", "Tocopherols", "antioxidant"),
            new("307", "Alpha-tocopherol", "Alpha-tocopherol", "antioxidant"),
            new("316", "Natri erythorbat", "Sodium erythorbate", "antioxidant"),
            new("319", "TBHQ", "TBHQ", "antioxidant"),
            new("320", "BHA", "Butylated hydroxyanisole (BHA)", "antioxidant",
                Note(FoodNoteLevel.Caution, "IARC xếp nhóm 2B (có thể gây ung thư, bằng chứng hạn chế).", "IARC Group 2B (possibly carcinogenic, limited evidence).", Sources.IarcList)),
            new("321", "BHT", "Butylated hydroxytoluene (BHT)", "antioxidant"),
            new("322", "Lecithin", "Lecithins", "emulsifier",
                Note(FoodNoteLevel.Info, "Thường làm từ đậu nành; người dị ứng đậu nành nên xem nguồn gốc.", "Often made from soy; people allergic to soy should check the source.", Sources.EuFoodInformation)),
            new("325", "Natri lactat", "Sodium lactate", "acidity_regulator"),
            new("327", "Calci lactat", "Calcium lactate", "acidity_regulator"),
            new("330", "Acid citric", "Citric acid", "acidity_regulator"),
            new("331", "Natri citrat", "Sodium citrates", "acidity_regulator"),
            new("332", "Kali citrat", "Potassium citrates", "acidity_regulator"),
            new("333", "Calci citrat", "Calcium citrates", "acidity_regulator"),
            new("334", "Acid tartric", "Tartaric acid", "acidity_regulator"),
            new("338", "Acid phosphoric", "Phosphoric acid", "acidity_regulator", PhosphateNote),
            new("339", "Natri phosphat", "Sodium phosphates", "acidity_regulator", PhosphateNote),
            new("340", "Kali phosphat", "Potassium phosphates", "acidity_regulator", PhosphateNote),
            new("341", "Calci phosphat", "Calcium phosphates", "acidity_regulator", PhosphateNote),
            new("401", "Natri alginat", "Sodium alginate", "thickener"),
            new("406", "Thạch (agar)", "Agar", "thickener"),
            new("407", "Carrageenan", "Carrageenan", "thickener"),
            new("410", "Gôm đậu carob", "Locust bean gum", "thickener"),
            new("412", "Gôm guar", "Guar gum", "thickener"),
            new("414", "Gôm arabic", "Gum arabic", "thickener"),
            new("415", "Gôm xanthan", "Xanthan gum", "thickener"),
            new("418", "Gôm gellan", "Gellan gum", "thickener"),
            new("420", "Sorbitol", "Sorbitol", "sweetener", PolyolNote),
            new("421", "Mannitol", "Mannitol", "sweetener", PolyolNote),
            new("422", "Glycerol", "Glycerol", "humectant"),
            new("440", "Pectin", "Pectins", "thickener"),
            new("450", "Diphosphat", "Diphosphates", "raising_agent", PhosphateNote),
            new("451", "Triphosphat", "Triphosphates", "stabiliser", PhosphateNote),
            new("452", "Polyphosphat", "Polyphosphates", "stabiliser", PhosphateNote),
            new("460", "Cellulose", "Cellulose", "anticaking"),
            new("461", "Methyl cellulose", "Methyl cellulose", "thickener"),
            new("466", "Natri carboxymethyl cellulose (CMC)", "Carboxymethyl cellulose", "thickener"),
            new("471", "Mono- và diglycerid của acid béo", "Mono- and diglycerides of fatty acids", "emulsifier"),
            new("472e", "Este DATEM", "DATEM", "emulsifier"),
            new("475", "Este polyglycerol", "Polyglycerol esters of fatty acids", "emulsifier"),
            new("476", "PGPR", "Polyglycerol polyricinoleate", "emulsifier"),
            new("481", "Natri stearoyl lactylat", "Sodium stearoyl lactylate", "emulsifier"),
            new("500", "Natri carbonat (baking soda)", "Sodium carbonates", "raising_agent"),
            new("501", "Kali carbonat", "Potassium carbonates", "acidity_regulator"),
            new("503", "Amoni carbonat", "Ammonium carbonates", "raising_agent"),
            new("504", "Magnesi carbonat", "Magnesium carbonates", "anticaking"),
            new("508", "Kali clorid", "Potassium chloride", "flavour_enhancer"),
            new("509", "Calci clorid", "Calcium chloride", "stabiliser"),
            new("551", "Silic dioxid", "Silicon dioxide", "anticaking"),
            new("621", "Mononatri glutamat (bột ngọt, mì chính)", "Monosodium glutamate", "flavour_enhancer", GlutamateNote),
            new("627", "Dinatri guanylat", "Disodium guanylate", "flavour_enhancer"),
            new("631", "Dinatri inosinat", "Disodium inosinate", "flavour_enhancer"),
            new("635", "Dinatri 5'-ribonucleotid", "Disodium 5'-ribonucleotides", "flavour_enhancer"),
            new("901", "Sáp ong", "Beeswax", "glazing"),
            new("903", "Sáp carnauba", "Carnauba wax", "glazing"),
            new("904", "Shellac", "Shellac", "glazing"),
            new("920", "L-cystein", "L-cysteine", "flour_treatment"),
            new("950", "Acesulfam kali", "Acesulfame K", "sweetener"),
            new("951", "Aspartam", "Aspartame", "sweetener",
                Note(FoodNoteLevel.Caution, "IARC (2023) xếp nhóm 2B (bằng chứng hạn chế); JECFA giữ mức ăn vào chấp nhận được 40 mg/kg cân nặng/ngày.", "IARC (2023) Group 2B (limited evidence); JECFA kept the acceptable daily intake of 40 mg/kg body weight.", Sources.IarcAspartame),
                Note(FoodNoteLevel.Caution, "Chứa phenylalanin: người bị phenylceton niệu (PKU) phải tránh.", "Contains a source of phenylalanine: people with phenylketonuria (PKU) must avoid it.", Sources.EuAdditives)),
            new("952", "Cyclamat", "Cyclamates", "sweetener"),
            new("953", "Isomalt", "Isomalt", "sweetener", PolyolNote),
            new("954", "Saccharin", "Saccharin", "sweetener"),
            new("955", "Sucralose", "Sucralose", "sweetener"),
            new("960", "Steviol glycosid (cỏ ngọt)", "Steviol glycosides", "sweetener"),
            new("965", "Maltitol", "Maltitol", "sweetener", PolyolNote),
            new("967", "Xylitol", "Xylitol", "sweetener", PolyolNote),
            new("968", "Erythritol", "Erythritol", "sweetener", PolyolNote),
            new("1100", "Amylase", "Amylase", "enzyme"),
            new("1404", "Tinh bột oxy hóa", "Oxidised starch", "modified_starch"),
            new("1412", "Distarch phosphat", "Distarch phosphate", "modified_starch"),
            new("1414", "Distarch phosphat acetyl hóa", "Acetylated distarch phosphate", "modified_starch"),
            new("1420", "Tinh bột acetyl hóa", "Acetylated starch", "modified_starch"),
            new("1422", "Distarch adipat acetyl hóa", "Acetylated distarch adipate", "modified_starch"),
            new("1442", "Hydroxypropyl distarch phosphat", "Hydroxypropyl distarch phosphate", "modified_starch"),
            new("1450", "Tinh bột natri octenyl succinat", "Starch sodium octenyl succinate", "modified_starch")
        }.ToDictionary(x => x.Code);

        /// <summary>Additives often written by name instead of number, Vietnamese and English (lower case, with diacritics).</summary>
        public static readonly IReadOnlyList<(string Word, string Code)> AdditiveNames = new List<(string, string)>
        {
            ("bột ngọt", "621"), ("mì chính", "621"), ("monosodium glutamate", "621"), ("mononatri glutamat", "621"), ("msg", "621"),
            ("aspartame", "951"), ("aspartam", "951"),
            ("acesulfame", "950"), ("acesulfam", "950"),
            ("sucralose", "955"), ("saccharin", "954"), ("cyclamate", "952"), ("cyclamat", "952"),
            ("steviol", "960"), ("stevia", "960"), ("cỏ ngọt", "960"),
            ("sorbitol", "420"), ("maltitol", "965"), ("xylitol", "967"), ("erythritol", "968"), ("isomalt", "953"),
            ("sodium benzoate", "211"), ("natri benzoat", "211"),
            ("potassium sorbate", "202"), ("kali sorbat", "202"),
            ("sodium nitrite", "250"), ("natri nitrit", "250"), ("sodium nitrate", "251"), ("natri nitrat", "251"),
            ("titanium dioxide", "171"), ("titan dioxid", "171"), ("titan dioxit", "171"),
            ("tartrazine", "102"), ("sunset yellow", "110"), ("ponceau 4r", "124"), ("allura red", "129"), ("carmoisine", "122"), ("quinoline yellow", "104"),
            ("carrageenan", "407"), ("xanthan", "415"),
            ("sodium metabisulfite", "223"), ("sodium metabisulphite", "223"), ("natri metabisulfit", "223"),
            ("bha", "320"), ("tbhq", "319"), ("bht", "321")
        };

        public record AllergenGroup(string Key, string Vi, string En, params string[] Words);
        /// <summary>The 14 allergen groups of EU 1169/2011 Annex II (which include the Codex list), with label words in Vietnamese and English.</summary>
        public static readonly IReadOnlyList<AllergenGroup> Allergens = new List<AllergenGroup>
        {
            new("gluten", "Ngũ cốc chứa gluten", "Cereals containing gluten", "gluten", "lúa mì", "bột mì", "lúa mạch", "lúa mạch đen", "yến mạch", "kiều mạch", "wheat", "barley", "rye", "oat", "oats", "spelt", "malt", "mạch nha"),
            new("crustaceans", "Giáp xác", "Crustaceans", "tôm", "cua", "ghẹ", "tôm hùm", "shrimp", "prawn", "crab", "lobster", "crustacean", "crustaceans"),
            new("eggs", "Trứng", "Eggs", "trứng", "lòng đỏ", "lòng trắng", "egg", "eggs", "albumin"),
            new("fish", "Cá", "Fish", "cá", "nước mắm", "fish", "anchovy", "tuna", "salmon"),
            new("peanuts", "Đậu phộng (lạc)", "Peanuts", "đậu phộng", "lạc", "peanut", "peanuts", "groundnut"),
            new("soy", "Đậu nành", "Soybeans", "đậu nành", "đỗ tương", "đậu tương", "soy", "soya", "soybean", "soybeans"),
            new("milk", "Sữa", "Milk", "sữa", "lactose", "whey", "váng sữa", "casein", "caseinat", "bơ sữa", "kem sữa", "phô mai", "milk", "butter", "cream", "cheese", "milk powder"),
            new("nuts", "Các loại hạt cây", "Tree nuts", "hạnh nhân", "hạt điều", "óc chó", "hạt phỉ", "hồ đào", "hạt dẻ cười", "macadamia", "almond", "almonds", "cashew", "walnut", "hazelnut", "pecan", "pistachio", "brazil nut"),
            new("celery", "Cần tây", "Celery", "cần tây", "celery"),
            new("mustard", "Mù tạt", "Mustard", "mù tạt", "mustard"),
            new("sesame", "Mè (vừng)", "Sesame", "mè", "vừng", "sesame"),
            new("sulphites", "Sulfit", "Sulphites", "sulfit", "sulphite", "sulfite", "sulphites", "sulfites", "sulphur dioxide"),
            new("lupin", "Đậu lupin", "Lupin", "lupin"),
            new("molluscs", "Nhuyễn thể", "Molluscs", "mực", "bạch tuộc", "nghêu", "sò", "hến", "hàu", "ốc", "trai", "squid", "octopus", "clam", "oyster", "mussel", "scallop", "mollusc", "molluscs")
        };
        /// <summary>Additive codes that are themselves an allergen group (sulphites).</summary>
        public static readonly IReadOnlyDictionary<string, string> AllergenCodes = new Dictionary<string, string>
        {
            ["220"] = "sulphites", ["221"] = "sulphites", ["222"] = "sulphites", ["223"] = "sulphites", ["224"] = "sulphites", ["228"] = "sulphites"
        };
        /// <summary>Phrases that start a precautionary "may contain" statement.</summary>
        public static readonly IReadOnlyList<string> MayContainPhrases = new List<string>
        {
            "có thể chứa", "có thể có", "có thể lẫn", "may contain", "may also contain", "may have traces", "traces of", "sản xuất trên dây chuyền", "sản xuất chung dây chuyền", "made on a line", "produced in a factory"
        };
        /// <summary>Partially hydrogenated oils are the main source of industrial trans fat.</summary>
        public static readonly IReadOnlyList<string> PartiallyHydrogenated = new List<string>
        {
            "hydro hóa một phần", "hydro hoá một phần", "hydrogen hóa một phần", "hydrogen hoá một phần", "partially hydrogenated", "partially hydrogenated"
        };
        public static readonly IReadOnlyList<string> Caffeine = new List<string> { "caffeine", "cafein", "caffein", "cà phê", "coffee", "guarana" };

        public static readonly FoodNote PartiallyHydrogenatedNote = Note(FoodNoteLevel.Caution,
            "Có dầu/mỡ hydro hóa một phần, nguồn chính của chất béo chuyển hóa công nghiệp. WHO khuyến nghị chất béo chuyển hóa dưới 1% năng lượng và kêu gọi loại bỏ hoàn toàn.",
            "Contains partially hydrogenated fat, the main source of industrial trans fat. WHO advises trans fat under 1% of energy and calls for its elimination.",
            Sources.WhoReplace);
        public static readonly FoodNote TransFatNote = Note(FoodNoteLevel.Caution,
            "Nhãn ghi có chất béo chuyển hóa. WHO khuyến nghị dưới 1% năng lượng mỗi ngày (khoảng 2,2 g với 2000 kcal).",
            "The label declares trans fat. WHO advises under 1% of daily energy (about 2.2 g at 2000 kcal).",
            Sources.WhoFats2023);
        public static readonly FoodNote SweetenerNote = Note(FoodNoteLevel.Info,
            "Có chất tạo ngọt không đường. WHO (2023) khuyến nghị không dùng chúng để kiểm soát cân nặng (khuyến nghị có điều kiện).",
            "Contains non-sugar sweeteners. WHO (2023) advises against using them for weight control (conditional recommendation).",
            Sources.WhoSweeteners2023);
        public static readonly FoodNote CaffeineNote = Note(FoodNoteLevel.Info,
            "Có caffeine. EFSA: người lớn đến 400 mg/ngày (200 mg mỗi lần) là an toàn; phụ nữ mang thai, cho con bú đến 200 mg/ngày; không khuyến khích cho trẻ em.",
            "Contains caffeine. EFSA: up to 400 mg a day (200 mg at a time) is safe for adults; up to 200 mg a day in pregnancy and breastfeeding; not advised for children.",
            Sources.EfsaCaffeine);
        public static readonly FoodNote BenzoateAscorbicNote = Note(FoodNoteLevel.Info,
            "Đồ uống có cả natri benzoat (211) và vitamin C (300): hai chất này có thể tạo một lượng nhỏ benzen khi gặp nhiệt và ánh sáng; FDA đã khảo sát và nhà sản xuất thường điều chỉnh công thức.",
            "A drink with both sodium benzoate (211) and vitamin C (300): together they can form small amounts of benzene with heat and light; the FDA has surveyed this and makers usually reformulate.",
            Sources.FdaBenzene);
    }
}
