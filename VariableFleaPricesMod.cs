using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Traders;
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
    TraderHelper traderHelper,                 // ★ 新增：用于复刻 trader 价钳制
    RagfairConfig ragfairConfig,
    RagfairOfferHolder ragfairOfferHolder,
    RagfairOfferService ragfairOfferService,
    RagfairOfferGenerator ragfairOfferGenerator,
    ICloner cloner) : IOnLoad
{
    private Config clampedConfig = new();      // config.json         —— 受钳制
    private Config unclampedConfig = new();    // config_freeprice.json —— 不受钳制

    private readonly string configFolderPath = Path.Join(
        modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()),
        "config");

    // 精确 tpl 匹配列表：最后一个 bool 表示"是否受钳制"
    private readonly List<(MongoId Tpl, string Name, DiscountRule Rule, bool Clamp)> parsedTplOverrides = new();

    // baseClass 匹配列表
    private readonly List<(MongoId BaseClass, string Name, DiscountRule Rule, bool Clamp)> parsedBaseClassOverrides = new();

    private Dictionary<MongoId, double> originalPrices = new();

    private const string ClampedConfigFile = "config.json";
    private const string UnclampedConfigFile = "config_freeprice.json";

    // =========================================================================
    // config.json 首次不存在时的默认模板（受钳制的那份）
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

    // =========================================================================
    // config_freeprice.json 首次不存在时的默认模板（不受钳制的那份，默认空）
    // =========================================================================
    private const string DefaultUnclampedConfigTemplate = """
{
  "debug": false,
  "defaultRule": {
    "minPrice": 0,
    "maxPrice": 0,
    "minDiscount": 0.15,
    "maxDiscount": 0.40
  },
  "overrides": []
}
""";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // ---------- 确保两份配置文件都存在 ----------
        EnsureConfigExists(ClampedConfigFile, DefaultConfigTemplate);
        EnsureConfigExists(UnclampedConfigFile, DefaultUnclampedConfigTemplate);

        // ---------- 加载两份配置 ----------
        clampedConfig = LoadConfig(ClampedConfigFile) ?? new Config();
        unclampedConfig = LoadConfig(UnclampedConfigFile) ?? new Config();

        clampedConfig.defaultRule ??= new DiscountRule();
        clampedConfig.overrides ??= new List<CategoryOverride>();
        unclampedConfig.defaultRule ??= new DiscountRule();
        unclampedConfig.overrides ??= new List<CategoryOverride>();

        logger.Info($"[VariableFleaPrices] 已加载 config.json：overrides={clampedConfig.overrides.Count}（受钳制）");
        logger.Info($"[VariableFleaPrices] 已加载 config_freeprice.json：overrides={unclampedConfig.overrides.Count}（不受钳制）");

        // =========================================================================
        // 全局禁用 SPT 原生钳制
        // =========================================================================
        var dyn = ragfairConfig.Dynamic;
        var baseGen = dyn.GenerateBaseFleaPrices;

        baseGen.UseHandbookPrice = false;
        baseGen.PriceMultiplier = 1.0;
        baseGen.PreventPriceBeingBelowTraderBuyPrice = false;
        baseGen.UseHideoutCraftMultiplier = false;

        dyn.OfferAdjustment.AdjustPriceWhenBelowHandbookPrice = false;
        dyn.UseTraderPriceForOffersIfHigher = false;

        if (dyn.UnreasonableModPrices != null && dyn.UnreasonableModPrices.Count > 0)
        {
            logger.Info($"[VariableFleaPrices] 清空 UnreasonableModPrices，共 {dyn.UnreasonableModPrices.Count} 条规则");
            dyn.UnreasonableModPrices.Clear();
        }
        if (dyn.ItemPriceMultiplier != null && dyn.ItemPriceMultiplier.Count > 0)
        {
            logger.Info($"[VariableFleaPrices] 清空 ItemPriceMultiplier，共 {dyn.ItemPriceMultiplier.Count} 条");
            dyn.ItemPriceMultiplier.Clear();
        }
        if (dyn.ItemPriceOverrideRouble != null && dyn.ItemPriceOverrideRouble.Count > 0)
        {
            logger.Info($"[VariableFleaPrices] 清空 ItemPriceOverrideRouble，共 {dyn.ItemPriceOverrideRouble.Count} 条");
            dyn.ItemPriceOverrideRouble.Clear();
        }

        logger.Info("[VariableFleaPrices] 已禁用 SPT 原生钳制，按配置文件分别处理");

        // ---------- 缓存原始价格表 ----------
        if (originalPrices.Count == 0)
        {
            var cloned = cloner.Clone(templateTable.Prices);
            originalPrices = cloned ?? new Dictionary<MongoId, double>();
            logger.Info($"[VariableFleaPrices] 已缓存原始价格表，共 {originalPrices.Count} 项");
        }

        // ---------- 解析两份配置的覆盖项 ----------
        ParseOverrides();

        // ---------- 应用折扣 ----------
        ApplyLocalDiscount();

        // ---------- 刷新报价 ----------
        RefreshRagfair();

        return Task.CompletedTask;
    }

    private void EnsureConfigExists(string fileName, string defaultTemplate)
    {
        var path = Path.Join(configFolderPath, fileName);
        if (File.Exists(path)) return;

        logger.Warning($"[VariableFleaPrices] 未找到 {fileName}，自动生成默认配置：{path}");
        try
        {
            Directory.CreateDirectory(configFolderPath);
            File.WriteAllText(path, defaultTemplate, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            logger.Error($"[VariableFleaPrices] 生成 {fileName} 失败", ex);
        }
    }

    private Config? LoadConfig(string fileName)
    {
        try
        {
            return modHelper.GetJsonDataFromFile<Config>(configFolderPath, fileName);
        }
        catch (Exception ex)
        {
            logger.Error($"[VariableFleaPrices] 读取 {fileName} 失败", ex);
            return null;
        }
    }

    private void ParseOverrides()
    {
        parsedTplOverrides.Clear();
        parsedBaseClassOverrides.Clear();

        // ★ 关键：先解析"不受钳制"的，再解析"受钳制"的。
        // 因为 ResolveRuleWithName 从头到尾遍历，先插入的优先。
        // 这样一份物品同时出现在两份配置时，不受钳制的版本会覆盖受钳制的版本。
        ParseOverridesFromConfig(unclampedConfig, isClamped: false, sourceName: UnclampedConfigFile);
        ParseOverridesFromConfig(clampedConfig, isClamped: true, sourceName: ClampedConfigFile);
    }

    private void ParseOverridesFromConfig(Config cfg, bool isClamped, string sourceName)
    {
        if (cfg.overrides.Count == 0)
        {
            logger.Info($"[VariableFleaPrices] {sourceName} 没有配置分类覆盖");
            return;
        }

        foreach (var ov in cfg.overrides)
        {
            if (ov == null) continue;

            var displayName = string.IsNullOrWhiteSpace(ov.name) ? "(未命名)" : ov.name;
            var rule = ov.rule ?? cfg.defaultRule;

            // --- 1) 精确 tpl 匹配 ---
            if (ov.itemTpls != null && ov.itemTpls.Count > 0)
            {
                foreach (var tplStr in ov.itemTpls)
                {
                    if (string.IsNullOrWhiteSpace(tplStr)) continue;

                    try
                    {
                        var tplId = new MongoId(tplStr);
                        parsedTplOverrides.Add((tplId, displayName, rule, isClamped));
                        logger.Info($"[VariableFleaPrices] [{sourceName}] 注册 tpl 覆盖：{displayName} ({tplId}) 受钳制={isClamped}");
                    }
                    catch
                    {
                        logger.Warning($"[VariableFleaPrices] [{sourceName}] 无法解析 itemTpl：'{tplStr}'（分类 {displayName}）");
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
                    logger.Warning($"[VariableFleaPrices] [{sourceName}] 无法解析 baseClass：'{classStr}'（分类 {displayName}）");
                    continue;
                }

                parsedBaseClassOverrides.Add((id.Value, displayName, rule, isClamped));
                logger.Info($"[VariableFleaPrices] [{sourceName}] 注册 baseClass 覆盖：{displayName} ({id.Value}) 受钳制={isClamped}");
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
        catch { }

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

        // 第一趟：确定每个物品属于哪个分类 + 是否受钳制 + 统计分类价格范围
        var itemRuleName = new Dictionary<MongoId, string>();
        var itemRule = new Dictionary<MongoId, DiscountRule>();
        var itemClamp = new Dictionary<MongoId, bool>();
        var categoryRange = new Dictionary<string, (double Min, double Max)>();

        foreach (var itemId in prices.Keys.ToList())
        {
            double basePrice;
            if (originalPrices.TryGetValue(itemId, out var orig) && orig > 0)
                basePrice = orig;
            else
                basePrice = prices[itemId];

            if (basePrice <= 0) continue;

            var (rule, ruleName, clamp) = ResolveRuleWithName(itemId);
            itemRuleName[itemId] = ruleName;
            itemRule[itemId] = rule;
            itemClamp[itemId] = clamp;

            if (categoryRange.TryGetValue(ruleName, out var range))
                categoryRange[ruleName] = (Math.Min(range.Min, basePrice), Math.Max(range.Max, basePrice));
            else
                categoryRange[ruleName] = (basePrice, basePrice);
        }

        foreach (var kv in categoryRange)
        {
            logger.Info($"[VariableFleaPrices] 分类 {kv.Key} 自动价格范围: {kv.Value.Min:F0} ~ {kv.Value.Max:F0}");
        }

        // 第二趟：应用折扣 + 视情况钳制
        int affected = 0, failed = 0, clampedCount = 0;
        var ruleHitCount = new Dictionary<string, int>();
        foreach (var name in categoryRange.Keys) ruleHitCount[name] = 0;
        ruleHitCount["[default]"] = 0;

        foreach (var itemId in prices.Keys.ToList())
        {
            try
            {
                if (!itemRuleName.TryGetValue(itemId, out var ruleName)) continue;

                var rule = itemRule[itemId];
                var range = categoryRange[ruleName];
                var clamp = itemClamp.GetValueOrDefault(itemId, true);

                double basePrice;
                if (originalPrices.TryGetValue(itemId, out var orig) && orig > 0)
                    basePrice = orig;
                else
                    basePrice = prices[itemId];

                if (basePrice <= 0) continue;

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

                // ★ 按配置决定是否钳制
                if (clamp)
                {
                    var before = newPrice;
                    newPrice = ApplyClampingForItem(itemId, newPrice);
                    if (Math.Abs(newPrice - before) > 0.01) clampedCount++;
                }

                if (config_debug())
                {
                    logger.Debug($"[VariableFleaPrices] {itemId}: {basePrice:F0} -> {newPrice:F0} (降 {discount * 100:F1}%) [{ruleName}] 受钳制={clamp}");
                }

                prices[itemId] = newPrice;
                affected++;
                ruleHitCount[ruleName] = ruleHitCount.GetValueOrDefault(ruleName) + 1;
            }
            catch (Exception ex)
            {
                failed++;
                if (config_debug()) logger.Debug($"[VariableFleaPrices] 处理 {itemId} 失败：{ex.Message}");
            }
        }

        logger.Info($"[VariableFleaPrices] 已对 {affected} 个物品应用折扣（失败 {failed} 个，其中 {clampedCount} 个被钳制）");

        foreach (var kv in ruleHitCount)
        {
            logger.Info($"[VariableFleaPrices] 规则命中统计：{kv.Key} -> {kv.Value} 个物品");
        }
    }

    /// <summary>
    /// 复刻 SPT 原生钳制逻辑：
    ///   1) 低于 handbook × 阈值时，抬到 handbook × 1.1
    ///   2) Trader 收购价更高时，抬到 Trader 价
    /// </summary>
    private double ApplyClampingForItem(MongoId tpl, double price)
    {
        double result = price;

        // --- 1) handbook 钳制 ---
        double hbPrice = 0;
        try
        {
            var hbItem = templateTable.Handbook.Items.FirstOrDefault(x => x.Id == tpl);
            if (hbItem != null) hbPrice = hbItem.Price ?? 0;
        }
        catch { }

        if (hbPrice > 0 && result > 0)
        {
            // 复刻 RagfairPriceService.AdjustPriceIfBelowHandbook
            double priceDifference = 100.0 * hbPrice / (hbPrice + result);
            var oa = ragfairConfig.Dynamic.OfferAdjustment;
            if (priceDifference > oa.MaxPriceDifferenceBelowHandbookPercent && result >= oa.PriceThresholdRub)
            {
                result = Math.Round(hbPrice * oa.HandbookPriceMultiplier);
            }
        }

        // --- 2) Trader 价钳制 ---
        try
        {
            double traderPrice = traderHelper.GetHighestSellToTraderPrice(tpl);
            if (traderPrice > result) result = traderPrice;
        }
        catch { }

        return result;
    }

    private bool config_debug() => clampedConfig.debug || unclampedConfig.debug;

    private (DiscountRule Rule, string Name, bool Clamp) ResolveRuleWithName(MongoId itemId)
    {
        // 1) 精确 tpl 匹配（unclamped 先插入，所以先被检查）
        foreach (var (tpl, name, rule, clamp) in parsedTplOverrides)
        {
            if (tpl == itemId) return (rule, name, clamp);
        }

        // 2) baseClass 匹配
        foreach (var (baseClass, name, rule, clamp) in parsedBaseClassOverrides)
        {
            try
            {
                if (itemHelper.IsOfBaseclass(itemId, baseClass)) return (rule, name, clamp);
            }
            catch { }
        }

        // 默认规则：受钳制
        return (clampedConfig.defaultRule, "[default]", true);
    }

    private void RefreshRagfair()
    {
        var expiredIds = ragfairOfferHolder.GetStaleOfferIds();
        var flagged = 0;

        foreach (var offer in ragfairOfferHolder.GetOffers())
        {
            if (offer.IsTraderOffer() || offer.IsPlayerOffer() || expiredIds.Contains(offer.Id))
                continue;

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

    private double CalculateDiscount(double price, double minP, double maxP, double minD, double maxD)
    {
        if (maxP <= minP || maxD <= minD) return minD;
        if (price <= minP) return minD;
        if (price >= maxP) return maxD;

        var t = Math.Log(price / minP) / Math.Log(maxP / minP);
        return minD + t * (maxD - minD);
    }
}