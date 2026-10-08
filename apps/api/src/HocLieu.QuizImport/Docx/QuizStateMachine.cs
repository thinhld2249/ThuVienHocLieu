using System.Text;
using System.Text.RegularExpressions;
using HocLieu.QuizImport.Model;

namespace HocLieu.QuizImport.Docx;

/// <summary>
/// Máy trạng thái dựng QuizDraft từ các Line đã phân loại (spec §6.3.6–6.3.13):
/// Preamble → mô tả; GroupHeader/GroupIntro → nhóm; QuestionStart → Stem → Options → Explanation;
/// AnswerKeyHeader → khối đáp án đến hết file. Xác định đáp án đúng theo thứ tự nguồn
/// Asterisk &gt; AnswerLine &gt; AnswerKey &gt; Formatting (§6.3.8).
/// </summary>
public static partial class QuizStateMachine
{
    private enum State { Preamble, GroupIntro, Stem, Options, Explanation, AnswerKey }

    private sealed class OptionBuilder
    {
        public string Label = string.Empty;
        public List<Line> Lines { get; } = [];
        public bool Asterisk { get; set; }
    }

    private sealed class QuestionBuilder
    {
        public int Number;
        public string? GroupTempId;
        public List<Line> StemLines { get; } = [];
        public List<OptionBuilder> Options { get; } = [];
        public List<Line> ExplanationLines { get; } = [];
        public HashSet<string> AsteriskLabels { get; } = [];
        public HashSet<string>? AnswerLineLabels { get; set; }
    }

    [GeneratedRegex(@"(\d{1,3})\s*[.\-:)]?\s*([A-H]{1,8}|Đúng|Sai|Đ|S)\b", RegexOptions.IgnoreCase)]
    private static partial Regex KeyScanRx();

    public static QuizDraft Build(List<Line> lines, List<DraftImage> images)
    {
        var draft = new QuizDraft();
        var warnings = draft.Warnings;
        var groups = new List<DraftGroup>();
        var groupPassages = new Dictionary<string, List<Line>>();
        string? currentGroup = null;

        State state = State.Preamble;
        var preamble = new List<Line>();
        var current = new QuestionBuilder();
        var keyEntries = new Dictionary<int, HashSet<string>>();

        void NewGroup(string? title)
        {
            currentGroup = $"g{groups.Count + 1}";
            groups.Add(new DraftGroup { TempId = currentGroup, Title = title });
            groupPassages[currentGroup] = [];
        }

        void StartQuestion(int number, Line line, string rest)
        {
            FinishCurrentQuestion();
            current = new QuestionBuilder { Number = number, GroupTempId = currentGroup };
            var stem = StripPrefix(line, rest);
            if (stem.Text.Trim().Length > 0 || stem.Images.Count > 0)
                current.StemLines.Add(stem);
            state = State.Stem;
        }

        void FinishCurrentQuestion()
        {
            if (current.Number == 0 || current.StemLines.Count == 0)
            {
                current = new QuestionBuilder();
                return;
            }
            FinalizeQuestion(current, warnings, draft);
            current = new QuestionBuilder();
        }

        // Gom bảng đáp án 2 hàng: "Câu | 1 | 2 …" (hàng trên) + "Đáp án | C | B …" (hàng dưới)
        void CollectKeyTables()
        {
            var tableRows = lines
                .Where(l => l.InTable && l.TableIndex is not null && l.RowIndex is not null)
                .GroupBy(l => (l.TableIndex!.Value, l.RowIndex!.Value))
                .Select(g => g.OrderBy(l => l.CellIndex ?? 0).Select(l => l.Text.Trim()).ToList())
                .ToList();
            for (var i = 0; i + 1 < tableRows.Count; i++)
            {
                var row = tableRows[i];
                if (row.Count < 2 || !string.Equals(row[0], "Câu", StringComparison.OrdinalIgnoreCase))
                    continue;
                var answerRow = tableRows[i + 1];
                if (!string.Equals(answerRow[0], "Đáp án", StringComparison.OrdinalIgnoreCase))
                    continue;
                for (var col = 1; col < row.Count && col < answerRow.Count; col++)
                {
                    if (int.TryParse(row[col], out var num) && ParseAnswerLetters(answerRow[col], out var labels) && labels.Count > 0)
                        keyEntries[num] = labels;
                }
            }
        }

        foreach (var rawLine in lines)
        {
            foreach (var img in rawLine.Images.Where(i => i.UnsupportedFormat))
                warnings.Add(new ImportWarning(WarningCodes.UnsupportedImageFormat, current.Number,
                    "Có ảnh không hiển thị được (thường là công thức MathType WMF/EMF) — hãy thay bằng ảnh PNG."));

            if (rawLine.EquationSimplified)
                warnings.Add(new ImportWarning(WarningCodes.EquationSimplified, current.Number,
                    "Có công thức toán phức tạp đã được chuyển thành văn bản gần đúng — kiểm tra lại trước khi phát hành."));

            var line = LineClassifier.Classify(rawLine);

            switch (state)
            {
                case State.AnswerKey:
                    ScanKeyLine(line, keyEntries);
                    break;

                case State.Preamble:
                    switch (line.Kind)
                    {
                        case LineKind.GroupHeader:
                            NewGroup(line.GroupTitle);
                            state = State.GroupIntro;
                            break;
                        case LineKind.AnswerKeyHeader:
                            state = State.AnswerKey;
                            ScanKeyLine(line, keyEntries);
                            break;
                        case LineKind.QuestionStart:
                            StartQuestion(line.QuestionNumber!.Value, rawLine, line.QuestionText);
                            break;
                        default:
                            preamble.Add(rawLine); // Text / GroupIntro / Option… ở đầu bài → mô tả
                            break;
                    }
                    break;

                case State.GroupIntro:
                    switch (line.Kind)
                    {
                        case LineKind.GroupHeader:
                            NewGroup(line.GroupTitle);
                            break;
                        case LineKind.QuestionStart:
                            StartQuestion(line.QuestionNumber!.Value, rawLine, line.QuestionText);
                            break;
                        case LineKind.AnswerKeyHeader:
                            state = State.AnswerKey;
                            ScanKeyLine(line, keyEntries);
                            break;
                        default:
                            groupPassages[currentGroup!].Add(rawLine); // passage
                            break;
                    }
                    break;

                case State.Stem:
                    switch (line.Kind)
                    {
                        case LineKind.QuestionStart:
                            StartQuestion(line.QuestionNumber!.Value, rawLine, line.QuestionText);
                            break;
                        case LineKind.Option:
                            foreach (var part in line.Options)
                                AddOption(current, part, rawLine, line.LeadingWhitespace);
                            state = State.Options;
                            break;
                        case LineKind.TrueFalseOption:
                            AddOption(current, line.Options[0], rawLine, line.LeadingWhitespace);
                            state = State.Options;
                            break;
                        case LineKind.AnswerLine:
                            current.AnswerLineLabels = ParseAnswerLetters(line.AnswerText, out var al) ? al : null;
                            break;
                        case LineKind.GroupHeader:
                            FinishCurrentQuestion();
                            NewGroup(line.GroupTitle);
                            state = State.GroupIntro;
                            break;
                        case LineKind.GroupIntro:
                            FinishCurrentQuestion();
                            NewGroup(null);
                            state = State.GroupIntro;
                            break;
                        case LineKind.AnswerKeyHeader:
                            state = State.AnswerKey;
                            ScanKeyLine(line, keyEntries);
                            break;
                        default:
                            current.StemLines.Add(rawLine);
                            break;
                    }
                    break;

                case State.Options:
                    switch (line.Kind)
                    {
                        case LineKind.QuestionStart:
                            StartQuestion(line.QuestionNumber!.Value, rawLine, line.QuestionText);
                            break;
                        case LineKind.Option:
                            foreach (var part in line.Options)
                                AddOption(current, part, rawLine, line.LeadingWhitespace);
                            break;
                        case LineKind.TrueFalseOption:
                            AddOption(current, line.Options[0], rawLine, line.LeadingWhitespace);
                            break;
                        case LineKind.AnswerLine:
                            current.AnswerLineLabels = ParseAnswerLetters(line.AnswerText, out var al2) ? al2 : null;
                            break;
                        case LineKind.ExplanationStart:
                            state = State.Explanation;
                            if (line.ExplanationText is { Length: > 0 })
                                current.ExplanationLines.Add(CloneForText(rawLine, line.ExplanationText));
                            break;
                        case LineKind.GroupHeader:
                            FinishCurrentQuestion();
                            NewGroup(line.GroupTitle);
                            state = State.GroupIntro;
                            break;
                        case LineKind.GroupIntro:
                            FinishCurrentQuestion();
                            NewGroup(null);
                            state = State.GroupIntro;
                            break;
                        case LineKind.AnswerKeyHeader:
                            state = State.AnswerKey;
                            ScanKeyLine(line, keyEntries);
                            break;
                        default:
                            // Text nối tiếp → nối vào phương án cuối (nếu dòng không rỗng)
                            if (rawLine.Text.Trim().Length > 0 || rawLine.Images.Count > 0)
                                if (current.Options.Count > 0)
                                    current.Options[^1].Lines.Add(rawLine);
                            break;
                    }
                    break;

                case State.Explanation:
                    switch (line.Kind)
                    {
                        case LineKind.QuestionStart:
                            StartQuestion(line.QuestionNumber!.Value, rawLine, line.QuestionText);
                            break;
                        case LineKind.GroupHeader:
                            FinishCurrentQuestion();
                            NewGroup(line.GroupTitle);
                            state = State.GroupIntro;
                            break;
                        case LineKind.GroupIntro:
                            FinishCurrentQuestion();
                            NewGroup(null);
                            state = State.GroupIntro;
                            break;
                        case LineKind.AnswerKeyHeader:
                            state = State.AnswerKey;
                            ScanKeyLine(line, keyEntries);
                            break;
                        default:
                            if (rawLine.Text.Trim().Length > 0 || rawLine.Images.Count > 0)
                                current.ExplanationLines.Add(rawLine);
                            break;
                    }
                    break;
            }
        }

        FinishCurrentQuestion();
        CollectKeyTables();

        // Gắn đáp án từ khối key (xử lý cuối vì khối key nằm cuối file)
        foreach (var (num, labels) in keyEntries)
        {
            var q = draft.Questions.FirstOrDefault(x => x.Number == num);
            if (q is null)
            {
                warnings.Add(new ImportWarning(WarningCodes.AnswerKeyUnmatched, num,
                    $"Khối đáp án có câu {num} nhưng bài không có câu số {num}."));
                continue;
            }
            ApplyKeyToQuestion(q, labels, warnings);
        }

        // Title + mô tả từ preamble
        var firstText = preamble.FirstOrDefault(l => l.Text.Trim().Length > 0);
        draft.Title = firstText is null ? string.Empty
            : firstText.Text.Trim().Split('\n').FirstOrDefault(t => t.Trim().Length > 0)?.Trim() ?? string.Empty;
        draft.DescriptionHtml = string.Join("",
            preamble.Where(l => l.Text.Trim().Length > 0 || l.Images.Count > 0)
                    .Select(l => "<p>" + LineToInlineHtml(l) + "</p>"));

        // Passage của từng nhóm
        foreach (var g in groups)
        {
            g.PassageHtml = groupPassages.TryGetValue(g.TempId, out var pl) && pl.Count > 0
                ? string.Join("", pl.Select(l => "<p>" + LineToInlineHtml(l) + "</p>"))
                : null;
        }

        // Số câu: trùng / nhảy số
        var seen = new HashSet<int>();
        int? last = null;
        foreach (var n in draft.Questions.Select(q => q.Number ?? 0))
        {
            if (!seen.Add(n))
                warnings.Add(new ImportWarning(WarningCodes.DuplicateQuestionNumber, n,
                    $"Câu {n} xuất hiện nhiều lần."));
            else if (last is { } prev && n > prev + 1)
                warnings.Add(new ImportWarning(WarningCodes.QuestionNumberGap, n,
                    $"Bố trí nhảy số: thiếu câu từ {prev + 1} đến {n - 1}."));
            last = n;
        }

        draft.Groups = groups;
        draft.Images = images;
        return draft;
    }

    private static void AddOption(QuestionBuilder q, OptionPart part, Line line, int leading)
    {
        if (part.Asterisk)
            q.AsteriskLabels.Add(NormalizeLabel(part.Label));
        var existing = q.Options.FirstOrDefault(o => o.Label == part.Label);
        if (existing is null)
        {
            existing = new OptionBuilder { Label = part.Label, Asterisk = part.Asterisk };
            q.Options.Add(existing);
        }
        else
            existing.Asterisk |= part.Asterisk;
        if (part.Rest.Length > 0)
            existing.Lines.Add(CloneForText(line, part.Rest, part.Start + leading));
        else if (part.Label is "Đúng" or "Sai")
        {
            // Phương án Đúng/Sai không có nội dung — hiển thị chính nhãn
            var tfLine = new Line();
            tfLine.Runs.Add(new Run { Text = part.Label });
            existing.Lines.Add(tfLine);
        }
    }

    private static string NormalizeLabel(string label)
    {
        var l = label.Trim();
        if (string.Equals(l, "Đúng", StringComparison.OrdinalIgnoreCase))
            return "Đúng";
        if (string.Equals(l, "Sai", StringComparison.OrdinalIgnoreCase))
            return "Sai";
        if (l.Length == 1)
            return char.ToUpperInvariant(l[0]).ToString();
        return l;
    }

    private static bool ParseAnswerLetters(string? text, out HashSet<string> labels)
    {
        labels = [];
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var t = text.Trim().TrimEnd('.');
        if (string.Equals(t, "Đúng", StringComparison.OrdinalIgnoreCase))
        {
            labels.Add("Đúng");
            return true;
        }
        if (string.Equals(t, "Sai", StringComparison.OrdinalIgnoreCase))
        {
            labels.Add("Sai");
            return true;
        }
        foreach (var ch in t)
        {
            var up = char.ToUpperInvariant(ch);
            if (up >= 'A' && up <= 'H')
                labels.Add(up.ToString());
        }
        return labels.Count > 0;
    }

    private static void ScanKeyLine(ClassifiedLine line, Dictionary<int, HashSet<string>> keyEntries)
    {
        // Dạng "Câu 1: B" trong khối key
        if (line.Kind == LineKind.QuestionStart
            && line.QuestionNumber is { } num
            && ParseAnswerLetters(line.QuestionText, out var ql)
            && ql.All(l => l.Length == 1 || l is "Đúng" or "Sai"))
        {
            keyEntries[num] = ql;
            return;
        }
        // Dạng text "5.C 6.B 3.AC" / "1-C; 2-B" / "1.C 2.B 3.AC"
        foreach (Match m in KeyScanRx().Matches(line.Text))
        {
            if (!int.TryParse(m.Groups[1].Value, out var num2))
                continue;
            var raw = m.Groups[2].Value;
            var labels = new HashSet<string>();
            if (string.Equals(raw, "Đ", StringComparison.OrdinalIgnoreCase))
                labels.Add("Đúng");
            else if (string.Equals(raw, "S", StringComparison.OrdinalIgnoreCase))
                labels.Add("Sai");
            else
                foreach (var ch in raw)
                    labels.Add(char.ToUpperInvariant(ch).ToString());
            keyEntries[num2] = labels;
        }
    }

    private static void FinalizeQuestion(QuestionBuilder q, List<ImportWarning> warnings, QuizDraft draft)
    {
        var labels = q.Options.Select(o => NormalizeLabel(o.Label)).ToList();
        var isTrueFalse = labels.Count >= 1 && labels.All(l => l is "Đúng" or "Sai");

        // Cảnh báo phương án
        foreach (var o in q.Options)
        {
            var text = string.Concat(o.Lines.Select(l => l.Text));
            if (text.Trim().Length == 0 && o.Lines.All(l => l.Images.Count == 0))
                warnings.Add(new ImportWarning(WarningCodes.EmptyOption, q.Number,
                    $"Câu {q.Number}: phương án {o.Label} trống."));
        }
        if (labels.Count != labels.Distinct().Count())
            warnings.Add(new ImportWarning(WarningCodes.DuplicateOptionLabel, q.Number,
                $"Câu {q.Number}: có phương án trùng chữ cái."));
        if (labels.Count < 2)
            warnings.Add(new ImportWarning(WarningCodes.TooFewOptions, q.Number,
                $"Câu {q.Number}: chỉ có {labels.Count} phương án (cần tối thiểu 2)."));

        // Nguồn đáp án theo thứ tự ưu tiên (spec §6.3.8) — AnswerKey gắn ở bước sau (khối key cuối file)
        var asterisk = q.AsteriskLabels.Count > 0 ? new HashSet<string>(q.AsteriskLabels) : null;
        var answerLine = q.AnswerLineLabels is { Count: > 0 } ? q.AnswerLineLabels : null;
        var formatting = DetectFormatting(q);

        string? winnerName = AnswerSources.None;
        HashSet<string>? winner = null;
        foreach (var (name, lbls) in new (string, HashSet<string>?)[]
        {
            (AnswerSources.Asterisk, asterisk),
            (AnswerSources.AnswerLine, answerLine),
            (AnswerSources.Formatting, formatting),
        })
        {
            if (lbls is null || lbls.Count == 0)
                continue;
            if (winner is null)
            {
                winnerName = name;
                winner = lbls;
            }
            else if (!winner.SetEquals(lbls))
            {
                warnings.Add(new ImportWarning(WarningCodes.ConflictingAnswerSources, q.Number,
                    $"Câu {q.Number}: các cách đánh dấu đáp án cho kết quả khác nhau — kiểm tra lại."));
                break;
            }
        }

        if (winner is null || winner.Count == 0)
            warnings.Add(new ImportWarning(WarningCodes.NoCorrectAnswer, q.Number,
                $"Câu {q.Number} chưa xác định được đáp án đúng."));

        var draftQ = new DraftQuestion
        {
            Number = q.Number,
            GroupTempId = q.GroupTempId,
            ContentHtml = q.StemLines.Count > 0
                ? string.Join("", q.StemLines.Select(l => "<p>" + LineToInlineHtml(l) + "</p>"))
                : string.Empty,
            ExplanationHtml = q.ExplanationLines.Count > 0
                ? string.Join("", q.ExplanationLines.Select(l => "<p>" + LineToInlineHtml(l) + "</p>"))
                : null,
            Points = 1m,
            AnswerSource = winnerName,
        };

        foreach (var o in q.Options)
        {
            var label = NormalizeLabel(o.Label);
            draftQ.Options.Add(new DraftOption
            {
                Label = label,
                ContentHtml = string.Join("<br/>", o.Lines.Select(LineToInlineHtml)),
                IsCorrect = winner is not null && winner.Contains(label),
            });
        }

        var correctCount = draftQ.Options.Count(o => o.IsCorrect);
        draftQ.Type = isTrueFalse ? QuestionType.TrueFalse
            : correctCount > 1 ? QuestionType.Multi : QuestionType.Single;

        draft.Questions.Add(draftQ);
    }

    /// <summary>
    /// Gắn đáp án từ khối key: key thắng Formatting (ưu tiên thấp hơn);
    /// key khác Asterisk/AnswerLine → cảnh báo xung đột, giữ nguồn ưu tiên cao.
    /// </summary>
    private static void ApplyKeyToQuestion(DraftQuestion q, HashSet<string> keyLabels, List<ImportWarning> warnings)
    {
        var currentCorrect = q.Options.Where(o => o.IsCorrect).Select(o => o.Label).ToHashSet();
        if (currentCorrect.Count == 0)
        {
            foreach (var o in q.Options)
                o.IsCorrect = keyLabels.Contains(o.Label);
            if (q.Options.Any(o => o.IsCorrect))
            {
                // Key giải quyết được đáp án → gỡ cảnh báo NO_CORRECT_ANSWER (thêm ở FinalizeQuestion,
                // chạy trước khối key cuối file) và tính lại loại câu (key có thể cho nhiều đáp án đúng)
                q.AnswerSource = AnswerSources.AnswerKey;
                warnings.RemoveAll(w => w.Code == WarningCodes.NoCorrectAnswer && w.QuestionNumber == q.Number);
                var correctCount = q.Options.Count(o => o.IsCorrect);
                q.Type = q.Type == QuestionType.TrueFalse ? QuestionType.TrueFalse
                    : correctCount > 1 ? QuestionType.Multi : QuestionType.Single;
            }
            return;
        }
        if (q.AnswerSource == AnswerSources.Formatting && !currentCorrect.SetEquals(keyLabels))
        {
            // Key ưu tiên cao hơn formatting → key thắng
            foreach (var o in q.Options)
                o.IsCorrect = keyLabels.Contains(o.Label);
            q.AnswerSource = AnswerSources.AnswerKey;
            return;
        }
        if (!currentCorrect.SetEquals(keyLabels))
            warnings.Add(new ImportWarning(WarningCodes.ConflictingAnswerSources, q.Number,
                $"Câu {q.Number}: khối đáp án khác với đánh dấu trong bài — kiểm tra lại."));
    }

    private static HashSet<string>? DetectFormatting(QuestionBuilder q)
    {
        var result = new HashSet<string>();
        var boldCandidates = q.Options.Where(o => IsUniform(o, f => f.Bold)).ToList();
        foreach (var o in q.Options)
        {
            var isBoldOnly = boldCandidates.Count == 1 && boldCandidates[0] == o;
            if (IsUniform(o, f => f.Underline) || IsUniform(o, f => f.Red) || IsUniform(o, f => f.Highlight) || isBoldOnly)
                result.Add(NormalizeLabel(o.Label));
        }
        return result.Count > 0 ? result : null;
    }

    private static bool IsUniform(OptionBuilder o, Func<(bool Underline, bool Red, bool Highlight, bool Bold), bool> pred)
    {
        var all = o.Lines.SelectMany(l => l.Runs).Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToList();
        if (all.Count == 0)
            return false;
        return pred((
            all.All(r => r.Underline),
            all.All(r => r.ColorHex is { } c && Line.ParseColor(c, out var rr, out var g, out var b) && rr >= 180 && g <= 90 && b <= 90),
            all.All(r => r.Highlight is not null),
            all.All(r => r.Bold)));
    }

    // ========== HTML từ Line ==========

    /// <summary>Dòng → HTML inline (escape, tag định dạng, img data-temp-id, &lt;br/&gt;).</summary>
    public static string LineToInlineHtml(Line line)
    {
        var sb = new StringBuilder();
        foreach (var run in line.Runs)
        {
            var text = run.Raw ? run.Text : EscapeHtml(run.Text);
            var open = new List<string>();
            if (run.Bold)
                open.Add("<strong>");
            if (run.Italic)
                open.Add("<em>");
            if (run.Underline)
                open.Add("<u>");
            if (run.Subscript)
                open.Add("<sub>");
            if (run.Superscript)
                open.Add("<sup>");
            for (var i = 0; i < open.Count; i++)
                sb.Append(open[i]);
            sb.Append(text);
            for (var i = open.Count - 1; i >= 0; i--)
                sb.Append(CloseTag(open[i]));
        }
        foreach (var img in line.Images)
            sb.Append(img.UnsupportedFormat
                ? "<span class=\"hl-img-warn\">[Ảnh chưa hiển thị được — hãy thay bằng ảnh PNG]</span>"
                : $"<img data-temp-id=\"{img.TempId}\" alt=\"\" />");
        return sb.ToString();
    }

    private static string EscapeHtml(string s)
    {
        var sb = new StringBuilder(s.Length);
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

    private static string CloseTag(string open) => open switch
    {
        "<strong>" => "</strong>",
        "<em>" => "</em>",
        "<u>" => "</u>",
        "<sub>" => "</sub>",
        "<sup>" => "</sup>",
        _ => string.Empty,
    };

    /// <summary>Copy line, thay nội dung text bằng một phần (cho phương án tách từ dòng chung) — giữ định dạng + ảnh.</summary>
    private static Line CloneForText(Line source, string text) => CloneForText(source, text, 0);

    /// <summary>
    /// Như <see cref="CloneForText(Line, string)"/>, nhưng định dạng lấy từ run đầu tiên tại/vượt
    /// vị trí <paramref name="start"/> (vị trí nội dung phương án) — để "B. " thường + " 5,7" gạch chân
    /// vẫn nhận diện được định dạng của nội dung (spec §6.3.8).
    /// </summary>
    private static Line CloneForText(Line source, string text, int start)
    {
        var line = new Line
        {
            InTable = source.InTable,
            TableIndex = source.TableIndex,
            RowIndex = source.RowIndex,
            CellIndex = source.CellIndex,
            EquationSimplified = source.EquationSimplified,
        };
        foreach (var img in source.Images)
            line.Images.Add(img);
        var fmt = new Run();
        var consumed = 0;
        var found = false;
        foreach (var r in source.Runs)
        {
            var len = r.Text.Length;
            if (!found && consumed + len > start && !string.IsNullOrWhiteSpace(r.Text))
            {
                fmt = r;
                found = true;
            }
            if (found)
                break;
            consumed += len;
        }
        if (!found)
            fmt = source.Runs.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Text)) ?? new Run();
        if (text.Length > 0)
            line.Runs.Add(new Run
            {
                Text = text,
                Bold = fmt.Bold,
                Italic = fmt.Italic,
                Underline = fmt.Underline,
                Superscript = fmt.Superscript,
                Subscript = fmt.Subscript,
                ColorHex = fmt.ColorHex,
                Highlight = fmt.Highlight,
            });
        return line;
    }

    /// <summary>
    /// Bỏ prefix "Câu n:" khỏi đầu line (giữ phần còn lại + ảnh) để contentHtml chỉ chứa đề.
    /// </summary>
    private static Line StripPrefix(Line source, string rest)
    {
        var line = new Line
        {
            InTable = source.InTable,
            TableIndex = source.TableIndex,
            RowIndex = source.RowIndex,
            CellIndex = source.CellIndex,
            EquationSimplified = source.EquationSimplified,
        };
        foreach (var img in source.Images)
            line.Images.Add(img);

        // rest là phần còn lại sau prefix trong text đã trim → phải tính trên text trim
        // (kể cả dòng có khoảng trắng đầu/cuối, vd "…đúng: " + ảnh)
        var text = source.Text;
        var lead = text.Length - text.TrimStart().Length;
        var trimmedLen = text.Trim().Length;
        var prefixLen = rest.Length > 0
            ? Math.Max(0, trimmedLen - rest.Length)
            : trimmedLen; // "Câu 1" không có phần sau → bỏ cả nhãn
        var skip = lead + prefixLen;
        foreach (var run in source.Runs)
        {
            var runText = run.Text;
            if (skip > 0)
            {
                if (runText.Length <= skip)
                {
                    skip -= runText.Length;
                    continue;
                }
                runText = runText[skip..];
                skip = 0;
            }
            if (runText.Length > 0)
            {
                line.Runs.Add(new Run
                {
                    Text = runText,
                    Raw = run.Raw,
                    Bold = run.Bold,
                    Italic = run.Italic,
                    Underline = run.Underline,
                    Superscript = run.Superscript,
                    Subscript = run.Subscript,
                    ColorHex = run.ColorHex,
                    Highlight = run.Highlight,
                });
            }
        }
        return line;
    }
}
