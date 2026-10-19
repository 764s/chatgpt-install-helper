using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace ChatGPTInstaller
{
    internal static class ChangelogParser
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;
        private static readonly TimeSpan MatchLimit = TimeSpan.FromSeconds(3);

        public static List<ReleaseNote> Parse(string html)
        {
            var records = Regex.Matches(html, @"<li\b(?<attributes>[^>]*\bid\s*=\s*['"" ]?codex-[^>]*)>", Options, MatchLimit);
            var notes = new List<ReleaseNote>();
            foreach (Match record in records)
            {
                string attributes = record.Groups["attributes"].Value;
                string topics = Attribute(attributes, "data-codex-topics");
                if (!topics.Split(',').Contains("codex-app")) continue;
                int end = html.IndexOf("</article>", record.Index + record.Length, StringComparison.OrdinalIgnoreCase);
                if (end < 0) continue;
                string block = html.Substring(record.Index + record.Length, end - record.Index - record.Length);
                var heading = Regex.Match(block, @"<h3\b[^>]*>(.*?)</h3>", Options, MatchLimit);
                var article = Regex.Match(block, @"<article\b[^>]*>(.*)$", Options, MatchLimit);
                var date = Regex.Match(block, @"<time\b[^>]*>(.*?)</time>", Options, MatchLimit);
                if (!heading.Success || !article.Success || !date.Success) continue;
                string header = Regex.Replace(heading.Groups[1].Value, @"<button\b[^>]*>.*?</button>", "", Options, MatchLimit);
                var versionSpan = Regex.Match(header, @"<span\b[^>]*\bclass\s*=\s*['""][^'""]*text-tertiary[^'""]*['""][^>]*>(.*?)</span>", Options, MatchLimit);
                string title = PlainText(versionSpan.Success ? header.Replace(versionSpan.Value, "") : header);
                // 独立 macOS 公告不能作为 Windows 的更新内容；混合平台条目保留原文限制。
                if (Regex.IsMatch(title, @"\bmacOS\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(title, @"\bWindows\b", RegexOptions.IgnoreCase)) continue;
                DateTime published;
                if (!DateTime.TryParseExact(PlainText(date.Groups[1].Value), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out published)) continue;
                string id = Attribute(attributes, "id");
                var note = new ReleaseNote
                {
                    Title = title, Content = PlainText(article.Groups[1].Value), Published = published,
                    Source = new Uri(AppInfo.Changelog + "#" + Uri.EscapeDataString(id))
                };
                foreach (Match item in Regex.Matches(PlainText(versionSpan.Success ? versionSpan.Groups[1].Value : ""), @"(?<![\d.])\d{2,4}\.\d{1,5}(?:\.\d{1,6}){0,2}(?![\d.])"))
                {
                    Version version;
                    if (Version.TryParse(item.Value, out version)) note.Versions.Add(version);
                }
                if (!string.IsNullOrWhiteSpace(note.Content)) notes.Add(note);
            }
            if (notes.Count == 0) throw new InvalidDataException("官方日志页面未找到可识别的桌面版条目，页面格式可能已改变。");
            return notes.GroupBy(n => n.Source.AbsoluteUri).Select(g => g.First()).OrderByDescending(n => n.Published).ToList();
        }

        public static List<ReleaseNote> Between(IEnumerable<ReleaseNote> notes, Version installed, Version latest)
        {
            if (installed == null || latest == null || installed >= latest) return new List<ReleaseNote>();
            return notes.Where(n => n.Versions.Any(v => Batch(v) > Batch(installed) && Batch(v) <= Batch(latest)))
                .OrderByDescending(n => n.Versions.Select(Batch).Max()).ThenByDescending(n => n.Published).ToList();
        }

        public static Version Batch(Version version) { return new Version(version.Major, version.Minor); }

        private static string Attribute(string attributes, string name)
        {
            var match = Regex.Match(attributes, @"\b" + Regex.Escape(name) + @"\s*=\s*(['""])(.*?)\1", Options, MatchLimit);
            return match.Success ? WebUtility.HtmlDecode(match.Groups[2].Value) : "";
        }

        public static string PlainText(string html)
        {
            string value = Regex.Replace(html, @"<(script|style|svg|button)\b[^>]*>.*?</\1>", "", Options, MatchLimit);
            // 保留段落、列表层次和链接文字，不执行网页或脚本。
            value = Regex.Replace(value, @"[\r\n\t]+", " ");
            value = Regex.Replace(value, @"<li\b[^>]*>", "\n• ", Options, MatchLimit);
            value = Regex.Replace(value, @"<(br|/p|/h[1-6]|/li|/ul|/ol|/pre|/blockquote)\b[^>]*>", "\n", Options, MatchLimit);
            value = Regex.Replace(value, @"<[^>]+>", "", Options, MatchLimit);
            value = WebUtility.HtmlDecode(value).Replace('\u00A0', ' ');
            value = Regex.Replace(value, @"[ \t]+", " ");
            value = Regex.Replace(value, @" *\n *", "\n");
            value = Regex.Replace(value, @"\n{3,}", "\n\n");
            return value.Trim();
        }
    }
}
