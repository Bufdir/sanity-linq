// ReSharper disable InconsistentNaming
namespace Sanity.Linq.Mutations;

public static class MutationQuerySettings
{
    /// <summary>
    ///  Most likely, nesting is never applicable for mutation queries
    /// </summary>
    public const int MAX_NESTING_LEVEL = 2;
}