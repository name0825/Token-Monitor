namespace TokenMonitor.Providers.Cli;

public sealed class TerminalScreen
{
    private readonly int _rows;
    private readonly int _columns;
    private readonly char[,] _grid;
    private int _row;
    private int _col;
    private int _savedRow;
    private int _savedCol;
    private string _pending = string.Empty;

    public TerminalScreen(int columns = 120, int rows = 40)
    {
        _columns = columns;
        _rows = rows;
        _grid = new char[rows, columns];
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < columns; c++)
            _grid[r, c] = ' ';
    }

    public void Write(string text)
    {
        var s = _pending + text;
        _pending = string.Empty;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '')
            {
                var consumed = ProcessEscape(s, i, out var needMore);
                if (needMore)
                {
                    _pending = s.Substring(i);
                    return;
                }
                i += consumed;
                continue;
            }

            ProcessChar(c);
            i++;
        }
    }

    public string Render()
    {
        var lines = new string[_rows];
        for (var r = 0; r < _rows; r++)
        {
            var chars = new char[_columns];
            for (var c = 0; c < _columns; c++)
                chars[c] = _grid[r, c];
            lines[r] = new string(chars).TrimEnd(' ');
        }

        var lastNonBlank = _rows - 1;
        while (lastNonBlank >= 0 && lines[lastNonBlank].Length == 0)
            lastNonBlank--;

        return lastNonBlank < 0 ? string.Empty : string.Join("\n", lines, 0, lastNonBlank + 1);
    }

    private void ProcessChar(char c)
    {
        switch (c)
        {
            case '\r':
                _col = 0;
                return;
            case '\n':
                LineFeed();
                return;
            case '\b':
                _col = Math.Max(0, _col - 1);
                return;
            case '\t':
                _col = Math.Min(_columns - 1, (_col / 8 + 1) * 8);
                return;
        }

        if (c < 0x20)
            return;

        if (_col >= _columns)
        {
            _col = 0;
            LineFeed();
        }

        _grid[_row, _col] = c;
        _col++;
    }

    private void LineFeed()
    {
        if (_row == _rows - 1)
            ScrollUp(1);
        else
            _row++;
    }

    private int ProcessEscape(string s, int i, out bool needMore)
    {
        needMore = false;
        if (i + 1 >= s.Length)
        {
            needMore = true;
            return 0;
        }

        var c1 = s[i + 1];
        switch (c1)
        {
            case '[':
                return ProcessCsi(s, i, out needMore);
            case ']':
                return ProcessStringSequence(s, i, allowBel: true, out needMore);
            case 'P':
            case 'X':
            case '^':
            case '_':
                return ProcessStringSequence(s, i, allowBel: false, out needMore);
            case '7':
                _savedRow = _row;
                _savedCol = _col;
                return 2;
            case '8':
                _row = Clamp(_savedRow, 0, _rows - 1);
                _col = Clamp(_savedCol, 0, _columns - 1);
                return 2;
            case '(':
            case ')':
            case '*':
            case '+':
            case '-':
            case '.':
            case '/':
                if (i + 2 >= s.Length)
                {
                    needMore = true;
                    return 0;
                }
                return 3;
            default:
                return 2;
        }
    }

    private int ProcessCsi(string s, int i, out bool needMore)
    {
        needMore = false;
        var j = i + 2;
        while (j < s.Length && s[j] < '@')
            j++;

        if (j >= s.Length)
        {
            needMore = true;
            return 0;
        }

        var finalChar = s[j];
        var paramsPart = s.Substring(i + 2, j - (i + 2)).Replace("?", string.Empty);
        var paramsList = ParseParams(paramsPart);
        ApplyCsi(paramsList, finalChar);
        return j - i + 1;
    }

    private int ProcessStringSequence(string s, int i, bool allowBel, out bool needMore)
    {
        needMore = false;
        var k = i + 2;
        while (k < s.Length)
        {
            if (allowBel && s[k] == '')
                return k - i + 1;

            if (s[k] == '')
            {
                if (k + 1 >= s.Length)
                {
                    needMore = true;
                    return 0;
                }

                if (s[k + 1] == '\\')
                    return k - i + 2;
            }

            k++;
        }

        needMore = true;
        return 0;
    }

    private static List<int> ParseParams(string paramsPart)
    {
        var result = new List<int>();
        if (paramsPart.Length == 0)
            return result;

        foreach (var part in paramsPart.Split(';'))
        {
            result.Add(int.TryParse(part, out var value) ? value : 0);
        }

        return result;
    }

    private static int GetParam(List<int> list, int index, int defaultValue)
    {
        return index < list.Count && list[index] > 0 ? list[index] : defaultValue;
    }

    private void ApplyCsi(List<int> paramsList, char finalChar)
    {
        switch (finalChar)
        {
            case 'A':
                _row = Clamp(_row - GetParam(paramsList, 0, 1), 0, _rows - 1);
                break;
            case 'B':
                _row = Clamp(_row + GetParam(paramsList, 0, 1), 0, _rows - 1);
                break;
            case 'C':
                _col = Clamp(_col + GetParam(paramsList, 0, 1), 0, _columns - 1);
                break;
            case 'D':
                _col = Clamp(_col - GetParam(paramsList, 0, 1), 0, _columns - 1);
                break;
            case 'E':
                _row = Clamp(_row + GetParam(paramsList, 0, 1), 0, _rows - 1);
                _col = 0;
                break;
            case 'F':
                _row = Clamp(_row - GetParam(paramsList, 0, 1), 0, _rows - 1);
                _col = 0;
                break;
            case 'G':
            case '`':
                _col = Clamp(GetParam(paramsList, 0, 1) - 1, 0, _columns - 1);
                break;
            case 'd':
                _row = Clamp(GetParam(paramsList, 0, 1) - 1, 0, _rows - 1);
                break;
            case 'H':
            case 'f':
                _row = Clamp(GetParam(paramsList, 0, 1) - 1, 0, _rows - 1);
                _col = Clamp(GetParam(paramsList, 1, 1) - 1, 0, _columns - 1);
                break;
            case 'J':
                ApplyEraseDisplay(GetParam(paramsList, 0, 0));
                break;
            case 'K':
                ApplyEraseLine(GetParam(paramsList, 0, 0));
                break;
            case 'L':
                InsertLines(GetParam(paramsList, 0, 1));
                break;
            case 'M':
                DeleteLines(GetParam(paramsList, 0, 1));
                break;
            case 'P':
                DeleteChars(GetParam(paramsList, 0, 1));
                break;
            case '@':
                InsertChars(GetParam(paramsList, 0, 1));
                break;
            case 'X':
                EraseChars(GetParam(paramsList, 0, 1));
                break;
            case 'S':
                ScrollUp(GetParam(paramsList, 0, 1));
                break;
            case 'T':
                ScrollDown(GetParam(paramsList, 0, 1));
                break;
        }
    }

    private void ApplyEraseDisplay(int mode)
    {
        if (mode == 0)
        {
            ClearRowRange(_row, _col, _columns);
            for (var r = _row + 1; r < _rows; r++)
                ClearRow(r);
        }
        else if (mode == 1)
        {
            for (var r = 0; r < _row; r++)
                ClearRow(r);
            ClearRowRange(_row, 0, _col + 1);
        }
        else
        {
            for (var r = 0; r < _rows; r++)
                ClearRow(r);
        }
    }

    private void ApplyEraseLine(int mode)
    {
        if (mode == 0)
            ClearRowRange(_row, _col, _columns);
        else if (mode == 1)
            ClearRowRange(_row, 0, _col + 1);
        else
            ClearRowRange(_row, 0, _columns);
    }

    private void InsertLines(int n)
    {
        n = Math.Min(n, _rows - _row);
        for (var r = _rows - 1; r >= _row + n; r--)
            CopyRow(r - n, r);
        for (var r = _row; r < _row + n; r++)
            ClearRow(r);
    }

    private void DeleteLines(int n)
    {
        n = Math.Min(n, _rows - _row);
        for (var r = _row; r < _rows - n; r++)
            CopyRow(r + n, r);
        for (var r = _rows - n; r < _rows; r++)
            ClearRow(r);
    }

    private void DeleteChars(int n)
    {
        n = Math.Min(n, _columns - _col);
        for (var c = _col; c < _columns - n; c++)
            _grid[_row, c] = _grid[_row, c + n];
        for (var c = _columns - n; c < _columns; c++)
            _grid[_row, c] = ' ';
    }

    private void InsertChars(int n)
    {
        n = Math.Min(n, _columns - _col);
        for (var c = _columns - 1; c >= _col + n; c--)
            _grid[_row, c] = _grid[_row, c - n];
        for (var c = _col; c < _col + n; c++)
            _grid[_row, c] = ' ';
    }

    private void EraseChars(int n)
    {
        n = Math.Min(n, _columns - _col);
        for (var c = _col; c < _col + n; c++)
            _grid[_row, c] = ' ';
    }

    private void ScrollUp(int n)
    {
        n = Math.Min(n, _rows);
        for (var r = 0; r < _rows - n; r++)
            CopyRow(r + n, r);
        for (var r = _rows - n; r < _rows; r++)
            ClearRow(r);
    }

    private void ScrollDown(int n)
    {
        n = Math.Min(n, _rows);
        for (var r = _rows - 1; r >= n; r--)
            CopyRow(r - n, r);
        for (var r = 0; r < n; r++)
            ClearRow(r);
    }

    private void CopyRow(int src, int dst)
    {
        for (var c = 0; c < _columns; c++)
            _grid[dst, c] = _grid[src, c];
    }

    private void ClearRow(int r)
    {
        ClearRowRange(r, 0, _columns);
    }

    private void ClearRowRange(int r, int startCol, int endColExclusive)
    {
        startCol = Clamp(startCol, 0, _columns);
        endColExclusive = Clamp(endColExclusive, 0, _columns);
        for (var c = startCol; c < endColExclusive; c++)
            _grid[r, c] = ' ';
    }

    private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
}
