namespace Sanity.Linq.Enums;

/// <summary>
/// Specifies how Sanity should handle draft and published document versions in query results.
/// </summary>
public enum SanityPerspective
{
    /// <summary>
    /// Returns all documents as-is, including both draft versions (with "drafts." prefix)
    /// and published versions as separate documents. Use this when you need to see
    /// the raw state of all documents in the dataset.
    /// </summary>
    Raw = 0,

    /// <summary>
    /// Returns only published documents. Draft documents are excluded from results.
    /// This is the traditional default behavior of the Sanity API.
    /// </summary>
    Published = 1,

    /// <summary>
    /// Returns draft documents when available, falling back to published versions.
    /// Useful for preview functionality where you want to see unpublished changes.
    /// </summary>
    PreviewDrafts = 2
}
