using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

internal static class SliceFile
{
    internal const string Extension = ".gcode.3mf";
    private const int MetadataLimit = 8 * 1024 * 1024;
    private static readonly Regex PlatePath = new Regex(
        @"^Metadata/plate_[0-9]+\.gcode$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Names identify newer models automatically. IDs cover exports without a model name.
    private static readonly Dictionary<string, string> BambuModels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "BL-P001", "Bambu Lab X1 Carbon" }, { "BL-P002", "Bambu Lab X1" },
            { "C11", "Bambu Lab P1P" }, { "C12", "Bambu Lab P1S" }, { "C13", "Bambu Lab X1E" },
            { "N1", "Bambu Lab A1 mini" }, { "N2S", "Bambu Lab A1" },
            { "N6", "Bambu Lab X2D" }, { "N7", "Bambu Lab P2S" }, { "N8", "Bambu Lab N8" },
            { "N9", "Bambu Lab A2L" }, { "N10", "Bambu Lab H3C" },
            { "O1C", "Bambu Lab H2C" }, { "O1C2", "Bambu Lab H2C" }, { "O1D", "Bambu Lab H2D" },
            { "O1E", "Bambu Lab H2D Pro" }, { "O1S", "Bambu Lab H2S" }
        };

    internal static string ReadModel(Stream stream)
    {
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
        {
            bool hasPlate = false;
            foreach (var entry in archive.Entries)
            {
                if (entry.Length > 0 && PlatePath.IsMatch(entry.FullName))
                {
                    hasPlate = true;
                    break;
                }
            }
            if (!hasPlate)
                throw new InvalidDataException("文件中没有已切片的打印盘。");

            string declared = ReadProjectModel(archive.GetEntry("Metadata/project_settings.config"));
            string model = null;
            if (!String.IsNullOrWhiteSpace(declared))
            {
                model = RecognizeModel(declared);
                if (model == null)
                    return null;
            }

            var sliceInfo = archive.GetEntry("Metadata/slice_info.config");
            if (sliceInfo == null)
                return model;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            var xml = new XmlDocument { XmlResolver = null };
            using (var data = OpenMetadata(sliceInfo))
            using (var reader = XmlReader.Create(data, settings))
                xml.Load(reader);

            foreach (XmlElement plate in xml.SelectNodes("/config/plate/metadata[@key='printer_model_id']"))
            {
                string id = plate.GetAttribute("value");
                if (String.IsNullOrWhiteSpace(id))
                    continue;
                string candidate = RecognizeModel(id);
                if (candidate == null)
                {
                    if (String.IsNullOrWhiteSpace(declared))
                        return null;
                    continue;
                }
                if (model != null && !String.Equals(candidate, model, StringComparison.OrdinalIgnoreCase))
                    return null;
                model = candidate;
            }
            return model;
        }
    }

    private static string ReadProjectModel(ZipArchiveEntry entry)
    {
        if (entry == null)
            return null;
        using (var reader = new StreamReader(OpenMetadata(entry)))
        {
            var json = new JavaScriptSerializer { MaxJsonLength = MetadataLimit };
            var config = json.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
            object value;
            return config != null && config.TryGetValue("printer_model", out value) ? value as string : null;
        }
    }

    private static Stream OpenMetadata(ZipArchiveEntry entry)
    {
        if (entry.Length > MetadataLimit)
            throw new InvalidDataException("切片设置信息过大。");
        return entry.Open();
    }

    private static string RecognizeModel(string value)
    {
        value = value.Trim();
        string displayName;
        if (BambuModels.TryGetValue(value, out displayName))
            return displayName;
        return value.StartsWith("Bambu Lab ", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    internal static string ImportUrl(string path)
    {
        string fileName = Path.GetFileName(path);
        string title = fileName.Substring(0, fileName.Length - Extension.Length);
        return "bambu-connect://import-file?path=" + EncodeImportValue(path)
            + "&name=" + EncodeImportValue(title) + "&version=1.0.0";
    }

    private static string EncodeImportValue(string value)
    {
        // Connect decodes the whole link before it parses the query. Keep separators encoded until that second step.
        return Uri.EscapeDataString(value.Replace("%", "%25").Replace("&", "%26")
            .Replace("+", "%2B").Replace("#", "%23"));
    }

}
