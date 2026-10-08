using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HocLieu.Common;

/// <summary>ProblemDetails (RFC 9457): title tiếng Việt cho người dùng, code máy đọc (spec §8.4).</summary>
public static class ApiErrors
{
    public static IResult NotFound(string title) => Problem(HttpStatusCode.NotFound, "not_found", title);

    public static IResult Forbidden(string title) => Problem(HttpStatusCode.Forbidden, "forbidden", title);

    public static IResult Problem(HttpStatusCode status, string code, string title)
    {
        var pd = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Type = $"https://hoclieu.dev/errors/{code}",
        };
        if (code.Length > 0)
            pd.Extensions["code"] = code;
        return Results.Problem(detail: null, title: pd.Title, statusCode: (int?)status, type: pd.Type, instance: null, extensions: pd.Extensions);
    }

    public static IResult Validation(Dictionary<string, string[]> errors, string title = "Dữ liệu không hợp lệ")
    {
        var pd = new ProblemDetails
        {
            Status = 422,
            Title = title,
            Type = "https://hoclieu.dev/errors/validation",
        };
        pd.Extensions["errors"] = errors; // spec §8.4: errors cho validation (key camelCase)
        pd.Extensions["code"] = "validation_error";
        return Results.Problem(detail: null, title: title, statusCode: 422, type: pd.Type, instance: null, extensions: pd.Extensions);
    }
}

/// <summary>Các mã lỗi máy đọc dùng chung.</summary>
public static class ErrorCodes
{
    public const string NotVisible = "content.not_visible";
    public const string NotTeacher = "auth.not_teacher";
    public const string PendingAccount = "user.pending";
    public const string SuspendedAccount = "user.suspended";
    public const string RejectedAccount = "user.rejected";
    public const string TeamNotLead = "team.not_lead";
    public const string TeamNotManage = "team.not_manage";
    public const string ClassNoAccess = "class.no_access";
    public const string Conflict = "conflict";
    public const string QuizHasAttempts = "quiz.has_attempts";
    public const string FileInvalid = "file.invalid";
    public const string RateLimited = "rate_limited";
}
