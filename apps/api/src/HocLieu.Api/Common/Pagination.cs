namespace HocLieu.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);

    public static PagedResult<T> Of(IReadOnlyList<T> items, int page, int pageSize)
        => new(items, items.Count, page, pageSize);
}

public static class Pagination
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Parse(int? page, int? pageSize, int defaultPageSize = 24)
    {
        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? defaultPageSize, 1, MaxPageSize);
        return (p, ps);
    }
}
