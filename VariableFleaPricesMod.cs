using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

// -2：在 ragfair 回调之前加载。先关掉 SPT 内置算法、写好价格，再让 SPT 去生成报价。
[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 2)]
public class VariableFleaPricesMod(
    ISptLogger<VariableFleaPricesMod> logger,
    TemplateTable templateTable,
    ModHelper modHelper,
    ItemHelper itemHelper,
    RagfairConfig ragfairConfig,
    RagfairOfferHolder ragfairOfferHolder,
    RagfairOfferService ragfairOfferService,
    RagfairOfferGenerator ragfairOfferGenerator,
    ICloner cloner) : IOnLoad
{
    private Config config = new();

    private readonly string configFolderPath = Path.Join(
        modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()),
        "config");

    // 精确 tpl 匹配列表（优先级最高）
    private readonly List<(MongoId Tpl, string Name, DiscountRule Rule)> parsedTplOverrides = new();

    // baseClass 匹配列表
    private readonly List<(MongoId BaseClass, string Name, DiscountRule Rule)> parsedBaseClassOverrides = new();

    // 缓存一份原始价格表（SPT 内置值），作为折扣计算的基准。
    // 避免热重载 / 二次扫描时在已降价的价格上再乘一次。
    private Dictionary<MongoId, double> originalPrices = new();

    // =========================================================================
    // 首次运行时如果 config.json 不存在，会自动生成一份带完整模板的默认配置
    // =========================================================================
    private const string DefaultConfigTemplate = """
{
  "debug": false,
  "defaultRule": {
    "minPrice": 0,
    "maxPrice": 0,
    "minDiscount": 0.15,
    "maxDiscount": 0.40
  },
  "overrides": [
    {
      "name": "武器-G36系列",
      "itemTpls": [],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.05,
        "maxDiscount": 0.20
      }
    },
    {
      "name": "武器-AUG系列",
      "itemTpls": [],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.05,
        "maxDiscount": 0.20
      }
    },
    {
      "name": "武器-M1A系列",
      "itemTpls": [],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.05,
        "maxDiscount": 0.20
      }
    },
    {
      "name": "武器",
      "baseClasses": [
        "5422acb9af1c889c16000029",
        "543be5664bdc2dd4348b4569"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.10,
        "maxDiscount": 0.35
      }
    },
    {
      "name": "弹药",
      "baseClasses": [
        "5485a8684bdc2da71d8b4567"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.20,
        "maxDiscount": 0.50
      }
    },
    {
      "name": "配件",
      "baseClasses": [
        "5448fe124bdc2da5018b4568",
        "5448bc234bdc2d3c308b4569"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.15,
        "maxDiscount": 0.45
      }
    },
    {
      "name": "装备",
      "baseClasses": [
        "5448e54d4bdc2dcc718b4568",
        "5a341c4086f77401f2541505",
        "5a341c4686f77469e155819e",
        "5448e5724bdc2ddf718b4568",
        "5448e5284bdc2dcb718b4567",
        "5448e53e4bdc2d60728b4567",
        "5645bc214bdc2d363b8b4571"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.12,
        "maxDiscount": 0.35
      }
    },
    {
      "name": "医疗品",
      "baseClasses": [
        "5448f3a64bdc2d2771977719",
        "5448f3a14bdc2d2771977719",
        "5448f39d4bdc2d0a4c8b456d"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.20,
        "maxDiscount": 0.50
      }
    },
    {
      "name": "食品饮料",
      "baseClasses": [
        "5448e8d04bdc2ddf718b4569",
        "5448e8d64bdc2dce718b4568"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.20,
        "maxDiscount": 0.50
      }
    },
    {
      "name": "钥匙",
      "baseClasses": [
        "5c99f98d86f7745c314214b3"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.05,
        "maxDiscount": 0.25
      }
    },
    {
      "name": "钥匙卡",
      "baseClasses": [
        "5c164d2286f774194c5e69fa"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.05,
        "maxDiscount": 0.20
      }
    },
    {
      "name": "交换品",
      "baseClasses": [
        "5448eb774bdc2d0a728b4567"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.20,
        "maxDiscount": 0.50
      }
    },
    {
      "name": "贵重品",
      "baseClasses": [
        "5661632d4bdc2d903d8b456b"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.15,
        "maxDiscount": 0.40
      }
    },
    {
      "name": "手雷",
      "baseClasses": [
        "543be6564bdc2df4348b4568"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0.15,
        "maxDiscount": 0.40
      }
    },
    {
      "name": "货币",
      "baseClasses": [
        "543be5dd4bdc2deb348b4569"
      ],
      "rule": {
        "minPrice": 0,
        "maxPrice": 0,
        "minDiscount": 0,
        "maxDiscount": 0
      }
    }
  ]
}
""";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var configPath = Path.Join(configFolderPath, "config.json");

        // 如果 config.json 不存在，自动生成一份带完整模板的默认配置
        if (!File.Exists(configPath))
        {
            logger.Warning($"[VariableFleaPrices] 未找到 config.json，自动生成默认配置：{configPath}");
            try
            {
                Directory.CreateDirectory(configFolderPath);
                File.WriteAllText(configPath, DefaultConfigTemplate, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                logger.Error("[VariableFleaPrices] 生成默认 config.json 失败", ex);
            }
        }

        try
        {
            config = modHelper.GetJsonDataFromFile<Config>(configFolderPath, "config.json");
        }
        catch (Exception ex)
        {
            logger.Error("[VariableFleaPrices] 读取 config.json 失败，使用内置默认", ex);
            config = new Config();
        }

        config.defaultRule ??= new DiscountRule();
        config.overrides ??= new List<CategoryOverride>();

        logger.Info($"[VariableFleaPrices] config 加载完成：defaultRule(minP={config.defaultRule.minPrice}, maxP={config.defaultRule.maxPrice}, minD={config.defaultRule.minDiscount}, maxD={config.defaultRule.maxDiscount}), overrides 数量={config.overrides.Count}");

        // =========================================================================
        // 关键修复 1：关闭 SPT 内置跳蚤基础价生成
        // =========================================================================
        var baseGen = ragfairConfig.Dynamic.GenerateBaseFleaPrices;

        logger.Info($"[VariableFleaPrices] 修改前 GenerateBaseFleaPrices: UseHandbookPrice={baseGen.UseHandbookPrice}, PriceMultiplier={baseGen.PriceMultiplier}, PreventPriceBeingBelowTraderBuyPrice={baseGen.PreventPriceBeingBelowTraderBuyPrice}, UseHideoutCraftMultiplier={baseGen.UseHideoutCraftMultiplier}");

        baseGen.UseHandbookPrice = false;
        baseGen.PriceMultiplier = 1.0;
        baseGen.PreventPriceBeingBelowTraderBuyPrice = false;
        baseGen.UseHideoutCraftMultiplier = false;

        logger.Info("[VariableFleaPrices] 已禁用 SPT 内置跳蚤价计算，PriceMultiplier 归一到 1.0");

        // =========================================================================
        // 关键修复 2：缓存原始价格表作为折扣基准
        // =========================================================================
        if (originalPrices.Count == 0)
        {
            var cloned = cloner.Clone(templateTable.Prices);
            originalPrices = cloned ?? new Dictionary<MongoId, double>();
            logger.Info($"[VariableFleaPrices] 已缓存原始价格表，共 {originalPrices.Count} 项");
        }

        ParseOverrides();
        ApplyLocalDiscount();
        RefreshRagfair();

        return Task.CompletedTask;
    }

    private void ParseOverrides()
    {
        parsedTplOverrides.Clear();
        parsedBaseClassOverrides.Clear();

        if (config.overrides.Count == 0)
        {
            logger.Info("[VariableFleaPrices] 没有配置分类覆盖，全部使用默认规则");
            return;
        }

        foreach (var ov in config.overrides)
        {
            if (ov == null) continue;

            var displayName = string.IsNullOrWhiteSpace(ov.name) ? "(未命名)" : ov.name;
            var rule = ov.rule ?? config.defaultRule;

            bool hasTpl = false;
            bool hasBaseClass = false;

            // --- 1) 精确 tpl 匹配（优先级最高） ---
            if (ov.itemTpls != null && ov.itemTpls.Count > 0)
            {
                foreach (var tplStr in ov.itemTpls)
                {
                    if (string.IsNullOrWhiteSpace(tplStr)) continue;

                    try
                    {
                        var tplId = new MongoId(tplStr);
                        parsedTplOverrides.Add((tplId, displayName, rule));
                        logger.Info($"[VariableFleaPrices] 注册 tpl 覆盖：{displayName} ({tplId})");
                        hasTpl = true;
                    }
                    catch
                    {
                        logger.Warning($"[VariableFleaPrices] 无法解析 itemTpl：'{tplStr}'（分类 {displayName}），已跳过");
                    }
                }
            }

            // --- 2) baseClass 匹配 ---
            var classStrings = new List<string>();

            if (ov.baseClasses != null)
            {
                foreach (var s in ov.baseClasses)
                {
                    if (!string.IsNullOrWhiteSpace(s)) classStrings.Add(s);
                }
            }

            if (!string.IsNullOrWhiteSpace(ov.baseClass))
            {
                classStrings.Add(ov.baseClass!);
            }

            foreach (var classStr in classStrings)
            {
                var id = ResolveBaseClass(classStr);
                if (id == null)
                {
                    logger.Warning($"[VariableFleaPrices] 无法解析 baseClass：'{classStr}'（分类 {displayName}），已跳过");
                    continue;
                }

                parsedBaseClassOverrides.Add((id.Value, displayName, rule));
                logger.Info($"[VariableFleaPrices] 注册 baseClass 覆盖：{displayName} ({id.Value})");
                hasBaseClass = true;
            }

            if (!hasTpl && !hasBaseClass && config.debug)
            {
                logger.Debug($"[VariableFleaPrices] 分类 {displayName} 既没有 itemTpls 也没有 baseClasses，已跳过");
            }
        }

        // === 诊断：baseClass 每条匹配到多少物品 ===
        if (parsedBaseClassOverrides.Count > 0)
        {
            var priceKeys = templateTable.Prices.Keys.ToList();

            foreach (var (baseClass, name, _) in parsedBaseClassOverrides)
            {
                int matchCount = 0;
                int errorCount = 0;
                string firstError = "";

                foreach (var itemId in priceKeys)
                {
                    try
                    {
                        if (itemHelper.IsOfBaseclass(itemId, baseClass))
                        {
                            matchCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        if (errorCount == 1) firstError = ex.Message;
                    }
                }

                logger.Info($"[VariableFleaPrices] 诊断：分类 {name} ({baseClass}) 匹配 {matchCount}/{priceKeys.Count} 个物品（异常 {errorCount} 个）");
                if (errorCount > 0)
                {
                    logger.Warning($"[VariableFleaPrices] 诊断：{name} 首个异常：{firstError}");
                }
            }
        }
    }

    private MongoId? ResolveBaseClass(string nameOrId)
    {
        try
        {
            var field = typeof(BaseClasses).GetField(
                nameOrId,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

            if (field != null)
            {
                var val = field.GetValue(null);
                if (val is MongoId mongoId) return mongoId;
                if (val is string s && !string.IsNullOrWhiteSpace(s)) return new MongoId(s);
            }
        }
        catch
        {
            // 忽略，继续当 MongoId 处理
        }

        try
        {
            return new MongoId(nameOrId);
        }
        catch
        {
            return null;
        }
    }

    private void ApplyLocalDiscount()
    {
        var prices = templateTable.Prices;

        // =========================================================================
        // 第一趟：确定每个物品属于哪个分类，并统计每个分类的【实际价格范围】
        // =========================================================================
        var itemRuleName = new Dictionary<MongoId, string>();     // itemId -> 分类名
        var itemRule = new Dictionary<MongoId, DiscountRule>();   // itemId -> 规则
        var categoryRange = new Dictionary<string, (double Min, double Max)>();

        foreach (var itemId in prices.Keys.ToList())
        {
            double basePrice;
            if (originalPrices.TryGetValue(itemId, out var orig) && orig > 0)
            {
                basePrice = orig;
            }
            else
            {
                basePrice = prices[itemId];
            }

            if (basePrice <= 0) continue;

            var (rule, ruleName) = ResolveRuleWithName(itemId);
            itemRuleName[itemId] = ruleName;
            itemRule[itemId] = rule;

            if (categoryRange.TryGetValue(ruleName, out var range))
            {
                categoryRange[ruleName] = (Math.Min(range.Min, basePrice), Math.Max(range.Max, basePrice));
            }
            else
            {
                categoryRange[ruleName] = (basePrice, basePrice);
            }
        }

        // 日志：每个分类的实际价格范围（这是自动归一化用的端点）
        foreach (var kv in categoryRange)
        {
            logger.Info($"[VariableFleaPrices] 分类 {kv.Key} 自动价格范围: {kv.Value.Min:F0} ~ {kv.Value.Max:F0}");
        }

        // =========================================================================
        // 第二趟：对每个物品，用【它所在分类的 min/max】做对数插值算折扣
        // =========================================================================
        int affected = 0, failed = 0;
        var ruleHitCount = new Dictionary<string, int>();
        foreach (var name in categoryRange.Keys)
        {
            ruleHitCount[name] = 0;
        }
        ruleHitCount["[default]"] = 0;

        foreach (var itemId in prices.Keys.ToList())
        {
            try
            {
                if (!itemRuleName.TryGetValue(itemId, out var ruleName)) continue;

                var rule = itemRule[itemId];
                var range = categoryRange[ruleName];

                double basePrice;
                if (originalPrices.TryGetValue(itemId, out var orig) && orig > 0)
                {
                    basePrice = orig;
                }
                else
                {
                    basePrice = prices[itemId];
                }

                if (basePrice <= 0) continue;

                // ★ 归一化端点：
                //   - rule.minPrice / rule.maxPrice 都 > 0 且有效时，用手动值（硬性覆盖）
                //   - 否则用该分类自动扫出来的实际范围
                double minP, maxP;
                if (rule.minPrice > 0 && rule.maxPrice > rule.minPrice)
                {
                    minP = rule.minPrice;
                    maxP = rule.maxPrice;
                }
                else
                {
                    minP = range.Min;
                    maxP = range.Max;
                }

                var discount = CalculateDiscount(basePrice, minP, maxP, rule.minDiscount, rule.maxDiscount);
                var newPrice = Math.Round(basePrice * (1.0 - discount));
                if (newPrice < 1) newPrice = 1;

                if (config.debug)
                {
                    logger.Debug($"[VariableFleaPrices] {itemId}: {basePrice:F0} -> {newPrice:F0} (降 {discount * 100:F1}%) [{ruleName}] 端点({minP:F0}~{maxP:F0})");
                }

                prices[itemId] = newPrice;
                affected++;
                ruleHitCount[ruleName] = ruleHitCount.GetValueOrDefault(ruleName) + 1;
            }
            catch (Exception ex)
            {
                failed++;
                if (config.debug)
                {
                    logger.Debug($"[VariableFleaPrices] 处理 {itemId} 失败：{ex.Message}");
                }
            }
        }

        logger.Info($"[VariableFleaPrices] 已对 {affected} 个物品应用折扣（失败 {failed} 个）");

        foreach (var kv in ruleHitCount)
        {
            logger.Info($"[VariableFleaPrices] 规则命中统计：{kv.Key} -> {kv.Value} 个物品");
        }

        if (config.debug && prices.Count > 0)
        {
            var sorted = prices.OrderBy(kv => kv.Value).ToList();
            var positions = new[] { 0, sorted.Count / 2, sorted.Count - 1 };
            foreach (var pos in positions)
            {
                var kv = sorted[pos];
                logger.Info($"[VariableFleaPrices] 样本(序{pos}): {kv.Key} = {kv.Value:F0}");
            }
        }
    }

    /// <summary>
    /// 决定一件物品走哪条规则。
    /// 匹配顺序：先精确 tpl（武器细分），再 baseClass（大类），都没有就走 defaultRule。
    /// </summary>
    private (DiscountRule Rule, string Name) ResolveRuleWithName(MongoId itemId)
    {
        // 1) 精确 tpl 匹配
        foreach (var (tpl, name, rule) in parsedTplOverrides)
        {
            if (tpl == itemId) return (rule, name);
        }

        // 2) baseClass 匹配
        foreach (var (baseClass, name, rule) in parsedBaseClassOverrides)
        {
            try
            {
                if (itemHelper.IsOfBaseclass(itemId, baseClass))
                {
                    return (rule, name);
                }
            }
            catch
            {
                // 单条匹配失败，继续下一条
            }
        }

        return (config.defaultRule, "[default]");
    }

    private DiscountRule ResolveRule(MongoId itemId)
    {
        return ResolveRuleWithName(itemId).Rule;
    }

    private void RefreshRagfair()
    {
        var expiredIds = ragfairOfferHolder.GetStaleOfferIds();
        var flagged = 0;

        foreach (var offer in ragfairOfferHolder.GetOffers())
        {
            if (offer.IsTraderOffer() || offer.IsPlayerOffer() || expiredIds.Contains(offer.Id))
            {
                continue;
            }

            ragfairOfferHolder.FlagOfferAsExpired(offer.Id);
            flagged++;
        }

        if (flagged == 0)
        {
            logger.Info("[VariableFleaPrices] 没有需要刷新的报价");
            return;
        }

        ragfairOfferService.RemoveExpiredOffers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, true, true);
        ragfairOfferGenerator.GenerateDynamicOffers();

        logger.Info($"[VariableFleaPrices] 已刷新 {flagged} 个报价");
    }

    /// <summary>
    /// 对数插值计算折扣。
    /// price 在 [minP, maxP] 之间时按 log 曲线在 [minD, maxD] 之间插值。
    /// </summary>
    private double CalculateDiscount(double price, double minP, double maxP, double minD, double maxD)
    {
        if (maxP <= minP || maxD <= minD) return minD;
        if (price <= minP) return minD;
        if (price >= maxP) return maxD;

        var t = Math.Log(price / minP) / Math.Log(maxP / minP);
        return minD + t * (maxD - minD);
    }
}