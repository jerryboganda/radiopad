using System.Text;
using System.Text.Json;

namespace RadioPad.Api.Services.ReportLayouts;

/// <summary>
/// Report Templates (RPT-030) — strict validating parser from a raw
/// <c>LayoutJson</c> string to a known-good <see cref="ReportLayoutModel"/>.
/// Mirrors <c>frontend/lib/reportLayouts/schema.ts</c>'s <c>validateLayout</c>
/// field-for-field so a layout that saves client-side also parses server-side.
///
/// Policy (see also <see cref="ReportLayoutsController"/> and
/// <see cref="RadioPad.Api.Controllers.ReportsController"/>): on <b>save</b> or
/// <b>preview</b>, any error here is a 400 <c>kind: "validation"</c> — nothing is
/// silently coerced. On <b>export</b> of an already-stored row, a parse failure
/// is logged and the caller falls back to the legacy Classic renderer — an
/// export must never fail because a layout went stale under a future schema.
/// </summary>
public static class ReportLayoutParser
{
    public static bool TryParse(string? json, out ReportLayoutModel? model, out IReadOnlyList<string> errors)
    {
        var errorList = new List<string>();
        errors = errorList;
        model = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            errorList.Add("layoutJson is required.");
            return false;
        }

        if (Encoding.UTF8.GetByteCount(json) > ReportLayoutBranding.MaxLayoutJsonBytes)
        {
            errorList.Add($"layoutJson exceeds the {ReportLayoutBranding.MaxLayoutJsonBytes / 1024} KB limit.");
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errorList.Add($"layoutJson is not valid JSON: {ex.Message}");
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errorList.Add("layoutJson root must be an object.");
                return false;
            }

            if (!root.TryGetProperty("schemaVersion", out var svEl) || svEl.ValueKind != JsonValueKind.Number ||
                !svEl.TryGetInt32(out var schemaVersion))
            {
                errorList.Add("schemaVersion is required and must be an integer.");
                return false;
            }
            if (schemaVersion != 1)
            {
                errorList.Add($"Unsupported schemaVersion {schemaVersion}; this build supports only 1.");
                return false;
            }

            PageSetupModel? page = null;
            if (!root.TryGetProperty("page", out var pageEl) || pageEl.ValueKind != JsonValueKind.Object)
            {
                errorList.Add("page is required and must be an object.");
            }
            else
            {
                page = ParsePage(pageEl, errorList);
            }

            var blocks = new List<LayoutBlockModel>();
            if (!root.TryGetProperty("blocks", out var blocksEl) || blocksEl.ValueKind != JsonValueKind.Array)
            {
                errorList.Add("blocks is required and must be an array.");
            }
            else
            {
                ParseBlocks(blocksEl, errorList, blocks);
            }

            LayoutFooterModel? footer = null;
            if (!root.TryGetProperty("footer", out var footerEl) || footerEl.ValueKind != JsonValueKind.Object)
            {
                errorList.Add("footer is required and must be an object.");
            }
            else
            {
                footer = ParseFooter(footerEl, errorList);
            }

            if (errorList.Count > 0 || page is null || footer is null)
            {
                return false;
            }

            model = new ReportLayoutModel
            {
                SchemaVersion = schemaVersion,
                Page = page,
                Blocks = blocks,
                Footer = footer,
            };
            return true;
        }
    }

    // ---- page ------------------------------------------------------------

    private static readonly Dictionary<string, LayoutPageSize> PageSizes = new(StringComparer.Ordinal)
    {
        ["letter"] = LayoutPageSize.Letter,
        ["a4"] = LayoutPageSize.A4,
    };

    private static readonly Dictionary<string, LayoutFont> Fonts = new(StringComparer.Ordinal)
    {
        ["sans"] = LayoutFont.Sans,
        ["serif"] = LayoutFont.Serif,
        ["mono"] = LayoutFont.Mono,
    };

    private static readonly Dictionary<string, LayoutAccent> Accents = new(StringComparer.Ordinal)
    {
        ["graphite"] = LayoutAccent.Graphite,
        ["navy"] = LayoutAccent.Navy,
        ["teal"] = LayoutAccent.Teal,
        ["burgundy"] = LayoutAccent.Burgundy,
        ["forest"] = LayoutAccent.Forest,
        ["slate"] = LayoutAccent.Slate,
    };

    private static PageSetupModel? ParsePage(JsonElement el, List<string> errors)
    {
        var ok = true;
        ok &= TryEnum(el, "page.size", errors, PageSizes, out var size, LayoutPageSize.Letter);
        ok &= TryNumber(el, "page.marginPt", errors, 24, 72, out var margin, 36);
        ok &= TryEnum(el, "page.font", errors, Fonts, out var font, LayoutFont.Sans);
        ok &= TryNumber(el, "page.baseFontSizePt", errors, 8, 14, out var baseSize, 11);
        ok &= TryEnum(el, "page.accent", errors, Accents, out var accent, LayoutAccent.Graphite);
        var showPageNumbers = TryBool(el, "page.showPageNumbers", true);

        if (!ok) return null;
        return new PageSetupModel
        {
            Size = size,
            MarginPt = margin,
            Font = font,
            BaseFontSizePt = baseSize,
            Accent = accent,
            ShowPageNumbers = showPageNumbers,
        };
    }

    // ---- footer ------------------------------------------------------------

    private static LayoutFooterModel? ParseFooter(JsonElement el, List<string> errors)
    {
        var showStatusLine = TryBool(el, "footer.showStatusLine", true);
        if (!TryOptionalString(el, "customText", errors, "footer.customText", 200, out var customText))
        {
            return null;
        }
        return new LayoutFooterModel { ShowStatusLine = showStatusLine, CustomText = customText };
    }

    // ---- blocks ------------------------------------------------------------

    private static readonly Dictionary<string, LayoutHeadingStyle> HeadingStyles = new(StringComparer.Ordinal)
    {
        ["uppercase"] = LayoutHeadingStyle.Uppercase,
        ["accent-bar"] = LayoutHeadingStyle.AccentBar,
        ["underline"] = LayoutHeadingStyle.Underline,
        ["plain"] = LayoutHeadingStyle.Plain,
    };

    private static readonly Dictionary<string, LayoutAlign> Aligns = new(StringComparer.Ordinal)
    {
        ["left"] = LayoutAlign.Left,
        ["center"] = LayoutAlign.Center,
        ["right"] = LayoutAlign.Right,
    };

    private static readonly Dictionary<string, LayoutSectionKey> SectionKeys = new(StringComparer.Ordinal)
    {
        ["indication"] = LayoutSectionKey.Indication,
        ["technique"] = LayoutSectionKey.Technique,
        ["comparison"] = LayoutSectionKey.Comparison,
        ["findings"] = LayoutSectionKey.Findings,
        ["impression"] = LayoutSectionKey.Impression,
        ["recommendations"] = LayoutSectionKey.Recommendations,
    };

    private static readonly Dictionary<string, LayoutStudyField> StudyFields = new(StringComparer.Ordinal)
    {
        ["patientReference"] = LayoutStudyField.PatientReference,
        ["accessionNumber"] = LayoutStudyField.AccessionNumber,
        ["modality"] = LayoutStudyField.Modality,
        ["bodyPart"] = LayoutStudyField.BodyPart,
        ["contrast"] = LayoutStudyField.Contrast,
        ["age"] = LayoutStudyField.Age,
        ["gender"] = LayoutStudyField.Gender,
        ["comparison"] = LayoutStudyField.Comparison,
        ["priorReportSummary"] = LayoutStudyField.PriorReportSummary,
        ["departmentTag"] = LayoutStudyField.DepartmentTag,
        ["reportDate"] = LayoutStudyField.ReportDate,
        ["status"] = LayoutStudyField.Status,
    };

    private static readonly Dictionary<string, LayoutDividerStyle> DividerStyles = new(StringComparer.Ordinal)
    {
        ["line"] = LayoutDividerStyle.Line,
        ["accent"] = LayoutDividerStyle.Accent,
        ["space"] = LayoutDividerStyle.Space,
    };

    private static readonly Dictionary<string, LayoutLogoPosition> LogoPositions = new(StringComparer.Ordinal)
    {
        ["left"] = LayoutLogoPosition.Left,
        ["right"] = LayoutLogoPosition.Right,
        ["above"] = LayoutLogoPosition.Above,
    };

    private static void ParseBlocks(JsonElement arr, List<string> errors, List<LayoutBlockModel> outBlocks)
    {
        if (arr.GetArrayLength() > ReportLayoutBranding.MaxBlocks)
        {
            errors.Add($"blocks may contain at most {ReportLayoutBranding.MaxBlocks} entries.");
            return;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenSingleton = new HashSet<string>(StringComparer.Ordinal);
        var seenSections = new HashSet<LayoutSectionKey>();
        var index = 0;

        foreach (var el in arr.EnumerateArray())
        {
            var path = $"blocks[{index}]";
            index++;

            if (el.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{path} must be an object.");
                continue;
            }

            if (!TryString(el, "id", errors, path, 40, out var id) || id is null)
            {
                continue;
            }
            if (!seenIds.Add(id))
            {
                errors.Add($"{path}: duplicate block id \"{id}\".");
                continue;
            }

            if (!el.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            {
                errors.Add($"{path}.type is required.");
                continue;
            }
            var type = typeEl.GetString() ?? "";

            switch (type)
            {
                case "letterhead":
                case "studyInfo":
                case "signatures":
                    if (!seenSingleton.Add(type))
                    {
                        errors.Add($"{path}: only one \"{type}\" block is allowed per layout.");
                        continue;
                    }
                    break;
            }

            LayoutBlockModel? block = type switch
            {
                "letterhead" => ParseLetterhead(el, id, errors, path),
                "studyInfo" => ParseStudyInfo(el, id, errors, path),
                "section" => ParseSection(el, id, errors, path, seenSections),
                "signatures" => ParseSignatures(el, id),
                "text" => ParseText(el, id, errors, path),
                "divider" => ParseDivider(el, id, errors, path),
                _ => Unknown(type, path, errors),
            };
            if (block is not null) outBlocks.Add(block);
        }
    }

    private static LayoutBlockModel? Unknown(string type, string path, List<string> errors)
    {
        errors.Add($"{path}.type \"{type}\" is not a recognised block type.");
        return null;
    }

    private static LayoutBlockModel? ParseLetterhead(JsonElement el, string id, List<string> errors, string path)
    {
        var ok = true;
        ok &= TryOptionalString(el, "clinicName", errors, $"{path}.clinicName", 200, out var clinicName);

        var lines = new List<string>();
        if (el.TryGetProperty("lines", out var linesEl))
        {
            if (linesEl.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{path}.lines must be an array."); ok = false;
            }
            else if (linesEl.GetArrayLength() > 4)
            {
                errors.Add($"{path}.lines may contain at most 4 lines."); ok = false;
            }
            else
            {
                foreach (var lineEl in linesEl.EnumerateArray())
                {
                    if (lineEl.ValueKind != JsonValueKind.String || (lineEl.GetString()?.Length ?? 0) > 120)
                    {
                        errors.Add($"{path}.lines entries must be strings of at most 120 characters."); ok = false;
                        break;
                    }
                    lines.Add(lineEl.GetString()!);
                }
            }
        }

        LayoutLogoModel? logo = null;
        if (el.TryGetProperty("logo", out var logoEl) && logoEl.ValueKind == JsonValueKind.Object)
        {
            logo = ParseLogo(logoEl, errors, $"{path}.logo");
            if (logo is null) ok = false;
        }
        else if (el.TryGetProperty("logo", out var logoNullEl) && logoNullEl.ValueKind != JsonValueKind.Null)
        {
            errors.Add($"{path}.logo must be an object or null."); ok = false;
        }

        ok &= TryEnum(el, $"{path}.logoPosition", errors, LogoPositions, out var logoPosition, LayoutLogoPosition.Left);
        ok &= TryEnum(el, $"{path}.align", errors, Aligns, out var align, LayoutAlign.Left);
        var showAccentRule = TryBool(el, $"{path}.showAccentRule", false);

        if (!ok) return null;
        return new LetterheadBlockModel(id, clinicName, lines, logo, logoPosition, align, showAccentRule);
    }

    private static LayoutLogoModel? ParseLogo(JsonElement el, List<string> errors, string path)
    {
        if (!el.TryGetProperty("dataUrl", out var urlEl) || urlEl.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path}.dataUrl is required.");
            return null;
        }
        var url = urlEl.GetString() ?? "";
        if (url.Length > 200_000)
        {
            errors.Add($"{path}.dataUrl exceeds the 200,000 character limit.");
            return null;
        }

        const string pngPrefix = "data:image/png;base64,";
        const string jpegPrefix = "data:image/jpeg;base64,";
        LayoutLogoFormat format;
        string base64;
        if (url.StartsWith(pngPrefix, StringComparison.Ordinal)) { format = LayoutLogoFormat.Png; base64 = url[pngPrefix.Length..]; }
        else if (url.StartsWith(jpegPrefix, StringComparison.Ordinal)) { format = LayoutLogoFormat.Jpeg; base64 = url[jpegPrefix.Length..]; }
        else
        {
            errors.Add($"{path}.dataUrl must be a data:image/png;base64, or data:image/jpeg;base64, URL.");
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            errors.Add($"{path}.dataUrl is not valid base64.");
            return null;
        }

        if (bytes.Length == 0 || bytes.Length > ReportLayoutBranding.MaxLogoBytes)
        {
            errors.Add($"{path}: decoded logo must be between 1 byte and {ReportLayoutBranding.MaxLogoBytes / 1024} KB.");
            return null;
        }

        // Magic-byte check so the declared format actually matches the bytes.
        var validMagic = format switch
        {
            LayoutLogoFormat.Png => bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            LayoutLogoFormat.Jpeg => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            _ => false,
        };
        if (!validMagic)
        {
            errors.Add($"{path}: decoded logo bytes do not match the declared image format.");
            return null;
        }

        if (!TryNumber(el, "widthPt", errors, 40, 250, out var widthPt, 120, path))
        {
            return null;
        }

        return new LayoutLogoModel { Bytes = bytes, Format = format, WidthPt = widthPt };
    }

    private static LayoutBlockModel? ParseStudyInfo(JsonElement el, string id, List<string> errors, string path)
    {
        var ok = true;
        var columns = 2;
        if (el.TryGetProperty("columns", out var colEl))
        {
            if (colEl.ValueKind != JsonValueKind.Number || !colEl.TryGetInt32(out columns) || columns is < 1 or > 3)
            {
                errors.Add($"{path}.columns must be 1, 2, or 3."); ok = false;
            }
        }
        var showBox = TryBool(el, $"{path}.showBox", false);

        var fields = new List<StudyFieldEntry>();
        if (el.TryGetProperty("fields", out var fieldsEl))
        {
            if (fieldsEl.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{path}.fields must be an array."); ok = false;
            }
            else if (fieldsEl.GetArrayLength() > 12)
            {
                errors.Add($"{path}.fields may contain at most 12 entries."); ok = false;
            }
            else
            {
                foreach (var fEl in fieldsEl.EnumerateArray())
                {
                    if (fEl.ValueKind != JsonValueKind.Object ||
                        !TryEnum(fEl, $"{path}.fields[].key", errors, StudyFields, out var key, null))
                    {
                        ok = false;
                        continue;
                    }
                    if (!TryOptionalString(fEl, "label", errors, $"{path}.fields[].label", 80, out var label))
                    {
                        ok = false;
                        continue;
                    }
                    fields.Add(new StudyFieldEntry(key!.Value, label));
                }
            }
        }

        if (!ok) return null;
        return new StudyInfoBlockModel(id, columns, showBox, fields);
    }

    private static LayoutBlockModel? ParseSection(JsonElement el, string id, List<string> errors, string path, HashSet<LayoutSectionKey> seenSections)
    {
        if (!TryEnum(el, $"{path}.section", errors, SectionKeys, out var section, null) || section is null)
        {
            return null;
        }
        if (!seenSections.Add(section.Value))
        {
            errors.Add($"{path}: a \"{section}\" section block is already present; each section may appear at most once.");
            return null;
        }
        if (!TryOptionalString(el, "label", errors, $"{path}.label", 60, out var label))
        {
            return null;
        }
        if (!TryEnum(el, $"{path}.headingStyle", errors, HeadingStyles, out var headingStyle, LayoutHeadingStyle.Uppercase))
        {
            return null;
        }
        var hideIfEmpty = TryBool(el, $"{path}.hideIfEmpty", true);
        return new SectionBlockModel(id, section.Value, label, headingStyle, hideIfEmpty);
    }

    private static LayoutBlockModel ParseSignatures(JsonElement el, string id) => new SignaturesBlockModel(
        id,
        TryBool(el, "showDate", true),
        TryBool(el, "showNote", true),
        TryBool(el, "showHash", false),
        TryBool(el, "showSignatureLine", true));

    private static LayoutBlockModel? ParseText(JsonElement el, string id, List<string> errors, string path)
    {
        if (!TryString(el, "content", errors, path, 4000, out var content) || content is null)
        {
            return null;
        }
        if (!TryEnum(el, $"{path}.align", errors, Aligns, out var align, LayoutAlign.Left))
        {
            return null;
        }
        var italic = TryBool(el, $"{path}.italic", false);
        var delta = 0;
        if (el.TryGetProperty("fontSizeDelta", out var deltaEl))
        {
            if (deltaEl.ValueKind != JsonValueKind.Number || !deltaEl.TryGetInt32(out delta) || delta is < -2 or > 2)
            {
                errors.Add($"{path}.fontSizeDelta must be an integer between -2 and 2.");
                return null;
            }
        }
        return new TextBlockModel(id, content, align, italic, delta);
    }

    private static LayoutBlockModel? ParseDivider(JsonElement el, string id, List<string> errors, string path)
    {
        if (!TryEnum(el, $"{path}.style", errors, DividerStyles, out var style, LayoutDividerStyle.Line))
        {
            return null;
        }
        if (!TryNumber(el, "spacePt", errors, 4, 48, out var spacePt, 16, path))
        {
            return null;
        }
        return new DividerBlockModel(id, style, spacePt);
    }

    // ---- small JSON helpers -------------------------------------------------

    private static bool TryString(JsonElement obj, string prop, List<string> errors, string path, int maxLen, out string? value)
    {
        value = null;
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path}.{prop} is required and must be a string.");
            return false;
        }
        var s = el.GetString() ?? "";
        if (s.Length > maxLen)
        {
            errors.Add($"{path}.{prop} exceeds the {maxLen} character limit.");
            return false;
        }
        value = s;
        return true;
    }

    private static bool TryOptionalString(JsonElement obj, string prop, List<string> errors, string path, int maxLen, out string? value)
    {
        value = null;
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (el.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path} must be a string or null.");
            return false;
        }
        var s = el.GetString() ?? "";
        if (s.Length > maxLen)
        {
            errors.Add($"{path} exceeds the {maxLen} character limit.");
            return false;
        }
        value = s;
        return true;
    }

    private static bool TryBool(JsonElement obj, string prop, bool @default)
    {
        // Booleans are always optional-with-default: every consumer sets a
        // sensible presentation default rather than rejecting the layout.
        var name = prop.Contains('.') ? prop[(prop.LastIndexOf('.') + 1)..] : prop;
        return obj.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : @default;
    }

    private static bool TryNumber(JsonElement obj, string prop, List<string> errors, double min, double max, out double value, double @default, string? pathOverride = null)
    {
        var name = prop.Contains('.') ? prop[(prop.LastIndexOf('.') + 1)..] : prop;
        var path = pathOverride is null ? prop : $"{pathOverride}.{name}";
        if (!obj.TryGetProperty(name, out var el))
        {
            value = @default;
            return true;
        }
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetDouble(out value) || value < min || value > max)
        {
            errors.Add($"{path} must be a number between {min} and {max}.");
            value = @default;
            return false;
        }
        return true;
    }

    private static bool TryEnum<T>(JsonElement obj, string prop, List<string> errors, IReadOnlyDictionary<string, T> map, out T? value, T? @default) where T : struct
    {
        var name = prop.Contains('.') ? prop[(prop.LastIndexOf('.') + 1)..] : prop;
        if (!obj.TryGetProperty(name, out var el))
        {
            value = @default;
            if (@default is null)
            {
                errors.Add($"{prop} is required and must be one of: {string.Join(", ", map.Keys)}.");
                return false;
            }
            return true;
        }
        if (el.ValueKind != JsonValueKind.String || el.GetString() is not { } s || !map.TryGetValue(s, out var mapped))
        {
            errors.Add($"{prop} must be one of: {string.Join(", ", map.Keys)}.");
            value = null;
            return false;
        }
        value = mapped;
        return true;
    }

    private static bool TryEnum<T>(JsonElement obj, string prop, List<string> errors, IReadOnlyDictionary<string, T> map, out T value, T @default) where T : struct
    {
        var ok = TryEnum(obj, prop, errors, map, out T? nullable, @default);
        value = nullable ?? @default;
        return ok;
    }
}
