namespace Parsing;

public static class StringSplitter
{
    public static ReadOnlyMemory<ReadOnlyMemory<string>> SplitMultiThreadedMany(
        this ReadOnlyMemory<string> inputs,
        char delimiter, char? escape = null)
    {
        Memory<ReadOnlyMemory<string>> tasks = new ReadOnlyMemory<string>[inputs.Length];
        Span<ReadOnlyMemory<string>> tasksSpan = tasks.Span;
        ReadOnlySpan<string> inputsSpan = inputs.Span;
        for (var i = 0; i < inputsSpan.Length; i++)
        {
            var stringInput = inputsSpan[i];
            tasksSpan[i] = QuickSplit(stringInput, delimiter, escape);
        }

        return tasks;
    }

    public static ReadOnlyMemory<string> QuickSplit(this string input, char delimiter,
        char? escape = null)
    {
        Memory<int> findIndicesTask = FindIndices(input, delimiter);
        Memory<int> findEscapesTask = FindIndices(input, escape ?? '\0');

        Span<int> foundDelimiters = findIndicesTask.Span;
        Span<int> foundEscapes = findEscapesTask.Span;

        if (foundEscapes.Length > 0 && foundEscapes.Length % 2 == 0)
            return FromIndicesAndEscapes(input, foundDelimiters, foundEscapes);
        if (foundEscapes.Length % 2 != 0)
            throw new InvalidOperationException("Missing closing escape character");
        return FromIndices(input, foundDelimiters);
    }

    private static ReadOnlyMemory<string> FromIndicesAndEscapes(string source, Span<int> foundDelimiters,
        Span<int> foundEscapes)
    {
        Memory<string> resultMem = new string[foundDelimiters.Length + 1];
        Span<string> result = resultMem.Span;

        var escapeIndex = 0;
        var previousIndex = 0;
        var splitIndex = 0;

        foreach (var delim in foundDelimiters)
        {
            if (escapeIndex < foundEscapes.Length)
            {
                for (; escapeIndex < foundEscapes.Length; escapeIndex++)
                    if (delim < foundEscapes[escapeIndex])
                        break;
            }
            else
                escapeIndex = -1;

            if (escapeIndex >= 0 && escapeIndex % 2 == 1) continue;

            if (!string.IsNullOrEmpty(source[previousIndex..delim]))
                result[splitIndex++] = source[previousIndex..delim].Replace("\"", "");
            previousIndex = delim + 1;
        }

        if (!string.IsNullOrEmpty(source[previousIndex..]))
            result[splitIndex++] = source[previousIndex..].Replace("\"", "");
        return resultMem[..splitIndex];
    }

    private static ReadOnlyMemory<string> FromIndices(string source, Span<int> foundDelimiters)
    {
        Memory<string> resultMem = new string[foundDelimiters.Length + 1];
        Span<string> result = resultMem.Span;


        var previousIndex = 0;
        var splitIndex = 0;

        foreach (var delim in foundDelimiters)
        {
            if (!string.IsNullOrEmpty(source[previousIndex..delim]))
                result[splitIndex++] = source[previousIndex..delim];
            previousIndex = delim + 1;
        }

        if (!string.IsNullOrEmpty(source[previousIndex..]))
            result[splitIndex++] = source[previousIndex..];
        return resultMem[..splitIndex];
    }

    private static Memory<int> FindIndices(ReadOnlySpan<char> input, char delimiter)
    {
        if (input.IsEmpty || delimiter == '\0')
            return Memory<int>.Empty;

        Memory<int> foundIndices = new int[input.Length];
        Span<int> indices = foundIndices.Span;

        int i = 0, pos = 0;
        while (pos < input.Length)
        {
            var relativeIndex = FirstIndexOf(input[pos..], delimiter);
            if (relativeIndex == -1)
                break;

            indices[i++] = pos + relativeIndex;
            pos += relativeIndex + 1;
        }

        return foundIndices[..i];
    }

    public static int FirstIndexOf<T>(this ReadOnlySpan<T> span, T delimiter)
    {
        if (delimiter == null) return -1;

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == null) continue;

            if (span[i]!.Equals(delimiter))
                return i;
        }

        return -1;
    }

    public static int FirstIndexOf<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> delimiters, out T? delimiterFound)
    {
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == null) continue;
            foreach (var delimiter in delimiters)
            {
                if (delimiter == null) continue;
                if (!span[i]!.Equals(delimiter)) continue;

                delimiterFound = delimiter;
                return i;
            }
        }

        delimiterFound = default;
        return -1;
    }
}