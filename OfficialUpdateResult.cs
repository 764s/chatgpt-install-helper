namespace ChatGPTInstaller
{
    internal sealed class OfficialUpdateResult
    {
        public string Status;
        public string Message;
        public bool SuggestRepair;

        public static OfficialUpdateResult FromStatus(string status, UpdateRetrievalStage stage)
        {
            if (status == "restart_required")
                return new OfficialUpdateResult { Status = status, SuggestRepair = true, Message = "Codex 自带更新器报告已准备好更新、等待重启。本更新器获取失败，更新途径可能已变化，建议检查并修复适配；也可能存在渠道或网络差异。" };
            if (status == "available" && stage == UpdateRetrievalStage.Information)
                return new OfficialUpdateResult { Status = status, SuggestRepair = true, Message = "Codex 自带更新器能确认可用更新，但本更新器未能获取更新信息。更新途径可能已变化，建议检查并修复适配；这不代表官方已下载更新包。" };
            if (status == "available")
                return new OfficialUpdateResult { Status = status, Message = "Codex 自带更新器确认有更新，但尚未确认它已取得更新包。目前不能据此判断本次下载失败是更新途径变化。" };
            if (status == "up_to_date")
                return new OfficialUpdateResult { Status = status, Message = "官方自带更新器当前报告无需更新；未确认存在更新途径差异，原失败原因仍保留。" };
            if (status == "busy") return Unknown("官方自带更新器正在处理其他操作，本次未能完成交叉核对。", status);
            return Unknown("未能从官方自带更新器确认可用更新，不能据此判断更新途径是否变化。", status);
        }

        public static OfficialUpdateResult Unknown(string message, string status = "unknown")
        {
            return new OfficialUpdateResult { Status = status, Message = message };
        }
    }
}
