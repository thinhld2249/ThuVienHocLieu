namespace HocLieu.Domain.Entities;

public class Grade
{
    public short Id { get; set; }
    public string Name { get; set; } = default!;
    public short Sort { get; set; }
}

public class Subject
{
    public long Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public short Sort { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Section
{
    public long Id { get; set; }
    public string Slug { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public short Sort { get; set; }
    public SectionContentKind ContentKind { get; set; } = SectionContentKind.Document;
    public PublishMode DefaultPublishMode { get; set; } = PublishMode.Visible;
    public ContentScope DefaultScope { get; set; } = ContentScope.Public;
    public bool RequireWeek { get; set; }
    public bool IsInternal { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SchoolYear
{
    public long Id { get; set; }
    public string Name { get; set; } = default!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
}

public class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
}
