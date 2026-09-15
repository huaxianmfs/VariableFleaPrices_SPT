internal class Config
{
    public DiscountRule defaultRule { get; set; } = new();
    public List<CategoryOverride> overrides { get; set; } = new();
    public bool debug { get; set; } = false;
}

internal class DiscountRule
{
    /// <summary>
    /// 归一化区间下界。
    /// - 填 0：自动使用该分类内实际的最低物品价格
    /// - 填正数：强制使用该值作为下界
    /// </summary>
    public double minPrice { get; set; } = 0;

    /// <summary>
    /// 归一化区间上界。
    /// - 填 0：自动使用该分类内实际的最高物品价格
    /// - 填正数：强制使用该值作为上界
    /// </summary>
    public double maxPrice { get; set; } = 0;

    public double minDiscount { get; set; } = 0.15;
    public double maxDiscount { get; set; } = 0.40;
}

internal class CategoryOverride
{
    public string name { get; set; } = "";

    /// <summary>旧的单值写法（兼容用）。</summary>
    public string? baseClass { get; set; }

    /// <summary>按 baseClass 匹配，一个分类可放多个。</summary>
    public List<string> baseClasses { get; set; } = new();

    /// <summary>按具体物品 tpl ID 精确匹配，优先级高于 baseClasses。</summary>
    public List<string> itemTpls { get; set; } = new();

    public DiscountRule rule { get; set; } = new();
}