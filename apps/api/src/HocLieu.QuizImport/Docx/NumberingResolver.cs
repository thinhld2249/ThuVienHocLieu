using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HocLieu.QuizImport.Docx;

/// <summary>
/// Đánh số tự động (numbering.xml, spec §6.3.4): giữ bộ đếm theo (numId, ilvl),
/// sinh nhãn từ numFmt + lvlText và chèn vào đầu text để tầng regex xử lý như văn bản gõ tay.
/// Hỗ trợ decimal / decimalZero / upperLetter / lowerLetter (A. a) 1.); bullet và format lạ → không gán nhãn.
/// </summary>
public sealed class NumberingResolver
{
    private sealed record LevelDef(string? NumFmt, string? LvlText);

    private readonly Dictionary<int, Dictionary<int, LevelDef>> _abstracts = new();
    private readonly Dictionary<int, int> _numToAbstract = new();
    private readonly Dictionary<(int numId, int ilvl), int> _counters = new();

    public void Load(NumberingDefinitionsPart? part)
    {
        var numbering = part?.Numbering;
        if (numbering is null)
            return;

        foreach (var abstractNum in numbering.Elements<AbstractNum>())
        {
            var idEl = abstractNum.AbstractNumberId;
            if (idEl is null)
                continue;
            var abstractId = idEl.Value;
            var levels = new Dictionary<int, LevelDef>();
            foreach (var lvl in abstractNum.Elements<Level>())
            {
                var ilvlEl = lvl.LevelIndex;
                var ilvl = ilvlEl is null ? 0 : ilvlEl.Value;
                var fmtEl = lvl.NumberingFormat?.Format;
                var textEl = lvl.LevelText?.Val;
                levels[ilvl] = new LevelDef(fmtEl?.Value, textEl?.Value);
            }
            _abstracts[abstractId] = levels;
        }

        foreach (var num in numbering.Elements<NumberingInstance>())
        {
            var numIdEl = num.NumberID;
            var absRef = num.AbstractNumId;
            var absIdEl = absRef?.Val;
            if (numIdEl is null || absIdEl is null)
                continue;
            var numId = numIdEl.Value;
            var abstractId = absIdEl.Value;
            if (_abstracts.ContainsKey(abstractId))
                _numToAbstract[numId] = abstractId;
        }
    }

    /// <summary>Trả nhãn (vd "A. ", "1. ") cho đoạn đánh số, hoặc null nếu không phải danh sách chữ/số.</summary>
    public string? NextLabel(int numId, int ilvl)
    {
        if (!_numToAbstract.TryGetValue(numId, out var abstractId) || !_abstracts.TryGetValue(abstractId, out var levels))
            return null;
        if (!levels.TryGetValue(ilvl, out var lvl) || lvl.NumFmt is null || lvl.LvlText is null)
            return null;
        if (lvl.NumFmt is not ("decimal" or "decimalZero" or "upperLetter" or "lowerLetter"))
            return null;

        // Cấp mới → reset các cấp sâu hơn
        var toRemove = new List<(int numId, int ilvl)>();
        foreach (var k in _counters.Keys)
            if (k.numId == numId && k.ilvl > ilvl)
                toRemove.Add(k);
        foreach (var k in toRemove)
            _counters.Remove(k);

        var key = (numId, ilvl);
        _counters[key] = _counters.TryGetValue(key, out var cur) ? cur + 1 : 1;

        var label = lvl.LvlText;
        for (var level = 0; level <= ilvl; level++)
        {
            var token = "%" + (level + 1);
            if (!label.Contains(token, StringComparison.Ordinal))
                continue;
            var value = level == ilvl ? _counters[key] : 1;
            label = label.Replace(token, FormatValue(value, lvl.NumFmt), StringComparison.Ordinal);
        }
        // Các token cấp sâu hơn (hiếm) → 1
        for (var level = ilvl + 1; level <= 8; level++)
            label = label.Replace("%" + (level + 1), "1", StringComparison.Ordinal);
        return label.TrimEnd() + " ";
    }

    private static string FormatValue(int value, string numFmt) => numFmt switch
    {
        "decimal" or "decimalZero" => value.ToString(),
        "upperLetter" => value > 0 ? ((char)('A' + (value - 1))).ToString() : string.Empty,
        "lowerLetter" => value > 0 ? ((char)('a' + (value - 1))).ToString() : string.Empty,
        _ => value.ToString(),
    };
}
