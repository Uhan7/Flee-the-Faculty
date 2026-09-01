using System;
using System.Collections.Generic;

public static class DialogueTextPaginator
{
    private const int PreferredWordsPerPage = 20;
    private const int MaximumWordsPerPage = 25;
    private const int MinimumUsefulRemainder = 10;

    public static IReadOnlyList<string> Split(string text)
    {
        List<string> pages = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return pages;
        }

        string[] words = text.Split(
            (char[])null,
            StringSplitOptions.RemoveEmptyEntries);
        int start = 0;

        while (start < words.Length)
        {
            int remaining = words.Length - start;
            if (remaining <= MaximumWordsPerPage)
            {
                pages.Add(string.Join(" ", words, start, remaining));
                break;
            }

            int pageWords = FindNaturalBreak(words, start, remaining);
            pages.Add(string.Join(" ", words, start, pageWords));
            start += pageWords;
        }

        return pages;
    }

    private static int FindNaturalBreak(string[] words, int start, int remaining)
    {
        int preferred = PreferredWordsPerPage;
        if (remaining - preferred < MinimumUsefulRemainder)
        {
            preferred = remaining - MinimumUsefulRemainder;
        }

        preferred = Math.Max(1, Math.Min(preferred, MaximumWordsPerPage));
        int bestBreak = -1;
        int bestDistance = int.MaxValue;

        for (int count = 1; count <= MaximumWordsPerPage; count++)
        {
            if (!EndsSentence(words[start + count - 1]))
            {
                continue;
            }

            int wordsAfterBreak = remaining - count;
            if (wordsAfterBreak > 0 && wordsAfterBreak < MinimumUsefulRemainder)
            {
                continue;
            }

            int distance = Math.Abs(count - preferred);
            if (distance < bestDistance)
            {
                bestBreak = count;
                bestDistance = distance;
            }
        }

        return bestBreak > 0 ? bestBreak : preferred;
    }

    private static bool EndsSentence(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return false;
        }

        char finalCharacter = word[word.Length - 1];
        return finalCharacter == '.'
            || finalCharacter == '!'
            || finalCharacter == '?'
            || finalCharacter == ';'
            || finalCharacter == ':';
    }
}
