namespace HocLieu.Features.Files;

public record FileDto(
    long Id,
    string OriginalName,
    string Ext,
    long Bytes,
    string ProcessingStatus,
    string? ProcessingError,
    int? PreviewPages,
    DateTimeOffset CreatedAt);

public record FilePageDto(int Number, string Url, string Kind);

public record FilePagesDto(long FileId, IReadOnlyList<FilePageDto> Pages);

public static class FileDtos
{
    public static FileDto Of(Domain.Entities.FileEntity f) => new(
        f.Id, f.OriginalName, f.Ext, f.Bytes,
        f.ProcessingStatus.ToString(), f.ProcessingError, f.PreviewPages, f.CreatedAt);
}
