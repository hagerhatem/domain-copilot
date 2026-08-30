namespace DomainCopilot.Evaluation.Pipeline;

/// <summary>
/// A deliberately crude in-memory "index": filename -> short text blurb.
/// This exists only so StubRetrievalPipeline has something to score against
/// before Qdrant + SQL Server full-text search are wired up. Blurbs below
/// are limited to content actually reviewed while building the golden set;
/// documents without a real blurb fall back to tokenizing the filename,
/// which will retrieve poorly and is expected to - that gap is real and
/// should show up in the harness report, not be papered over.
///
/// DELETE THIS FILE once the real hybrid retrieval pipeline exists.
/// </summary>
public static class StubCorpusIndex
{
    public static readonly IReadOnlyDictionary<string, string> Blurbs = new Dictionary<string, string>
    {
        ["nice_type2-diabetes-management_2015-12_v1.pdf"] =
            "Type 2 diabetes in adults management NG28 initial medicines heart failure atherosclerotic " +
            "cardiovascular disease early onset obesity chronic kidney disease frailty reviewing metformin " +
            "self monitoring blood glucose insulin",
        ["nice_diabetes-medicines-summary_2026-03_v1.pdf"] =
            "Type 2 diabetes medicines recommendations summary modified release metformin SGLT-2 inhibitor " +
            "GLP-1 receptor agonist tirzepatide DPP-4 inhibitor sulfonylurea pioglitazone insulin heart failure " +
            "atherosclerotic cardiovascular disease contraindicated",
        ["nice_chronic-heart-failure-management_2018-09_v1.pdf"] =
            "Chronic heart failure adults diagnosis management NG106 reduced ejection fraction preserved " +
            "ejection fraction starting monitoring medication chronic kidney disease cardiac rehabilitation",
        ["idsa_uti-treatment-women_2010-03_v1.pdf"] =
            "Acute uncomplicated cystitis pyelonephritis women nitrofurantoin trimethoprim sulfamethoxazole " +
            "fluoroquinolone resistance beta-lactam antimicrobial treatment",
        ["ispad_pediatric-diabetes-guideline_2022-12_v1.pdf"] =
            "ISPAD type 2 diabetes children adolescents youth-onset screening diagnosis education team " +
            "pediatric endocrinologist nutritionist psychologist exercise physiologist pharmacologic therapies",
        ["fda_metformin-label_v1.pdf"] =
            "Metformin hydrochloride extended release lactic acidosis renal impairment contraindications " +
            "dosage administration vitamin B12 hepatic impairment",
        ["fda_coumadin-warfarin-label_2016-06_v1.pdf"] =
            "Coumadin warfarin sodium bleeding risk INR monitoring pregnancy mechanical heart valve drug " +
            "interactions antibiotics cytochrome CYP2C9",
        ["who_hypertension-guideline_2021-08_v1.pdf"] =
            "WHO guideline pharmacological treatment hypertension adults",
    };

    /// <summary>Fallback for any document not in Blurbs: derive a weak pseudo-blurb from the filename itself.</summary>
    public static string BlurbOrFallback(string filename) =>
        Blurbs.TryGetValue(filename, out var blurb)
            ? blurb
            : Path.GetFileNameWithoutExtension(filename).Replace('_', ' ').Replace('-', ' ');
}
