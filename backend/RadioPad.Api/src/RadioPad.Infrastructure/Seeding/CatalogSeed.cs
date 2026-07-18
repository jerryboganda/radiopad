using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Infrastructure.Seeding;

/// <summary>
/// Iter-36 — single source of truth for the default admin-managed Modality and
/// BodyPart catalogs. These were previously hardcoded in the frontend
/// (<c>MetadataPanel.tsx</c>); they are now tenant-scoped DB rows that drive
/// report-template and rulebook (prompt) resolution.
///
/// Seeding every org through this helper — at registration/bootstrap and via a
/// one-time startup backfill for pre-existing orgs — mirrors <see cref="UbagPrimarySeed"/>
/// so the defaults never drift across call sites. Note: the seed re-runs at every
/// process start, so a hard-deleted default modality/body-part row is resurrected
/// on the next restart. To hide a default row permanently, clear its Active flag
/// instead of deleting it.
/// </summary>
public static class CatalogSeed
{
    /// <summary>Default imaging modalities (code, display name).</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> DefaultModalities = new[]
    {
        ("CT", "Computed Tomography"),
        ("MR", "Magnetic Resonance"),
        ("US", "Ultrasound"),
        ("XR", "Radiography (X-ray)"),
        ("NM", "Nuclear Medicine"),
        ("PET", "Positron Emission Tomography"),
        ("MG", "Mammography"),
        ("FL", "Fluoroscopy"),
    };

    /// <summary>
    /// Default anatomical body parts (code == display name). These codes are the
    /// canonical resolution-key vocabulary: report-template <c>BodyPart</c> and
    /// rulebook <c>applies_to.body_parts</c> must match one of these verbatim
    /// (case-insensitive). The original 8 generic regions stay first so their
    /// historical SortOrder is preserved; the granular clinical regions that back
    /// the curated exam library are appended.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultBodyParts = new[]
    {
        // Original generic regions (kept first — preserves their SortOrder).
        "Head", "Neck", "Chest", "Abdomen", "Pelvis", "Spine", "Extremity", "Whole Body",

        // Neuro
        "Brain", "Pituitary", "Orbits", "Internal Auditory Canal", "Temporal Bones",
        // Head & Neck
        "Paranasal Sinuses", "Facial Bones", "Nasopharynx", "Larynx",
        "Salivary Glands", "Thyroid", "Temporomandibular Joints",
        // Spine
        "Cervical Spine", "Thoracic Spine", "Lumbar Spine", "Whole Spine",
        "Sacrum & Coccyx", "Sacroiliac Joints",
        // Chest / Cardiac / Breast
        "Cardiac", "Coronary Arteries", "Pulmonary Arteries", "Thoracic Aorta", "Breast",
        // Abdomen
        "Liver", "Pancreas", "Biliary System", "Kidneys", "Adrenals",
        "Abdominal Aorta", "Abdomen & Pelvis",
        // Pelvis / GU
        "Female Pelvis", "Prostate", "Rectum", "Bladder", "Scrotum",
        // GI / GU specials
        "Small Bowel", "Urinary Tract", "KUB",
        // MSK upper limb
        "Shoulder", "Humerus", "Elbow", "Forearm", "Wrist", "Hand",
        // MSK lower limb
        "Hip", "Femur", "Knee", "Tibia & Fibula", "Ankle", "Foot", "Bony Pelvis",
        // Vascular
        "Carotid Arteries", "Intracranial Arteries", "Renal Arteries", "Peripheral Runoff",
        // Whole body / Obstetric / Paediatric
        "Obstetric", "Neonatal Head",
    };

    /// <summary>
    /// Idempotently ensures the default Modality + BodyPart rows exist for a tenant.
    /// Only inserts codes that are absent (case-insensitive); never overwrites or
    /// re-activates an operator's customised/removed rows. Saves its own changes;
    /// returns the number of rows inserted.
    /// </summary>
    public static async Task<int> EnsureCatalogAsync(RadioPadDbContext db, Guid tenantId, CancellationToken ct)
    {
        var added = 0;

        var haveModalities = new HashSet<string>(
            await db.Modalities.Where(m => m.TenantId == tenantId).Select(m => m.Code).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < DefaultModalities.Count; i++)
        {
            var (code, name) = DefaultModalities[i];
            if (haveModalities.Contains(code)) continue;
            db.Modalities.Add(new Modality { TenantId = tenantId, Code = code, Name = name, SortOrder = i });
            added++;
        }

        var haveBodyParts = new HashSet<string>(
            await db.BodyParts.Where(b => b.TenantId == tenantId).Select(b => b.Code).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < DefaultBodyParts.Count; i++)
        {
            var code = DefaultBodyParts[i];
            if (haveBodyParts.Contains(code)) continue;
            db.BodyParts.Add(new BodyPart { TenantId = tenantId, Code = code, Name = code, SortOrder = i });
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }
}
