using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatGPTInstaller
{
    internal static class UpdateReport
    {
        public static string Format(UpdateCheckResult result)
        {
            var text = new StringBuilder();
            text.AppendLine(result.Summary);
            text.AppendLine("检查时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("本机安装包：" + (result.InstalledVersion == null ? "未检测到" : result.InstalledVersion.ToString()));
            text.AppendLine("官方最新版：" + (result.LatestVersion == null ? "未能确认" : result.LatestVersion.ToString()));
            text.AppendLine("仅查询版本及发布说明；未下载完整安装包、申请管理员权限或改动应用。");
            text.AppendLine();
            if (!string.IsNullOrEmpty(result.LocalError)) text.AppendLine("本机检测信息：" + result.LocalError);
            text.AppendLine("版本来源：App 使用的官方生产渠道更新清单，每次检查实时读取，不固定版本号。");
            text.AppendLine(AppInfo.UpdateManifest);
            text.AppendLine("已核对该版本安装包可访问。应用内的待安装或重启状态仍需由应用完成更新后刷新。");
            text.AppendLine();
            text.AppendLine("累计更新内容");
            text.AppendLine("────────────────────────────");
            if (!result.NotesFetched)
            {
                text.AppendLine("版本检查已完成，但更新内容获取失败。不能据此判断没有变更。");
                text.AppendLine(result.NotesError ?? "未知错误");
            }
            else if (!string.IsNullOrEmpty(result.LocalError) || result.LatestVersion == null)
            {
                text.AppendLine("缺少可比较的版本，无法确定累计更新范围。");
            }
            else if (result.InstalledVersion == null)
            {
                text.AppendLine("没有本机版本作为基线。以下列出最近 3 条带版本号的桌面版说明，供安装前参考。");
                AppendNotes(text, result.Notes.Where(n => n.Versions.Any(v => ChangelogParser.Batch(v) <= ChangelogParser.Batch(result.LatestVersion))).Take(3).ToList());
            }
            else if (result.UpdateAvailable != true)
            {
                text.AppendLine("此安装渠道没有高于本机的版本，因此没有待安装的累计更新范围。");
            }
            else
            {
                var selected = ChangelogParser.Between(result.Notes, result.InstalledVersion, result.LatestVersion);
                text.AppendLine("范围：" + result.InstalledVersion + " 之后，至 " + result.LatestVersion + "（含）。");
                text.AppendLine("按官方明确标注的发布批次累计；Windows 构建号不一定与日志逐一对应。跨平台条目的适用性以原文为准。");
                text.AppendLine("未标版本的公告、未公开的修复以及同一批次内的小构建差异，不作推断。");
                if (selected.Count == 0)
                    text.AppendLine("该范围尚无可按发布批次对应的官方说明；不代表没有更新内容。");
                else
                {
                    text.AppendLine("已找到 " + selected.Count + " 条官方说明：");
                    if (!selected.Any(n => n.Versions.Any(v => ChangelogParser.Batch(v) == ChangelogParser.Batch(result.LatestVersion))))
                        text.AppendLine("覆盖提示：日志尚未覆盖目标发布批次，以下内容不能视为完整构建差异。");
                    AppendNotes(text, selected);
                }
            }
            text.AppendLine();
            text.AppendLine("完整官方日志：" + AppInfo.Changelog);
            text.AppendLine("说明采用官方原文；没有对应条目时不会生成或猜测更新内容。");
            return text.ToString().Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }

        private static void AppendNotes(StringBuilder text, List<ReleaseNote> notes)
        {
            foreach (var note in notes)
            {
                text.AppendLine();
                text.AppendLine(note.Published.ToString("yyyy-MM-dd") + "  ·  " + string.Join(", ", note.Versions.Select(v => v.ToString())));
                text.AppendLine(note.Title);
                text.AppendLine(note.Content);
                text.AppendLine("来源：" + note.Source.AbsoluteUri);
            }
        }
    }
}
