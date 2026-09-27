using System.Text.RegularExpressions;

namespace Foundrmind.Services;

public record ParsedPost(int Day, string Channel, string Text, string Visual);

/// <summary>
/// Turns a Content Reactor calendar ("### Day N — pillar" / "#### Channel" / copy / "Visual: …") into individual posts.
/// Tolerant of small format drift: bold channel labels, "Day 3:" headings, trailing separators.
/// </summary>
public static partial class ContentParser
{
    [GeneratedRegex(@"^#{2,4}\s*\**\s*Day\s+(\d+)\b.*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DayHeading();

    [GeneratedRegex(@"^(?:#{3,5}\s*|\*\*)\s*(LinkedIn|X|Twitter|X \(Twitter\)|Instagram|Facebook|TikTok|Threads|YouTube|YouTube Shorts|Pinterest)\s*(?:\*\*)?\s*:?\s*(?:\*\*)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ChannelHeading();

    [GeneratedRegex(@"^\s*[*_]*\s*Visual(?: idea)?\s*[*_]*\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex VisualLine();

    public static List<ParsedPost> Parse(string markdown)
    {
        var posts = new List<ParsedPost>();
        var days = DayHeading().Matches(markdown);
        for (var d = 0; d < days.Count; d++)
        {
            var dayNum = int.Parse(days[d].Groups[1].Value);
            var start = days[d].Index + days[d].Length;
            var end = d + 1 < days.Count ? days[d + 1].Index : NextSectionOrEnd(markdown, start);
            var body = markdown[start..end];

            var channels = ChannelHeading().Matches(body);
            for (var c = 0; c < channels.Count; c++)
            {
                var cStart = channels[c].Index + channels[c].Length;
                var cEnd = c + 1 < channels.Count ? channels[c + 1].Index : body.Length;
                var block = body[cStart..cEnd];

                var visual = "";
                var vm = VisualLine().Match(block);
                if (vm.Success)
                {
                    visual = vm.Groups[1].Value.Trim().Trim('*', '_').Trim();
                    block = block.Remove(vm.Index, vm.Length);
                }

                var text = Clean(block);
                if (text.Length > 0)
                    posts.Add(new ParsedPost(dayNum, NormalizeChannel(channels[c].Groups[1].Value), text, visual));
            }
        }
        return posts;
    }

    static int NextSectionOrEnd(string md, int from)
    {
        var m = Regex.Match(md[from..], @"^##\s+(?!#)", RegexOptions.Multiline);
        return m.Success ? from + m.Index : md.Length;
    }

    static string Clean(string s)
    {
        var lines = s.Replace("\r", "").Split('\n').ToList();
        // Drop horizontal rules and surrounding blank lines.
        lines = lines.Where(l => !Regex.IsMatch(l.Trim(), @"^(-{3,}|\*{3,}|_{3,})$")).ToList();
        var text = string.Join("\n", lines).Trim();
        if (text.StartsWith('"') && text.EndsWith('"') && text.Length > 1) text = text[1..^1].Trim();
        return text;
    }

    public static string NormalizeChannel(string c) => c.Trim().ToLowerInvariant() switch
    {
        "x" or "twitter" or "x (twitter)" => "X",
        "linkedin" => "LinkedIn",
        "instagram" => "Instagram",
        "facebook" => "Facebook",
        "tiktok" => "TikTok",
        "threads" => "Threads",
        "youtube" or "youtube shorts" => "YouTube",
        "pinterest" => "Pinterest",
        var other => other,
    };
}
