using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

// PostLoad：模板表已完全加载，改它会影响后续所有生成的物品实例
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public class InfiniteKeycards(
    ISptLogger<InfiniteKeycards> logger,
    TemplateTable templateTable,
    ItemHelper itemHelper) : IOnLoad
{
    // 只处理「使用次数正好等于这个值」的钥匙卡
    private const int TargetUsages = 10;

    // 0 = 无限次数
    private const int InfiniteUsages = 0;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        int scannedKeycards = 0;
        int modified = 0;

        foreach (var (tpl, item) in templateTable.Items)
        {
            if (item.Properties == null) continue;

            // 只处理钥匙卡（LABS keycard 那种），不包括机械钥匙
            bool isKeycard;
            try
            {
                isKeycard = itemHelper.IsOfBaseclass(tpl, BaseClasses.KEYCARD);
            }
            catch
            {
                continue;
            }
            if (!isKeycard) continue;

            scannedKeycards++;

            // 只改使用次数 == 10 的
            if (item.Properties.MaximumNumberOfUsage != TargetUsages) continue;

            item.Properties.MaximumNumberOfUsage = InfiniteUsages;
            modified++;

            logger.Debug($"[InfiniteKeycards] {tpl} ({item.Name}) 使用次数 {TargetUsages} -> {InfiniteUsages}");
        }

        logger.Info($"[InfiniteKeycards] 扫描到 {scannedKeycards} 张钥匙卡，其中 {modified} 张使用次数为 {TargetUsages} 已改为无限次");
        return Task.CompletedTask;
    }
}