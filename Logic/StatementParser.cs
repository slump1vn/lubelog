using CarCareTracker.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CarCareTracker.Logic
{
    /// <summary>One line of a bank statement.</summary>
    public class StatementRow
    {
        /// <summary>Line number in the file (1-based), to point the user at it.</summary>
        public int Line { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        /// <summary>False when the statement only gives the day; the time is then noon, Vietnam time.</summary>
        public bool HasTime { get; set; }
        public TransferDirection Direction { get; set; }
        public long Amount { get; set; }
        public long? Balance { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }
    public class StatementParseResult
    {
        public List<StatementRow> Rows { get; set; } = new List<StatementRow>();
        /// <summary>Which header the file used for each field, to show the user what was understood.</summary>
        public Dictionary<string, string> Columns { get; set; } = new Dictionary<string, string>();
        /// <summary>Account number found above the table, digits only; empty when none.</summary>
        public string AccountNumber { get; set; } = string.Empty;
        public List<string> Errors { get; set; } = new List<string>();
        public int SkippedLines { get; set; }
    }

    /// <summary>
    /// Reads a bank statement exported as .xlsx or .csv (VietinBank and most Vietnamese banks, in
    /// Vietnamese or English). The table header is found by its column names, so the layout above it
    /// (bank logo, account details) does not matter. No third-party Excel library: an .xlsx file is a
    /// zip of XML parts, and only the first sheet's cells are needed.
    /// </summary>
    public static class StatementParser
    {
        public const int MaxFileBytes = 5 * 1024 * 1024;
        private const long MaxXmlPartBytes = 40L * 1024 * 1024;
        private const int MaxRows = 20000;

        private static readonly (string Field, string[] Names)[] Fields =
        {
            ("date", new[] { "ngay giao dich", "thoi gian giao dich", "ngay gd", "ngay hach toan", "thoi gian", "ngay", "transaction date", "posting date", "trans date", "date", "ngay hieu luc", "value date" }),
            ("description", new[] { "noi dung giao dich", "noi dung", "dien giai", "mo ta", "chi tiet giao dich", "chi tiet", "description", "transaction details", "details", "remark", "remarks", "narrative" }),
            ("debit", new[] { "so tien ghi no", "ghi no", "phat sinh no", "so tien rut", "tien ra", "no", "rut", "chi", "debit", "withdrawal", "withdrawals", "money out" }),
            ("credit", new[] { "so tien ghi co", "ghi co", "phat sinh co", "so tien gui", "tien vao", "co", "gui", "thu", "credit", "deposit", "deposits", "money in" }),
            ("amount", new[] { "so tien giao dich", "so tien", "amount", "transaction amount" }),
            ("balance", new[] { "so du sau giao dich", "so du cuoi", "so du", "balance", "running balance", "closing balance" }),
            ("reference", new[] { "so tham chieu", "ma tham chieu", "ma giao dich", "so but toan", "ma gd", "so ct", "so chung tu", "reference", "reference no", "ref no", "transaction no", "transaction id" })
        };

        public static StatementParseResult Parse(byte[] content, string fileName)
        {
            var result = new StatementParseResult();
            if (content.Length == 0)
            {
                result.Errors.Add("The file is empty");
                return result;
            }
            if (content.Length > MaxFileBytes)
            {
                result.Errors.Add("The file is larger than 5 MB");
                return result;
            }
            List<List<string>> table;
            var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            bool isZip = content.Length > 4 && content[0] == 0x50 && content[1] == 0x4B;
            try
            {
                if (isZip || extension == ".xlsx")
                {
                    table = ReadXlsx(content);
                }
                else if (extension == ".xls" || (content.Length > 8 && content[0] == 0xD0 && content[1] == 0xCF))
                {
                    result.Errors.Add("Old .xls files are not supported. Open the file and save it as .xlsx or .csv");
                    return result;
                }
                else if (extension == ".pdf" || (content.Length > 4 && content[0] == 0x25 && content[1] == 0x50 && content[2] == 0x44 && content[3] == 0x46))
                {
                    result.Errors.Add("PDF statements are not supported. Export the statement as Excel (.xlsx) or .csv");
                    return result;
                }
                else
                {
                    table = ReadCsv(content);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is XmlException || ex is CsvHelperException || ex is IOException)
            {
                result.Errors.Add("The file could not be read as a statement");
                return result;
            }
            ReadTable(table, result);
            return result;
        }

        private static void ReadTable(List<List<string>> table, StatementParseResult result)
        {
            int headerIndex = -1;
            Dictionary<string, int> columns = new Dictionary<string, int>();
            for (int i = 0; i < Math.Min(table.Count, 60); i++)
            {
                var found = MatchHeader(table[i]);
                if (found.ContainsKey("date") && (found.ContainsKey("amount") || found.ContainsKey("debit") || found.ContainsKey("credit")))
                {
                    headerIndex = i;
                    columns = found;
                    break;
                }
            }
            //account number above the table: "Số tài khoản: 123..." or "Account number 123..."
            for (int i = 0; i < (headerIndex < 0 ? Math.Min(table.Count, 30) : headerIndex) && string.IsNullOrEmpty(result.AccountNumber); i++)
            {
                var line = string.Join(" ", table[i]);
                var normalized = Normalize(line);
                if (normalized.Contains("tai khoan") || normalized.Contains("account"))
                {
                    var match = Regex.Match(line, @"\d[\d\s\.\-]{5,24}\d");
                    if (match.Success)
                    {
                        result.AccountNumber = Regex.Replace(match.Value, @"\D", "");
                    }
                }
            }
            if (headerIndex < 0)
            {
                result.Errors.Add("No table with a date and an amount (or debit/credit) column was found");
                return;
            }
            foreach (var column in columns)
            {
                result.Columns[column.Key] = table[headerIndex][column.Value].Trim();
            }
            for (int i = headerIndex + 1; i < table.Count && result.Rows.Count < MaxRows; i++)
            {
                var cells = table[i];
                string Cell(string field) => columns.TryGetValue(field, out int index) && index < cells.Count ? cells[index].Trim() : string.Empty;
                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }
                var date = ParseDate(Cell("date"), out bool hasTime);
                long debit = 0, credit = 0;
                long? amount = null;
                if (columns.ContainsKey("debit") || columns.ContainsKey("credit"))
                {
                    debit = Math.Abs(ParseAmount(Cell("debit")) ?? 0);
                    credit = Math.Abs(ParseAmount(Cell("credit")) ?? 0);
                }
                else
                {
                    amount = ParseAmount(Cell("amount"));
                }
                if (date == null)
                {
                    //totals, opening balance and footer lines have no transaction date.
                    result.SkippedLines++;
                    continue;
                }
                var row = new StatementRow
                {
                    Line = i + 1,
                    OccurredAtUtc = DateTime.SpecifyKind(date.Value - SepayRules.VietnamOffset, DateTimeKind.Utc),
                    HasTime = hasTime,
                    Balance = ParseAmount(Cell("balance")),
                    Description = Cell("description"),
                    Reference = Cell("reference")
                };
                if (amount.HasValue)
                {
                    row.Direction = amount.Value < 0 ? TransferDirection.Out : TransferDirection.In;
                    row.Amount = Math.Abs(amount.Value);
                }
                else if (debit > 0 && credit == 0)
                {
                    row.Direction = TransferDirection.Out;
                    row.Amount = debit;
                }
                else if (credit > 0 && debit == 0)
                {
                    row.Direction = TransferDirection.In;
                    row.Amount = credit;
                }
                if (row.Amount <= 0)
                {
                    result.SkippedLines++;
                    continue;
                }
                result.Rows.Add(row);
            }
            if (!result.Rows.Any())
            {
                result.Errors.Add("The statement has no transactions");
            }
        }

        private static Dictionary<string, int> MatchHeader(List<string> cells)
        {
            var found = new Dictionary<string, int>();
            for (int index = 0; index < cells.Count; index++)
            {
                //"Số dư (VND)" -> "so du"; "Ngày GD/Trans Date" -> "ngay gd trans date"
                var header = Regex.Replace(Normalize(cells[index]), @"\([^)]*\)", " ");
                header = Regex.Replace(header, @"\s+", " ").Trim();
                if (header.Length == 0 || header.Length > 60)
                {
                    continue;
                }
                foreach (var (field, names) in Fields)
                {
                    if (found.ContainsKey(field))
                    {
                        continue;
                    }
                    //one-word names ("no", "co", "chi", "ngay") only as the whole header; phrases may be part of it.
                    if (names.Any(name => header == name || (name.Contains(' ') && (header.StartsWith(name + " ") || header.EndsWith(" " + name) || header.Contains(" " + name + " ")))))
                    {
                        found[field] = index;
                        break;
                    }
                }
            }
            return found;
        }

        /// <summary>Lower case, no Vietnamese accents, single spaces, no punctuation except parentheses.</summary>
        public static string Normalize(string text)
        {
            var decomposed = (text ?? string.Empty).Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }
                sb.Append(char.IsLetterOrDigit(c) || c == '(' || c == ')' ? char.ToLowerInvariant(c) : ' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private static readonly string[] DateFormats =
        {
            "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy", "d/M/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss", "d/M/yyyy HH:mm", "d/M/yyyy H:mm", "d/M/yyyy",
            "dd-MM-yyyy HH:mm:ss", "dd-MM-yyyy HH:mm", "dd-MM-yyyy", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy",
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd",
            "HH:mm:ss dd/MM/yyyy", "HH:mm dd/MM/yyyy"
        };
        /// <summary>A Vietnam-time date (kind Unspecified), or null.</summary>
        public static DateTime? ParseDate(string text, out bool hasTime)
        {
            hasTime = false;
            var value = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
            if (value.Length == 0)
            {
                return null;
            }
            //Excel serial date (days since 1899-12-30), with the time as the fraction.
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial))
            {
                if (serial > 20000 && serial < 80000)
                {
                    var date = DateTime.FromOADate(serial);
                    hasTime = date.TimeOfDay != TimeSpan.Zero;
                    return hasTime ? date : date.AddHours(12);
                }
                return null;
            }
            if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                hasTime = value.Contains(':');
                return hasTime ? parsed : parsed.AddHours(12);
            }
            return null;
        }

        /// <summary>
        /// Whole VND from "1.234.567", "1,234,567", "1234567.00", "-50,000", "(50.000)" or "50.000 VND".
        /// Null when the text holds no number.
        /// </summary>
        public static long? ParseAmount(string text)
        {
            var value = (text ?? string.Empty).Trim();
            bool negative = value.StartsWith("-") || (value.StartsWith("(") && value.EndsWith(")"));
            value = Regex.Replace(value, @"[^\d\.,]", "");
            if (value.Length == 0 || !value.Any(char.IsDigit))
            {
                return null;
            }
            int lastDot = value.LastIndexOf('.'), lastComma = value.LastIndexOf(',');
            int decimalAt = -1;
            if (lastDot >= 0 && lastComma >= 0)
            {
                decimalAt = Math.Max(lastDot, lastComma);
            }
            else if (lastDot >= 0 || lastComma >= 0)
            {
                char separator = lastDot >= 0 ? '.' : ',';
                int last = Math.Max(lastDot, lastComma);
                int count = value.Count(c => c == separator);
                int digitsAfter = value.Length - last - 1;
                //one separator not followed by exactly three digits is a decimal point ("50000.00", "1.5").
                if (count == 1 && digitsAfter != 3)
                {
                    decimalAt = last;
                }
            }
            string whole = decimalAt >= 0 ? value.Substring(0, decimalAt) : value;
            whole = Regex.Replace(whole, @"[\.,]", "");
            if (whole.Length == 0)
            {
                whole = "0";
            }
            if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out long amount))
            {
                return null;
            }
            //VND has no minor unit: round half up on the fraction.
            if (decimalAt >= 0)
            {
                var fraction = value.Substring(decimalAt + 1);
                if (fraction.Length > 0 && fraction[0] >= '5')
                {
                    amount++;
                }
            }
            return negative ? -amount : amount;
        }

        private static List<List<string>> ReadCsv(byte[] content)
        {
            var text = DecodeText(content);
            var firstLines = string.Join("\n", text.Split('\n').Take(20));
            char delimiter = new[] { ',', ';', '\t' }.OrderByDescending(d => firstLines.Count(c => c == d)).First();
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = delimiter.ToString(),
                HasHeaderRecord = false,
                BadDataFound = null,
                MissingFieldFound = null,
                DetectColumnCountChanges = false
            };
            var rows = new List<List<string>>();
            using var reader = new StringReader(text);
            using var parser = new CsvParser(reader, config);
            while (parser.Read() && rows.Count <= MaxRows + 100)
            {
                rows.Add((parser.Record ?? Array.Empty<string>()).ToList());
            }
            return rows;
        }

        private static string DecodeText(byte[] content)
        {
            if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(content, 2, content.Length - 2);
            }
            if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(content, 3, content.Length - 3);
            }
            return Encoding.UTF8.GetString(content);
        }

        private static readonly XNamespace SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        private static List<List<string>> ReadXlsx(byte[] content)
        {
            using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            var sharedStrings = new List<string>();
            var sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
            if (sharedEntry != null)
            {
                var doc = LoadXml(sharedEntry);
                foreach (var si in doc.Root?.Elements(SheetNs + "si") ?? Enumerable.Empty<XElement>())
                {
                    sharedStrings.Add(string.Concat(si.Descendants(SheetNs + "t").Select(t => t.Value)));
                }
            }
            var sheetEntry = FirstSheet(zip) ?? throw new InvalidDataException("No worksheet");
            var sheet = LoadXml(sheetEntry);
            var rows = new List<List<string>>();
            foreach (var row in sheet.Descendants(SheetNs + "row"))
            {
                if (rows.Count > MaxRows + 100)
                {
                    break;
                }
                int rowNumber = int.TryParse((string?)row.Attribute("r"), out int r) ? r : rows.Count + 1;
                //keep empty rows so line numbers match what the user sees in Excel.
                while (rows.Count < rowNumber - 1)
                {
                    rows.Add(new List<string>());
                }
                var cells = new List<string>();
                foreach (var cell in row.Elements(SheetNs + "c"))
                {
                    int column = ColumnIndex((string?)cell.Attribute("r")) ?? cells.Count;
                    if (column > 200)
                    {
                        continue;
                    }
                    while (cells.Count < column)
                    {
                        cells.Add(string.Empty);
                    }
                    var type = (string?)cell.Attribute("t");
                    string value;
                    if (type == "s")
                    {
                        value = int.TryParse(cell.Element(SheetNs + "v")?.Value, out int index) && index >= 0 && index < sharedStrings.Count ? sharedStrings[index] : string.Empty;
                    }
                    else if (type == "inlineStr")
                    {
                        value = string.Concat(cell.Descendants(SheetNs + "t").Select(t => t.Value));
                    }
                    else
                    {
                        value = cell.Element(SheetNs + "v")?.Value ?? string.Empty;
                    }
                    cells.Add(value);
                }
                rows.Add(cells);
            }
            return rows;
        }

        private static ZipArchiveEntry? FirstSheet(ZipArchive zip)
        {
            var workbookEntry = zip.GetEntry("xl/workbook.xml");
            var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry != null && relsEntry != null)
            {
                var workbook = LoadXml(workbookEntry);
                var rels = LoadXml(relsEntry);
                var relId = (string?)workbook.Descendants(SheetNs + "sheet").FirstOrDefault()?.Attribute(RelNs + "id");
                var target = (string?)rels.Root?.Elements(PackageRelNs + "Relationship").FirstOrDefault(x => (string?)x.Attribute("Id") == relId)?.Attribute("Target");
                if (!string.IsNullOrEmpty(target))
                {
                    var path = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
                    var entry = zip.GetEntry(path);
                    if (entry != null)
                    {
                        return entry;
                    }
                }
            }
            return zip.GetEntry("xl/worksheets/sheet1.xml") ?? zip.Entries.FirstOrDefault(x => x.FullName.StartsWith("xl/worksheets/") && x.FullName.EndsWith(".xml"));
        }

        private static XDocument LoadXml(ZipArchiveEntry entry)
        {
            if (entry.Length > MaxXmlPartBytes)
            {
                throw new InvalidDataException("Part too large");
            }
            using var stream = entry.Open();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlPartBytes };
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader);
        }

        private static int? ColumnIndex(string? reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }
            int index = 0, i = 0;
            while (i < reference.Length && char.IsLetter(reference[i]))
            {
                index = index * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
                i++;
            }
            return i == 0 ? null : index - 1;
        }
    }
}
