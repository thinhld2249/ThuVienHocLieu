using ClosedXML.Excel;
using HocLieu.QuizImport.Model;

namespace HocLieu.QuizImport.Xlsx;

/// <summary>
/// Nhập quiz từ Excel (spec §6.4): sheet đầu, hàng 1 là tiêu đề
/// (STT · Câu hỏi* · A…F ≥ 2 cột · Đáp án* · Giải thích · Nhóm · Đoạn văn · Điểm).
/// Mọi ô đọc bằng chuỗi đã định dạng để "5,7" không bị hiểu thành số.
/// </summary>
public static class XlsxQuizReader
{
    public static QuizDraft Read(Stream stream)
    {
        using var package = new XLWorkbook(stream);
        var ws = package.Worksheets.FirstOrDefault()
            ?? throw new ImportException("File Excel không có sheet nào.");
        if (ws.Row(1).IsEmpty())
            throw new ImportException("File Excel trống.");

        var draft = new QuizDraft();
        var warnings = draft.Warnings;

        // Ánh xạ cột từ hàng tiêu đề
        var colQuestion = -1;
        var colAnswer = -1;
        var colExplanation = -1;
        var colGroup = -1;
        var colPassage = -1;
        var colPoints = -1;
        var colStt = -1;
        var optionCols = new List<(int Col, string Label)>();

        foreach (var cell in ws.Row(1).CellsUsed())
        {
            var header = cell.GetFormattedString().Trim();
            if (header.Length == 0)
                continue;
            var upper = header.ToUpperInvariant();
            if (upper is "STT" or "TT" or "SỐ TT")
                colStt = cell.Address.ColumnNumber;
            else if (upper.Contains("CÂU HỎI", System.StringComparison.OrdinalIgnoreCase) || header.Contains("Câu hỏi", System.StringComparison.OrdinalIgnoreCase))
                colQuestion = cell.Address.ColumnNumber;
            else if (header.Contains("Đáp án", System.StringComparison.OrdinalIgnoreCase))
                colAnswer = cell.Address.ColumnNumber;
            else if (header.Contains("Giải thích", System.StringComparison.OrdinalIgnoreCase) || header.Contains("Lời giải", System.StringComparison.OrdinalIgnoreCase))
                colExplanation = cell.Address.ColumnNumber;
            else if (header.Contains("Nhóm", System.StringComparison.OrdinalIgnoreCase) && !header.Contains("Đáp"))
                colGroup = cell.Address.ColumnNumber;
            else if (header.Contains("Đoạn văn", System.StringComparison.OrdinalIgnoreCase) || header.Contains("Đoạn dẫn", System.StringComparison.OrdinalIgnoreCase))
                colPassage = cell.Address.ColumnNumber;
            else if (upper == "ĐIỂM")
                colPoints = cell.Address.ColumnNumber;
            else if (upper.Length == 1 && char.ToUpperInvariant(upper[0]) >= 'A' && char.ToUpperInvariant(upper[0]) <= 'H')
                optionCols.Add((cell.Address.ColumnNumber, upper[0].ToString()));
        }

        if (colQuestion < 0)
            throw new ImportException("Thiếu cột \"Câu hỏi\" ở hàng tiêu đề.");
        if (optionCols.Count < 2)
            throw new ImportException("Cần ít nhất 2 cột phương án (A, B…) ở hàng tiêu đề.");

        string GroupTempId = null!;
        var groups = new List<DraftGroup>();
        var groupPassage = new Dictionary<string, string>();

        void NewGroup(string title, string passage)
        {
            GroupTempId = $"g{groups.Count + 1}";
            groups.Add(new DraftGroup { TempId = GroupTempId, Title = title });
            groupPassage[GroupTempId] = passage;
        }

        var lastNumber = 0;
        var seenNumbers = new HashSet<int>();
        var autoNumber = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (var r = 2; r <= lastRow; r++)
        {
            var questionText = colQuestion > 0 ? ws.Cell(r, colQuestion).GetFormattedString().Trim() : string.Empty;
            var options = optionCols
                .Select(oc => (oc.Label, Text: ws.Cell(r, oc.Col).GetFormattedString().Trim()))
                .ToList();
            var anyOption = options.Any(o => o.Text.Length > 0);
            if (questionText.Length == 0 && !anyOption)
                continue; // hàng trống

            // Nhóm mới
            var groupText = colGroup > 0 ? ws.Cell(r, colGroup).GetFormattedString().Trim() : string.Empty;
            if (groupText.Length > 0)
            {
                var passage = colPassage > 0 ? ws.Cell(r, colPassage).GetFormattedString().Trim() : string.Empty;
                NewGroup(groupText, passage);
            }

            var number = colStt > 0 && int.TryParse(ws.Cell(r, colStt).GetFormattedString().Trim(), out var stt)
                ? stt
                : ++autoNumber;
            if (!seenNumbers.Add(number))
                warnings.Add(new ImportWarning(WarningCodes.DuplicateQuestionNumber, number, $"Câu {number} xuất hiện nhiều lần."));
            else if (lastNumber > 0 && number > lastNumber + 1)
                warnings.Add(new ImportWarning(WarningCodes.QuestionNumberGap, number, $"Bố trí nhảy số: thiếu câu từ {lastNumber + 1} đến {number - 1}."));
            lastNumber = Math.Max(lastNumber, number);

            // Phương án (bỏ ô trống; cảnh báo nếu quá ít)
            var filled = options.Where(o => o.Text.Length > 0).ToList();
            if (filled.Count < 2)
                warnings.Add(new ImportWarning(WarningCodes.TooFewOptions, number,
                    $"Câu {number}: chỉ có {filled.Count} phương án (cần tối thiểu 2)."));

            // Đáp án
            var answerText = colAnswer > 0 ? ws.Cell(r, colAnswer).GetFormattedString().Trim() : string.Empty;
            var correct = ParseAnswer(answerText);
            if (correct.Count == 0)
                warnings.Add(new ImportWarning(WarningCodes.NoCorrectAnswer, number, $"Câu {number} chưa xác định được đáp án đúng."));

            var isTrueFalse = correct.Count > 0 && correct.All(l => l is "Đúng" or "Sai")
                               && filled.Count(o => o.Text.Length > 0) == 0
                               || (filled.Select(o => NormText(o.Text)).All(t => t is "Đúng" or "Sai") && filled.Count >= 2);

            // Điểm
            var points = 1m;
            if (colPoints > 0)
            {
                var ptsText = ws.Cell(r, colPoints).GetFormattedString().Trim().Replace(',', '.');
                if (decimal.TryParse(ptsText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p) && p > 0)
                    points = p;
            }

            var q = new DraftQuestion
            {
                Number = number,
                GroupTempId = GroupTempId,
                ContentHtml = questionText.Length > 0 ? "<p>" + EscapeHtml(questionText) + "</p>" : string.Empty,
                ExplanationHtml = colExplanation > 0
                    ? ws.Cell(r, colExplanation).GetFormattedString().Trim() is { Length: > 0 } ex
                        ? "<p>" + EscapeHtml(ex) + "</p>"
                        : null
                    : null,
                Points = points,
                AnswerSource = correct.Count > 0 ? AnswerSources.AnswerLine : AnswerSources.None,
            };

            var tfLabels = new[] { "Đúng", "Sai" };
            foreach (var opt in options)
            {
                if (opt.Text.Length == 0)
                {
                    if (colAnswer is > 0 && answerText.Contains(opt.Label, System.StringComparison.OrdinalIgnoreCase))
                        warnings.Add(new ImportWarning(WarningCodes.EmptyOption, number,
                            $"Câu {number}: phương án {opt.Label} trống nhưng được chọn làm đáp án."));
                    continue;
                }
                // Câu Đúng/Sai: phương án là chữ "Đúng"/"Sai"
                var label = opt.Label;
                if (isTrueFalse)
                    label = NormText(opt.Text);
                q.Options.Add(new DraftOption
                {
                    Label = label,
                    ContentHtml = EscapeHtml(opt.Text),
                    IsCorrect = correct.Count > 0 && (correct.Contains(label) || (isTrueFalse && correct.Contains(NormText(opt.Text)))),
                });
            }

            var correctCount = q.Options.Count(o => o.IsCorrect);
            q.Type = isTrueFalse ? QuestionType.TrueFalse
                : correctCount > 1 ? QuestionType.Multi : QuestionType.Single;

            draft.Questions.Add(q);
        }

        foreach (var g in groups)
            g.PassageHtml = groupPassage.TryGetValue(g.TempId, out var p) && p.Length > 0
                ? "<p>" + EscapeHtml(p) + "</p>"
                : null;

        draft.Groups = groups;
        return draft;
    }

    private static HashSet<string> ParseAnswer(string text)
    {
        var labels = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(text))
            return labels;
        var t = text.Trim().TrimEnd('.');
        if (string.Equals(t, "Đúng", System.StringComparison.OrdinalIgnoreCase))
        {
            labels.Add("Đúng");
            return labels;
        }
        if (string.Equals(t, "Sai", System.StringComparison.OrdinalIgnoreCase))
        {
            labels.Add("Sai");
            return labels;
        }
        foreach (var ch in t)
        {
            var up = char.ToUpperInvariant(ch);
            if (up >= 'A' && up <= 'H')
                labels.Add(up.ToString());
        }
        return labels;
    }

    private static string NormText(string s)
        => string.Equals(s.Trim(), "Đúng", System.StringComparison.OrdinalIgnoreCase) ? "Đúng"
        : string.Equals(s.Trim(), "Sai", System.StringComparison.OrdinalIgnoreCase) ? "Sai"
        : s.Trim();

    private static string EscapeHtml(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\n': sb.Append("<br/>"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
