namespace BusinessLicensing_Practice.Services;

public static class Paging
{
    public const int PageSize = 10;

    public static int TotalPages(int totalCount) =>
        Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));

    public static int ClampPage(int page, int totalCount) =>
        Math.Clamp(page, 1, TotalPages(totalCount));
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page)
{
    public int PageSize => Paging.PageSize;
    public int TotalPages => Paging.TotalPages(TotalCount);
}
