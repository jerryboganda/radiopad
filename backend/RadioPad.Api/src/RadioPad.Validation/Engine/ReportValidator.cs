using System.Text.RegularExpressions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
using RadioPad.Validation.Rulebook;

namespace RadioPad.Validation.Engine;

/// <summary>
/// Deterministic report-validation engine. Runs every rule from the active
/// rulebook against the supplied <see cref="Report"/> and returns a list of
/// findings. AI-assisted checks (e.g. unsupported-claim detection) are
/// orchestrated separately through the AI gateway and merged into the same
/// <see cref="ValidationResult"/> shape.
/// </summary>
public class ReportValidator
{
    private static readonly Regex LeftRe = new(@"\b(left|left-?sided)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RightRe = new(@"\b(right|right-?sided)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MeasurementRe = new(@"(\d+(?:\.\d+)?)\s*(mm|cm)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SpineLevelRe = new(@"\b([CTLS])(\d)\s*[-/]\s*([CTLS]?)(\d)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BiRadsCategoryRe = new(@"\bBI[-\s]?RADS\b\s*:?\s*(?:category\s*:?\s*)?(0|1|2|3|4A|4B|4C|4|5|6)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LungRadsCategoryRe = new(@"\bLung[-\s]?RADS\b\s*:?\s*(?:category\s*:?\s*)?(0|1|2|3|4A|4B|4X)(?:\s*S)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LiRadsCategoryRe = new(@"\bLI[-\s]?RADS\b\s*:?\s*(?:category\s*:?\s*)?(?:LR[-\s]?)?(1|2|3|4|5|M|TIV|NC)\b|\bLR[-\s]?(1|2|3|4|5|M|TIV|NC)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PiRadsCategoryRe = new(@"\bPI[-\s]?RADS\b\s*:?\s*(?:category\s*:?\s*)?([1-5])\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BiRadsCategoryLineRe = new(@"(?m)^\s*(?:\d+\.|[-*])?\s*BI[-\s]?RADS\b\s*:?\s*(?:category\s*:?\s*)?(0|1|2|3|4A|4B|4C|4|5|6)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TiRadsCategoryRe = new(@"\b(?:TI[-\s]?RADS\b\s*:?\s*(?:category\s*:?\s*)?(?:TR)?[1-5]|TR[1-5])\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ContrastPhaseRe = new(@"\b(portal[-\s]+venous|arterial|delayed|non[-\s]?contrast|with\s+contrast)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GcsRe = new(@"\bGCS\s*:?\s*\d{1,2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BiaxialMeasurementRe = new(@"\d+(?:\.\d+)?\s*[x×]\s*\d+(?:\.\d+)?\s*(?:mm|cm)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FigoStageRe = new(@"\bFIGO\s+stage\b|\bStage\s+(?:I{1,3}V?|IV)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FollowUpApprovedRe = new(@"\b(FNA|fine\s+needle\s+aspiration|follow[-\s]?up|no\s+follow[-\s]?up\s+needed)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ValidationResult Validate(Report report, RulebookSpec rulebook)
        => Validate(report, rulebook, lexicon: null);

    /// <summary>
    /// Overload that incorporates the tenant lexicon (PRD STD-006). Each
    /// forbidden term that appears in any section emits a Warning finding
    /// with rule id <c>lexicon:&lt;term&gt;</c>.
    /// </summary>
    public ValidationResult Validate(
        Report report,
        RulebookSpec rulebook,
        IReadOnlyCollection<TenantLexicon>? lexicon)
    {
        var findings = new List<ValidationFinding>();
        var sectionMap = SectionMap(report);

        if (lexicon is { Count: > 0 })
        {
            foreach (var entry in lexicon.Where(l => l.Forbidden && !string.IsNullOrWhiteSpace(l.Term)))
            {
                foreach (var (section, content) in sectionMap)
                {
                    if (Regex.IsMatch(content, $@"\b{Regex.Escape(entry.Term)}\b", RegexOptions.IgnoreCase))
                    {
                        var msg = string.IsNullOrWhiteSpace(entry.Replacement)
                            ? $"Term '{entry.Term}' is not allowed by tenant lexicon."
                            : $"Term '{entry.Term}' is not allowed; prefer '{entry.Replacement}'.";
                        findings.Add(new ValidationFinding(
                            RuleId: $"lexicon:{entry.Term.ToLowerInvariant()}",
                            Severity: nameof(ValidationSeverity.Warning),
                            Message: msg,
                            Section: section,
                            Snippet: entry.Term));
                    }
                }
            }
        }

        return ValidateCore(report, rulebook, sectionMap, findings);
    }

    private ValidationResult ValidateCore(
        Report report,
        RulebookSpec rulebook,
        Dictionary<string, string> sectionMap,
        List<ValidationFinding> findings)
    {
        // Required sections (RB-005, AI-005)
        foreach (var required in rulebook.RequiredSections)
        {
            if (!sectionMap.TryGetValue(required, out var content) || string.IsNullOrWhiteSpace(content))
            {
                findings.Add(new ValidationFinding(
                    RuleId: $"required_section:{required.ToLowerInvariant()}",
                    Severity: nameof(ValidationSeverity.Blocker),
                    Message: $"Required section '{required}' is missing or empty.",
                    Section: required));
            }
        }

        // Style: avoid_terms (RB-005)
        foreach (var term in rulebook.Style.AvoidTerms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            foreach (var (section, content) in sectionMap)
            {
                if (Regex.IsMatch(content, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase))
                {
                    findings.Add(new ValidationFinding(
                        RuleId: "style:avoid_term",
                        Severity: nameof(ValidationSeverity.Warning),
                        Message: $"Avoid the term '{term}' in this report style.",
                        Section: section,
                        Snippet: term));
                }
            }
        }

        // Built-in rule resolvers keyed by rulebook rule id
        foreach (var rule in rulebook.Rules)
        {
            switch (rule.Id)
            {
                case "laterality_consistency":
                    findings.AddRange(CheckLaterality(report, rule));
                    break;
                case "measurement_consistency":
                    findings.AddRange(CheckMeasurements(report, rule));
                    break;
                case "level_consistency":
                    findings.AddRange(CheckLevelConsistency(report, rule));
                    break;
                case "negation_conflict":
                    findings.AddRange(CheckNegationConflict(report, rule));
                    break;
                case "modality_mismatch":
                    findings.AddRange(CheckModalityMismatch(report, rule));
                    break;
                case "impression_bullet_count":
                    findings.AddRange(CheckImpressionBullets(report, rulebook, rule));
                    break;
                case "critical_result_language":
                    findings.AddRange(CheckCriticalLanguage(report, rule));
                    break;
                case "birads_category_required":
                    findings.AddRange(RequireAssertedPattern(report.Impression, BiRadsCategoryRe, rule, "Impression", "Add the BI-RADS assessment category to the impression."));
                    break;
                case "birads_assessment_in_impression":
                    findings.AddRange(CheckBiRadsAssessmentLine(report, rule));
                    break;
                case "lungrads_category_required":
                    findings.AddRange(RequireAssertedPattern(report.Impression, LungRadsCategoryRe, rule, "Impression", "Add the Lung-RADS assessment category to the impression."));
                    break;
                case "nodule_dimensions_required":
                    findings.AddRange(CheckNoduleDimensions(report, rule));
                    break;
                case "lirads_category_required":
                    findings.AddRange(RequireAssertedPattern(report.Impression, LiRadsCategoryRe, rule, "Impression", "Add the LI-RADS assessment category to the impression."));
                    break;
                case "lirads_observation_size_required":
                    findings.AddRange(CheckObservationSize(report, rule));
                    break;
                case "pirads_category_required":
                    findings.AddRange(RequireAssertedPattern(report.Impression, PiRadsCategoryRe, rule, "Impression", "Add the PI-RADS assessment category to the impression."));
                    break;
                case "index_lesion_localized":
                    findings.AddRange(CheckIndexLesionLocalization(report, rule));
                    break;
                case "unauthorized_followup":
                    findings.AddRange(CheckUnauthorizedFollowup(report, rulebook, rule));
                    break;
                case "contrast_phase_documented":
                    findings.AddRange(RequirePattern(sectionMap.GetValueOrDefault("Technique") ?? string.Empty, ContrastPhaseRe, rule, "Technique", "Document the contrast phase in the Technique section."));
                    break;
                case "incidental_findings_listed":
                    findings.AddRange(CheckIncidentalFindings(report, rule));
                    break;
                case "required_organ_coverage":
                    findings.AddRange(CheckRequiredOrganCoverage(report, rule));
                    break;
                case "critical_finding_language":
                    findings.AddRange(CheckCriticalLanguage(report, rule));
                    break;
                case "midline_shift_measured":
                    findings.AddRange(CheckMidlineShiftMeasured(report, rule));
                    break;
                case "gcs_documented":
                    findings.AddRange(RequirePattern(report.Indication, GcsRe, rule, "Indication", "Document the GCS score in the Indication or clinical context."));
                    break;
                case "meniscus_tear_pattern_described":
                    findings.AddRange(CheckMeniscusTearPattern(report, rule));
                    break;
                case "acl_pcl_status_documented":
                    findings.AddRange(CheckAclPclStatus(report, rule));
                    break;
                case "lungrads_category_mandatory":
                    findings.AddRange(RequireAssertedPattern(report.Impression, LungRadsCategoryRe, rule, "Impression", "Add the Lung-RADS assessment category to the impression."));
                    break;
                case "prior_comparison_required":
                    findings.AddRange(CheckPriorComparison(report, rule));
                    break;
                case "nodule_measurement_3d":
                    findings.AddRange(CheckNoduleBiaxialMeasurement(report, rule));
                    break;
                case "fracture_acuity":
                    findings.AddRange(CheckFractureAcuity(report, rule));
                    break;
                case "figo_staging_when_oncologic":
                    findings.AddRange(CheckFigoStaging(report, rule));
                    break;
                case "pirads_category_mandatory":
                    findings.AddRange(RequireAssertedPattern(report.Impression, PiRadsCategoryRe, rule, "Impression", "Add the PI-RADS assessment category to the impression."));
                    break;
                case "rotator_cuff_thickness_described":
                    findings.AddRange(CheckRotatorCuffThickness(report, rule));
                    break;
                case "labrum_described":
                    findings.AddRange(CheckLabrumDescribed(report, rule));
                    break;
                case "tirads_category_mandatory":
                    findings.AddRange(RequireAssertedPattern(report.Impression, TiRadsCategoryRe, rule, "Impression", "Add the TI-RADS assessment category to the impression."));
                    break;
                case "nodule_size_required":
                    findings.AddRange(CheckNoduleDimensions(report, rule));
                    break;
                case "follow_up_language_approved":
                    findings.AddRange(CheckFollowUpLanguageApproved(report, rule));
                    break;
                default:
                    break;
            }
        }

        var blocker = findings.Any(f => string.Equals(f.Severity, nameof(ValidationSeverity.Blocker), StringComparison.OrdinalIgnoreCase));

        var blockerCount = findings.Count(f => string.Equals(f.Severity, nameof(ValidationSeverity.Blocker), StringComparison.OrdinalIgnoreCase));
        var warningCount = findings.Count(f => string.Equals(f.Severity, nameof(ValidationSeverity.Warning), StringComparison.OrdinalIgnoreCase));
        var infoCount = findings.Count(f => string.Equals(f.Severity, nameof(ValidationSeverity.Info), StringComparison.OrdinalIgnoreCase));
        var qualityScore = Math.Max(0, 100 - (blockerCount * 25) - (warningCount * 5) - (infoCount * 1));

        return new ValidationResult(blocker, findings, qualityScore);
    }

    private static Dictionary<string, string> SectionMap(Report r) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Indication"] = r.Indication,
        ["Technique"] = r.Technique,
        ["Comparison"] = r.Comparison,
        ["Findings"] = r.Findings,
        ["Impression"] = r.Impression,
        ["Recommendations"] = r.Recommendations,
    };

    private static IEnumerable<ValidationFinding> CheckLaterality(Report r, RulebookSpec.RuleSpec rule)
    {
        // If findings assert only "left" but impression asserts only "right" (or vice-versa), flag.
        // Negated side mentions such as "no left-sided abnormality" are not treated as asserted laterality.
        var findingsLeft = HasAssertedSide(r.Findings, LeftRe);
        var findingsRight = HasAssertedSide(r.Findings, RightRe);
        var contextLeft = HasAssertedSide(r.Indication + " " + r.Technique, LeftRe);
        var contextRight = HasAssertedSide(r.Indication + " " + r.Technique, RightRe);
        var impressionLeft = HasAssertedSide(r.Impression, LeftRe);
        var impressionRight = HasAssertedSide(r.Impression, RightRe);

        static bool Conflict(bool sourceLeft, bool sourceRight, bool impressionLeft, bool impressionRight) =>
            (sourceLeft && !sourceRight && impressionRight && !impressionLeft) ||
            (sourceRight && !sourceLeft && impressionLeft && !impressionRight);

        if (Conflict(findingsLeft, findingsRight, impressionLeft, impressionRight) ||
            Conflict(contextLeft, contextRight, impressionLeft, impressionRight))
        {
            yield return new ValidationFinding(
                RuleId: rule.Id,
                Severity: rule.Severity,
                Message: "Laterality conflict between Findings and Impression.",
                Section: "Impression");
        }
    }

    private static bool HasAssertedSide(string text, Regex sideRegex)
    {
        var clauses = Regex.Split(text ?? string.Empty, @"(?<=[.;\n])\s+|\n+");
        foreach (var clause in clauses)
        {
            foreach (Match side in sideRegex.Matches(clause))
            {
                var prefix = clause[..side.Index];
                if (Regex.IsMatch(prefix, @"\b(no|without|negative for|absence of)\s+(acute\s+)?$", RegexOptions.IgnoreCase))
                    continue;
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<ValidationFinding> CheckMeasurements(Report r, RulebookSpec.RuleSpec rule)
    {
        var findingMs = MeasurementRe.Matches(r.Findings).Select(NormalizeMeasurement).ToHashSet();
        var impressionMs = MeasurementRe.Matches(r.Impression).Select(NormalizeMeasurement).ToHashSet();
        var orphan = impressionMs.Except(findingMs).ToList();
        foreach (var m in orphan)
        {
            yield return new ValidationFinding(
                RuleId: rule.Id,
                Severity: rule.Severity,
                Message: $"Measurement '{m}' appears in Impression but not in Findings.",
                Section: "Impression",
                Snippet: m);
        }
    }

    private static string NormalizeMeasurement(Match m)
    {
        var unit = m.Groups[2].Value.ToLowerInvariant();
        var value = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (unit == "cm") { value *= 10; unit = "mm"; }
        return $"{value:0.##}{unit}";
    }

    private static IEnumerable<ValidationFinding> CheckLevelConsistency(Report r, RulebookSpec.RuleSpec rule)
    {
        var terms = new[]
        {
            "disc protrusion",
            "disc herniation",
            "canal stenosis",
            "central canal stenosis",
            "foraminal narrowing",
            "facet arthropathy",
        };

        var findingClauses = Regex.Split(r.Findings ?? string.Empty, @"(?<=[.;\n])\s+|\n+");
        var impressionClauses = Regex.Split(r.Impression ?? string.Empty, @"(?<=[.;\n])\s+|\n+");
        foreach (var term in terms)
        {
            var findingLevels = findingClauses
                .Where(c => c.Contains(term, StringComparison.OrdinalIgnoreCase))
                .SelectMany(ExtractSpineLevels)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (findingLevels.Count == 0) continue;

            foreach (var clause in impressionClauses.Where(c => c.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                var impressionLevels = ExtractSpineLevels(clause).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (impressionLevels.Count == 0) continue;
                if (impressionLevels.Any(level => !findingLevels.Contains(level)))
                {
                    yield return new ValidationFinding(
                        RuleId: rule.Id,
                        Severity: rule.Severity,
                        Message: $"Spine level conflict for '{term}' between Findings and Impression.",
                        Section: "Impression",
                        Snippet: string.Join(", ", impressionLevels));
                    yield break;
                }
            }
        }
    }

    private static IEnumerable<string> ExtractSpineLevels(string text)
    {
        foreach (Match match in SpineLevelRe.Matches(text ?? string.Empty))
        {
            var startPrefix = match.Groups[1].Value.ToUpperInvariant();
            var startNumber = match.Groups[2].Value;
            var endPrefix = match.Groups[3].Success && !string.IsNullOrWhiteSpace(match.Groups[3].Value)
                ? match.Groups[3].Value.ToUpperInvariant()
                : startPrefix;
            var endNumber = match.Groups[4].Value;
            yield return $"{startPrefix}{startNumber}-{endPrefix}{endNumber}";
        }
    }

    private static IEnumerable<ValidationFinding> CheckNegationConflict(Report r, RulebookSpec.RuleSpec rule)
    {
        // Negation conflict: a concept denied in Findings is asserted in Impression.
        var negations = Regex.Matches(r.Findings, @"\bno\s+([^.;\n]{3,80})", RegexOptions.IgnoreCase);
        foreach (Match neg in negations)
        {
            foreach (var concept in SplitNegatedConcepts(neg.Groups[1].Value))
            {
                if (!ContainsConcept(r.Impression, concept)) continue;
                if (IsNegatedMention(r.Impression, concept)) continue;
                yield return new ValidationFinding(
                    RuleId: rule.Id,
                    Severity: rule.Severity,
                    Message: $"Findings deny '{concept}' but Impression appears to assert it.",
                    Section: "Impression",
                    Snippet: concept);
                yield break;
            }
        }
    }

    private static IEnumerable<string> SplitNegatedConcepts(string raw)
    {
        foreach (var part in Regex.Split(raw ?? string.Empty, @"\s+\b(?:or|and)\b\s+|,"))
        {
            var concept = Regex.Replace(part.Trim(), @"\s+", " ").Trim(' ', '.', ';', ':');
            if (concept.Length < 4) continue;
            yield return concept.ToLowerInvariant();
        }
    }

    private static bool ContainsConcept(string text, string concept)
        => Regex.IsMatch(text ?? string.Empty, $@"\b{Regex.Escape(concept)}\b", RegexOptions.IgnoreCase);

    private static bool IsNegatedMention(string text, string concept)
        => Regex.IsMatch(text ?? string.Empty,
            $@"\b(no|without|negative for|absence of|no evidence of)\b[^.;\n]{{0,80}}\b{Regex.Escape(concept)}\b",
            RegexOptions.IgnoreCase);

    private static IEnumerable<ValidationFinding> CheckModalityMismatch(Report r, RulebookSpec.RuleSpec rule)
    {
        var modality = r.Study.Modality.ToUpperInvariant();
        var pairs = new (string Other, string Phrase)[]
        {
            ("CT", "MRI"), ("CT", "ultrasound"), ("MR", "CT"), ("MR", "ultrasound"),
            ("US", "CT"), ("US", "MRI"), ("XR", "MRI"), ("XR", "CT"),
        };
        foreach (var (other, phrase) in pairs)
        {
            if (modality.StartsWith(other)) continue;
            if (modality == other) continue;
            // We only care about phrase appearing while the actual modality is something else.
            if (!string.Equals(modality, other, StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(r.Findings + " " + r.Impression, $@"\b{Regex.Escape(phrase)}\b", RegexOptions.IgnoreCase) &&
                !modality.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                yield return new ValidationFinding(
                    RuleId: rule.Id,
                    Severity: rule.Severity,
                    Message: $"Report references '{phrase}' but the study modality is '{r.Study.Modality}'.",
                    Snippet: phrase);
                break;
            }
        }
    }

    private static IEnumerable<ValidationFinding> CheckImpressionBullets(Report r, RulebookSpec rb, RulebookSpec.RuleSpec rule)
    {
        var bullets = r.Impression
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(l => Regex.IsMatch(l, @"^(\d+\.|-|\*)\s+"));
        if (bullets > rb.Style.ImpressionMaxBullets)
        {
            yield return new ValidationFinding(
                RuleId: rule.Id,
                Severity: rule.Severity,
                Message: $"Impression has {bullets} bullets; rulebook limit is {rb.Style.ImpressionMaxBullets}.",
                Section: "Impression");
        }
    }

    private static IEnumerable<ValidationFinding> CheckCriticalLanguage(Report r, RulebookSpec.RuleSpec rule)
    {
        var text = r.Impression + "\n" + r.Recommendations;
        var communicationPattern = @"\b(communicated|notified|documented|paged|called|discussed|reported\s+to|critical\s+result)\b";
        var hasCritical = Regex.IsMatch(text,
            @"\bcritical\b|\bBI[-\s]?RADS\b\s*:?\s*(?:category\s*:?\s*)?(4C|5|6)\b|\bLung[-\s]?RADS\b\s*:?\s*(?:category\s*:?\s*)?(4B|4X)\b|\b(?:LI[-\s]?RADS\b\s*:?\s*(?:category\s*:?\s*)?(?:LR[-\s]?)?(5|M|TIV)|LR[-\s]?(5|M|TIV))\b|\b(tension pneumothorax|free intraperitoneal air|free air|malpositioned\s+(?:endotracheal|ett|ng|central venous|cvc|chest)\s*(?:tube|line|catheter)?)\b",
            RegexOptions.IgnoreCase)
            || (Regex.IsMatch(text, @"\bPI[-\s]?RADS\s*5\b(?s:.*)\b(extraprostatic extension|seminal vesicle invasion)\b", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(text, @"\b(no|without)\s+(extraprostatic extension|seminal vesicle invasion)\b", RegexOptions.IgnoreCase));
        if (hasCritical && !Regex.IsMatch(text, communicationPattern, RegexOptions.IgnoreCase))
        {
            yield return new ValidationFinding(
                RuleId: rule.Id,
                Severity: rule.Severity,
                Message: "Critical finding lacks approved escalation/communication language.",
                Section: "Impression");
        }
    }

    private static IEnumerable<ValidationFinding> RequirePattern(
        string text,
        Regex pattern,
        RulebookSpec.RuleSpec rule,
        string section,
        string message)
    {
        if (pattern.IsMatch(text ?? string.Empty)) yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: message,
            Section: section);
    }

    private static IEnumerable<ValidationFinding> RequireAssertedPattern(
        string text,
        Regex pattern,
        RulebookSpec.RuleSpec rule,
        string section,
        string message)
    {
        if (HasAssertedMatch(text, pattern)) yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: message,
            Section: section);
    }

    private static bool HasAssertedMatch(string text, Regex pattern)
    {
        var clauses = Regex.Split(text ?? string.Empty, @"(?<=[.;\n])\s+|\n+");
        foreach (var clause in clauses)
        {
            foreach (Match match in pattern.Matches(clause))
            {
                var prefix = clause[..match.Index];
                if (Regex.IsMatch(prefix, @"\b(no|without|negative for|absence of|no evidence of)\b[^.;\n]{0,80}$", RegexOptions.IgnoreCase))
                    continue;
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<ValidationFinding> CheckBiRadsAssessmentLine(Report r, RulebookSpec.RuleSpec rule)
    {
        if (!BiRadsCategoryRe.IsMatch(r.Impression ?? string.Empty)) yield break;
        if (BiRadsCategoryLineRe.IsMatch(r.Impression ?? string.Empty)) yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "Place the BI-RADS assessment on its own impression line or bullet.",
            Section: "Impression");
    }

    private static IEnumerable<ValidationFinding> CheckNoduleDimensions(Report r, RulebookSpec.RuleSpec rule)
    {
        var text = r.Findings + "\n" + r.Impression;
        if (!HasAssertedTerm(text, @"nodules?")) yield break;
        if (EveryAssertedTermClauseHasMeasurement(text, @"nodules?")) yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "Add nodule dimensions for measurable nodules.",
            Section: "Findings");
    }

    private static IEnumerable<ValidationFinding> CheckObservationSize(Report r, RulebookSpec.RuleSpec rule)
    {
        var text = r.Findings + "\n" + r.Impression;
        if (!HasAssertedTerm(text, @"observation|lesion|mass")) yield break;
        if (EveryAssertedTermClauseHasMeasurement(text, @"observation|lesion|mass")) yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "Add size for each LI-RADS observation.",
            Section: "Findings");
    }

    private static bool HasAssertedTerm(string text, string termPattern)
    {
        var clauses = Regex.Split(text ?? string.Empty, @"(?<=[.;\n])\s+|\n+");
        foreach (var clause in clauses)
        {
            foreach (Match term in Regex.Matches(clause, $@"\b(?:{termPattern})\b", RegexOptions.IgnoreCase))
            {
                var prefix = clause[..term.Index];
                if (Regex.IsMatch(prefix, @"\b(no|without|negative for|absence of)\b[^.;\n]{0,80}$", RegexOptions.IgnoreCase))
                    continue;
                return true;
            }
        }
        return false;
    }

    private static bool EveryAssertedTermClauseHasMeasurement(string text, string termPattern)
    {
        var foundAssertedClause = false;
        var clauses = Regex.Split(text ?? string.Empty, @"(?<=[.;\n])\s+|\n+");
        foreach (var clause in clauses)
        {
            var hasAssertedTerm = false;
            foreach (Match term in Regex.Matches(clause, $@"\b(?:{termPattern})\b", RegexOptions.IgnoreCase))
            {
                var prefix = clause[..term.Index];
                if (Regex.IsMatch(prefix, @"\b(no|without|negative for|absence of)\b[^.;\n]{0,80}$", RegexOptions.IgnoreCase))
                    continue;
                hasAssertedTerm = true;
                foundAssertedClause = true;
            }
            if (hasAssertedTerm && !MeasurementRe.IsMatch(clause)) return false;
        }
        return foundAssertedClause;
    }

    private static IEnumerable<ValidationFinding> CheckIndexLesionLocalization(Report r, RulebookSpec.RuleSpec rule)
    {
        var text = r.Findings + "\n" + r.Impression;
        if (!Regex.IsMatch(text, @"\b(index\s+)?lesion\b", RegexOptions.IgnoreCase)) yield break;
        var localized = Regex.IsMatch(text, @"\b(peripheral|transition|central|anterior|posterior|apex|base|mid\s*gland|left|right|sector|zone|clock)\b", RegexOptions.IgnoreCase);
        if (localized) yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "Localize the index lesion by prostate zone/sector or anatomic position.",
            Section: "Findings");
    }

    /// <summary>
    /// Iter-32 AI-008 — every Recommendations sentence must match one of the
    /// rulebook-authored <see cref="RulebookSpec.StyleSpec.ApprovedFollowups"/>.
    /// Comparison is case- and whitespace-insensitive. When the allow-list is
    /// empty the rule is a no-op (back-compat for legacy rulebooks).
    /// </summary>
    private static IEnumerable<ValidationFinding> CheckUnauthorizedFollowup(
        Report r, RulebookSpec rb, RulebookSpec.RuleSpec rule)
    {
        var allow = rb.Style.ApprovedFollowups;
        if (allow is null || allow.Count == 0) yield break;
        if (string.IsNullOrWhiteSpace(r.Recommendations)) yield break;

        static string Norm(string s)
        {
            var value = Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ").TrimEnd('.', ';');
            return Regex.Replace(value, @"^recommend\s+", "");
        }
        var allowed = new HashSet<string>(allow.Select(Norm), StringComparer.Ordinal);

        var sentences = Regex.Split(r.Recommendations, @"(?<=[.;\n])\s+|\n+");
        foreach (var raw in sentences)
        {
            var sentence = raw?.Trim();
            if (string.IsNullOrEmpty(sentence)) continue;
            // Strip leading list markers ("1. ", "- ", "* ").
            var stripped = Regex.Replace(sentence, @"^\s*(\d+\.\s*|-\s*|\*\s*)", "");
            var key = Norm(stripped);
            if (key.Length == 0) continue;
            if (key.Contains("no follow-up", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Regex.IsMatch(key, @"\b(recommend|follow-up|imaging|biopsy|consult|consultation)\b", RegexOptions.IgnoreCase)
                && !allowed.Contains(key)) continue;
            if (!allowed.Contains(key))
            {
                yield return new ValidationFinding(
                    RuleId: rule.Id,
                    Severity: rule.Severity,
                    Message: "Follow-up phrase is not on the rulebook's approved list.",
                    Section: "Recommendations",
                    Snippet: stripped.Length > 120 ? stripped[..120] + "…" : stripped);
            }
        }
    }

    private static IEnumerable<ValidationFinding> CheckIncidentalFindings(Report r, RulebookSpec.RuleSpec rule)
    {
        if (!Regex.IsMatch(r.Findings ?? string.Empty, @"\bincidental\b", RegexOptions.IgnoreCase))
            yield break;
        if (Regex.IsMatch(r.Impression ?? string.Empty, @"\bincidental\b", RegexOptions.IgnoreCase))
            yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "Incidental findings mentioned in Findings but not addressed in Impression.",
            Section: "Impression");
    }

    private static IEnumerable<ValidationFinding> CheckRequiredOrganCoverage(Report r, RulebookSpec.RuleSpec rule)
    {
        var organs = new[] { "liver", "gallbladder", "kidney", "pancreas", "spleen", "aorta" };
        var findings = r.Findings ?? string.Empty;
        foreach (var organ in organs)
        {
            // "kidneys" should match "kidney" etc.
            if (Regex.IsMatch(findings, $@"\b{Regex.Escape(organ)}s?\b", RegexOptions.IgnoreCase))
                continue;
            yield return new ValidationFinding(
                RuleId: rule.Id,
                Severity: rule.Severity,
                Message: $"Required organ '{organ}' is not mentioned in Findings.",
                Section: "Findings",
                Snippet: organ);
        }
    }

    private static IEnumerable<ValidationFinding> CheckMidlineShiftMeasured(Report r, RulebookSpec.RuleSpec rule)
    {
        var clauses = Regex.Split(r.Findings ?? string.Empty, @"(?<=[.;\n])\s+|\n+");
        foreach (var clause in clauses)
        {
            if (!Regex.IsMatch(clause, @"\bmidline\s+shift\b", RegexOptions.IgnoreCase))
                continue;
            if (IsNegatedMention(clause, "midline shift"))
                continue;
            if (MeasurementRe.IsMatch(clause))
                continue;
            yield return new ValidationFinding(
                RuleId: rule.Id,
                Severity: rule.Severity,
                Message: "Midline shift is mentioned without a measurement.",
                Section: "Findings",
                Snippet: "midline shift");
            yield break;
        }
    }

    private static IEnumerable<ValidationFinding> CheckMeniscusTearPattern(Report r, RulebookSpec.RuleSpec rule)
    {
        var findings = r.Findings ?? string.Empty;
        if (!Regex.IsMatch(findings, @"\bmenisc(?:us|al)\s+tear\b", RegexOptions.IgnoreCase))
            yield break;
        if (Regex.IsMatch(findings, @"\b(horizontal|vertical|complex|radial|bucket[-\s]?handle|flap|root)\b", RegexOptions.IgnoreCase))
            yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "Describe the meniscus tear pattern (e.g., horizontal, vertical, complex, radial, bucket-handle, flap, root).",
            Section: "Findings");
    }

    private static IEnumerable<ValidationFinding> CheckAclPclStatus(Report r, RulebookSpec.RuleSpec rule)
    {
        var findings = r.Findings ?? string.Empty;
        var hasAcl = Regex.IsMatch(findings, @"\b(ACL|anterior\s+cruciate\s+ligament)\b", RegexOptions.IgnoreCase);
        var hasPcl = Regex.IsMatch(findings, @"\b(PCL|posterior\s+cruciate\s+ligament)\b", RegexOptions.IgnoreCase);
        if (hasAcl && hasPcl)
            yield break;
        if (!hasAcl)
        {
            yield return new ValidationFinding(
                RuleId: rule.Id,
                Severity: rule.Severity,
                Message: "ACL (anterior cruciate ligament) status is not documented in Findings.",
                Section: "Findings");
        }
        if (!hasPcl)
        {
            yield return new ValidationFinding(
                RuleId: rule.Id,
                Severity: rule.Severity,
                Message: "PCL (posterior cruciate ligament) status is not documented in Findings.",
                Section: "Findings");
        }
    }

    private static IEnumerable<ValidationFinding> CheckPriorComparison(Report r, RulebookSpec.RuleSpec rule)
    {
        if (!string.IsNullOrWhiteSpace(r.Comparison))
            yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "Comparison section is empty; prior comparison is required for screening exams.",
            Section: "Comparison");
    }

    private static IEnumerable<ValidationFinding> CheckNoduleBiaxialMeasurement(Report r, RulebookSpec.RuleSpec rule)
    {
        var text = r.Findings + "\n" + r.Impression;
        if (!HasAssertedTerm(text, @"nodules?"))
            yield break;
        var clauses = Regex.Split(text, @"(?<=[.;\n])\s+|\n+");
        foreach (var clause in clauses)
        {
            if (!Regex.IsMatch(clause, @"\bnodules?\b", RegexOptions.IgnoreCase))
                continue;
            if (BiaxialMeasurementRe.IsMatch(clause))
                continue;
            yield return new ValidationFinding(
                RuleId: rule.Id,
                Severity: rule.Severity,
                Message: "Provide biaxial (2D) nodule measurements (e.g., 5 x 3 mm).",
                Section: "Findings");
            yield break;
        }
    }

    private static IEnumerable<ValidationFinding> CheckFractureAcuity(Report r, RulebookSpec.RuleSpec rule)
    {
        var findings = r.Findings ?? string.Empty;
        if (!HasAssertedTerm(findings, @"fracture"))
            yield break;
        if (Regex.IsMatch(findings, @"\b(acute|chronic|healing|old|recent|subacute)\b", RegexOptions.IgnoreCase))
            yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "Describe fracture acuity (e.g., acute, chronic, healing, old, recent, subacute).",
            Section: "Findings");
    }

    private static IEnumerable<ValidationFinding> CheckFigoStaging(Report r, RulebookSpec.RuleSpec rule)
    {
        var indication = r.Indication ?? string.Empty;
        if (!Regex.IsMatch(indication, @"\b(cancer|malignancy|staging|carcinoma|neoplasm)\b", RegexOptions.IgnoreCase))
            yield break;
        if (FigoStageRe.IsMatch(r.Impression ?? string.Empty))
            yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "Oncologic indication present but FIGO staging is not documented in Impression.",
            Section: "Impression");
    }

    private static IEnumerable<ValidationFinding> CheckRotatorCuffThickness(Report r, RulebookSpec.RuleSpec rule)
    {
        var findings = r.Findings ?? string.Empty;
        if (!Regex.IsMatch(findings, @"\brotator\s+cuff\b", RegexOptions.IgnoreCase))
            yield break;
        if (!Regex.IsMatch(findings, @"\btear\b", RegexOptions.IgnoreCase))
            yield break;
        if (Regex.IsMatch(findings, @"\b(partial|full[-\s]?thickness|intrasubstance|bursal|articular)\b", RegexOptions.IgnoreCase))
            yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "Describe the rotator cuff tear type (e.g., partial, full-thickness, intrasubstance, bursal, articular).",
            Section: "Findings");
    }

    private static IEnumerable<ValidationFinding> CheckLabrumDescribed(Report r, RulebookSpec.RuleSpec rule)
    {
        var findings = r.Findings ?? string.Empty;
        if (Regex.IsMatch(findings, @"\b(labrum|labral)\b", RegexOptions.IgnoreCase))
            yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "The glenoid labrum should be explicitly addressed in Findings.",
            Section: "Findings");
    }

    private static IEnumerable<ValidationFinding> CheckFollowUpLanguageApproved(Report r, RulebookSpec.RuleSpec rule)
    {
        var impression = r.Impression ?? string.Empty;
        var recommendations = r.Recommendations ?? string.Empty;

        // Only fire when TI-RADS >= 3
        var tiRadsMatch = Regex.Match(impression, @"\b(?:TI[-\s]?RADS\b\s*:?\s*(?:category\s*:?\s*)?(?:TR)?|TR)([1-5])\b", RegexOptions.IgnoreCase);
        if (!tiRadsMatch.Success)
            yield break;
        if (int.TryParse(tiRadsMatch.Groups[1].Value, out var category) && category < 3)
            yield break;

        if (FollowUpApprovedRe.IsMatch(recommendations))
            yield break;
        yield return new ValidationFinding(
            RuleId: rule.Id,
            Severity: rule.Severity,
            Message: "TI-RADS >= 3 requires approved follow-up language in Recommendations (e.g., FNA, fine needle aspiration, follow-up).",
            Section: "Recommendations");
    }
}
