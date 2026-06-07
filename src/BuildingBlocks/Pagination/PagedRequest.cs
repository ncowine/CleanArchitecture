namespace BuildingBlocks.Pagination;

/// <summary>
/// Base shape for paged list requests carried in the body. House convention: list reads are POSTs with
/// paging (and any filters) in the body — keeping URLs clean. Endpoints derive from this to add filters.
/// </summary>
public record PagedRequest(int Page = 1, int PageSize = 20);
