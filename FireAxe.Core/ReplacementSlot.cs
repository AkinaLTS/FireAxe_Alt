using System;
using System.Collections.Generic;
using System.Globalization;

namespace FireAxe;

/// <summary>
/// The kind of game object a replacement slot stands for.
/// </summary>
public enum ReplacementSlotCategory
{
    PrimaryWeapon,
    SecondaryWeapon,
    MeleeWeapon,
    Equipment,
    CommonInfected,
    SpecialInfected
}

/// <summary>
/// A slot of the game that addons can replace, for example the Chrome Shotgun's viewmodel
/// or the Hunter's model. A slot is identified by the vanilla file names it owns.
/// </summary>
public sealed class ReplacementSlot
{
    public ReplacementSlot(
        string id,
        ReplacementSlotCategory category,
        string displayNameZh,
        string displayNameEn,
        params string[] matchKeys)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(displayNameZh);
        ArgumentException.ThrowIfNullOrEmpty(displayNameEn);
        ArgumentNullException.ThrowIfNull(matchKeys);
        if (matchKeys.Length == 0)
        {
            throw new ArgumentException("At least one match key is required.", nameof(matchKeys));
        }

        var keys = new string[matchKeys.Length];
        for (int i = 0; i < matchKeys.Length; i++)
        {
            string key = matchKeys[i];
            ArgumentException.ThrowIfNullOrEmpty(key);
            keys[i] = key.ToLowerInvariant();
        }

        Id = id;
        Category = category;
        DisplayNameZh = displayNameZh;
        DisplayNameEn = displayNameEn;
        Keys = keys;
    }

    public string Id { get; }

    public ReplacementSlotCategory Category { get; }

    public string DisplayNameZh { get; }

    public string DisplayNameEn { get; }

    /// <summary>
    /// Lower-case file stem / path segment prefixes that identify this slot, for example
    /// <c>v_shotgun_chrome</c> and <c>shotgun_chrome</c>.
    /// </summary>
    public IReadOnlyList<string> Keys { get; }

    public string DisplayName => IsChineseCulture ? DisplayNameZh : DisplayNameEn;

    public bool IsChineseCulture => IsChineseUiCulture();

    internal static bool IsChineseUiCulture()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => DisplayNameEn;
}

/// <summary>
/// The built-in catalogue of replaceable slots and the logic that maps a VPK file path to a slot.
/// The file names come from the vanilla game archives (pak01_dir.vpk).
/// </summary>
public static class ReplacementSlotCatalog
{
    private static readonly ReplacementSlot[] s_slots =
    [
        // Primary weapons
        new("smg", ReplacementSlotCategory.PrimaryWeapon, "冲锋枪（Uzi）", "SMG (Uzi)", "v_smg", "smg"),
        new("silenced_smg", ReplacementSlotCategory.PrimaryWeapon, "消音冲锋枪", "Silenced SMG", "v_silenced_smg", "silenced_smg"),
        new("mp5", ReplacementSlotCategory.PrimaryWeapon, "MP5", "MP5", "v_smg_mp5", "smg_mp5"),
        new("pumpshotgun", ReplacementSlotCategory.PrimaryWeapon, "木喷（泵动霰弹枪）", "Pump Shotgun", "v_pumpshotgun", "pumpshotgun"),
        new("shotgun_chrome", ReplacementSlotCategory.PrimaryWeapon, "铁喷（铬制霰弹枪）", "Chrome Shotgun", "v_shotgun_chrome", "shotgun_chrome"),
        new("autoshotgun", ReplacementSlotCategory.PrimaryWeapon, "连喷（自动霰弹枪）", "Auto Shotgun", "v_autoshotgun", "autoshotgun"),
        new("shotgun_spas", ReplacementSlotCategory.PrimaryWeapon, "战斗喷（战术霰弹枪）", "Combat Shotgun", "v_shotgun_spas", "shotgun_spas"),
        new("huntingrifle", ReplacementSlotCategory.PrimaryWeapon, "猎枪（鸟狙）", "Hunting Rifle", "v_huntingrifle", "huntingrifle"),
        new("sniper_military", ReplacementSlotCategory.PrimaryWeapon, "军狙", "Military Sniper", "v_sniper_military", "sniper_military"),
        new("sniper_awp", ReplacementSlotCategory.PrimaryWeapon, "AWP", "AWP", "v_snip_awp", "snip_awp"),
        new("sniper_scout", ReplacementSlotCategory.PrimaryWeapon, "鸟狙（侦察兵）", "Scout", "v_snip_scout", "snip_scout"),
        new("rifle", ReplacementSlotCategory.PrimaryWeapon, "M16 步枪", "M16 Rifle", "v_rifle", "rifle"),
        new("rifle_ak47", ReplacementSlotCategory.PrimaryWeapon, "AK47", "AK-47", "v_rifle_ak47", "rifle_ak47"),
        new("rifle_desert", ReplacementSlotCategory.PrimaryWeapon, "SCAR（三连发）", "Desert Rifle (SCAR)", "v_desert_rifle", "desert_rifle"),
        new("rifle_sg552", ReplacementSlotCategory.PrimaryWeapon, "SG552", "SG552", "v_rif_sg552", "rif_sg552"),
        new("grenade_launcher", ReplacementSlotCategory.PrimaryWeapon, "榴弹发射器", "Grenade Launcher", "v_grenade_launcher", "grenade_launcher"),

        // Secondary weapons
        new("pistol", ReplacementSlotCategory.SecondaryWeapon, "手枪", "Pistol", "v_pistola", "pistola"),
        new("dual_pistols", ReplacementSlotCategory.SecondaryWeapon, "双持手枪", "Dual Pistols", "v_dual_pistola", "dual_pistola"),
        new("magnum", ReplacementSlotCategory.SecondaryWeapon, "马格南", "Magnum", "v_desert_eagle", "desert_eagle"),

        // Melee weapons
        new("chainsaw", ReplacementSlotCategory.MeleeWeapon, "电锯", "Chainsaw", "v_chainsaw", "chainsaw"),
        new("fireaxe", ReplacementSlotCategory.MeleeWeapon, "消防斧", "Fire Axe", "v_fireaxe", "fireaxe"),
        new("katana", ReplacementSlotCategory.MeleeWeapon, "武士刀", "Katana", "v_katana", "katana"),
        new("machete", ReplacementSlotCategory.MeleeWeapon, "砍刀", "Machete", "v_machete", "machete"),
        new("crowbar", ReplacementSlotCategory.MeleeWeapon, "撬棍", "Crowbar", "v_crowbar", "crowbar"),
        new("baseball_bat", ReplacementSlotCategory.MeleeWeapon, "棒球棍", "Baseball Bat", "v_bat", "bat"),
        new("cricket_bat", ReplacementSlotCategory.MeleeWeapon, "板球棒", "Cricket Bat", "v_cricket_bat", "cricket_bat"),
        new("electric_guitar", ReplacementSlotCategory.MeleeWeapon, "电吉他", "Electric Guitar", "v_electric_guitar", "electric_guitar"),
        new("tonfa", ReplacementSlotCategory.MeleeWeapon, "警棍", "Tonfa", "v_tonfa", "tonfa"),
        new("frying_pan", ReplacementSlotCategory.MeleeWeapon, "平底锅", "Frying Pan", "v_frying_pan", "frying_pan"),
        new("knife", ReplacementSlotCategory.MeleeWeapon, "战斗刀", "Combat Knife", "v_knife_t", "knife_t"),
        new("riotshield", ReplacementSlotCategory.MeleeWeapon, "防暴盾", "Riot Shield", "v_riotshield", "riotshield"),
        new("gnome", ReplacementSlotCategory.MeleeWeapon, "花园矮人", "Gnome", "v_gnome", "gnome"),

        // Throwables and healing items
        new("medkit", ReplacementSlotCategory.Equipment, "医疗包", "Medkit", "v_medkit", "medkit"),
        new("pain_pills", ReplacementSlotCategory.Equipment, "止痛药", "Pain Pills", "v_painpills", "painpills"),
        new("adrenaline", ReplacementSlotCategory.Equipment, "肾上腺素", "Adrenaline", "v_adrenaline", "adrenaline"),
        new("defibrillator", ReplacementSlotCategory.Equipment, "除颤器", "Defibrillator", "v_defibrillator", "defibrillator"),
        new("molotov", ReplacementSlotCategory.Equipment, "燃烧瓶", "Molotov", "v_molotov", "molotov"),
        new("pipebomb", ReplacementSlotCategory.Equipment, "土制炸弹", "Pipe Bomb", "v_pipebomb", "pipebomb"),
        new("bile_flask", ReplacementSlotCategory.Equipment, "胆汁罐", "Bile Flask", "v_bile_flask", "bile_flask"),

        // Common infected
        new("common_male", ReplacementSlotCategory.CommonInfected, "普通感染者（男）", "Common Infected (Male)", "common_male"),
        new("common_female", ReplacementSlotCategory.CommonInfected, "普通感染者（女）", "Common Infected (Female)", "common_female"),

        // Special infected
        new("hunter", ReplacementSlotCategory.SpecialInfected, "猎人", "Hunter", "hunter", "v_claw_hunter", "claw_hunter"),
        new("smoker", ReplacementSlotCategory.SpecialInfected, "烟鬼", "Smoker", "smoker", "v_claw_smoker", "claw_smoker"),
        new("boomer", ReplacementSlotCategory.SpecialInfected, "胖子（呕吐者）", "Boomer", "boomer", "boomette", "v_claw_boomer", "claw_boomer"),
        new("charger", ReplacementSlotCategory.SpecialInfected, "牛（冲锋者）", "Charger", "charger", "v_charger_arms", "v_claw_charger", "claw_charger"),
        new("jockey", ReplacementSlotCategory.SpecialInfected, "猴子（骑师）", "Jockey", "jockey", "v_jockey_arms", "claw_jockey"),
        new("spitter", ReplacementSlotCategory.SpecialInfected, "酸液（吐酸者）", "Spitter", "spitter", "v_spitter_arms", "claw_spitter"),
        new("tank", ReplacementSlotCategory.SpecialInfected, "坦克", "Tank", "hulk", "v_claw_hulk", "claw_hulk"),
        new("witch", ReplacementSlotCategory.SpecialInfected, "女巫", "Witch", "witch")
    ];

    // Keys starting with "v_" are model stems (models/v_models/*.mdl); keys without it are
    // directory names used by material and infected model paths.
    private static readonly (string Key, ReplacementSlot Slot)[] s_modelKeys;
    private static readonly (string Key, ReplacementSlot Slot)[] s_otherKeys;
    private static readonly Dictionary<string, ReplacementSlot> s_exactOtherKeys = new(StringComparer.Ordinal);

    static ReplacementSlotCatalog()
    {
        var modelKeys = new List<(string, ReplacementSlot)>();
        var otherKeys = new List<(string, ReplacementSlot)>();
        foreach (var slot in s_slots)
        {
            foreach (var key in slot.Keys)
            {
                if (key.StartsWith("v_", StringComparison.Ordinal))
                {
                    modelKeys.Add((key, slot));
                }
                else
                {
                    otherKeys.Add((key, slot));
                    s_exactOtherKeys.TryAdd(key, slot);
                }
            }
        }

        // Longest key first so that "v_smg_mp5" wins over "v_smg".
        modelKeys.Sort((a, b) => b.Item1.Length.CompareTo(a.Item1.Length));
        otherKeys.Sort((a, b) => b.Item1.Length.CompareTo(a.Item1.Length));
        s_modelKeys = [.. modelKeys];
        s_otherKeys = [.. otherKeys];
    }

    public static IReadOnlyList<ReplacementSlot> Slots => s_slots;

    public static IEnumerable<ReplacementSlot> GetSlots(ReplacementSlotCategory category)
    {
        foreach (var slot in s_slots)
        {
            if (slot.Category == category)
            {
                yield return slot;
            }
        }
    }

    /// <summary>
    /// Maps a path inside a VPK to the slot it replaces, or null when the file is unrelated
    /// to any catalogued slot.
    /// </summary>
    public static ReplacementSlot? TryMatch(string vpkFilePath)
    {
        ArgumentNullException.ThrowIfNull(vpkFilePath);

        string path = vpkFilePath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        if (path.Length == 0)
        {
            return null;
        }

        // Only the model directories the game itself uses count. Ported assets from other
        // Source games (for example models/weapons/v_smg1.mdl) must not be mistaken for a
        // replacement of an L4D2 slot.
        if (path.StartsWith("models/v_models/", StringComparison.Ordinal)
            || path.StartsWith("models/weapons/melee/", StringComparison.Ordinal))
        {
            return MatchPrefix(GetFileNameStem(path), s_modelKeys);
        }

        if (path.StartsWith("models/infected/", StringComparison.Ordinal))
        {
            string stem = GetFileNameStem(path);
            if (stem.StartsWith("v_", StringComparison.Ordinal))
            {
                return MatchPrefix(stem, s_modelKeys);
            }
            return MatchPrefix(stem, s_otherKeys);
        }

        if (path.StartsWith("materials/models/v_models/", StringComparison.Ordinal)
            || path.StartsWith("materials/models/infected/", StringComparison.Ordinal)
            || path.StartsWith(MeleeMaterialRoot, StringComparison.Ordinal))
        {
            if (!IsMaterialFile(path))
            {
                return null;
            }

            // Material directories are named after the weapon. A texture-only mod often puts
            // the file directly into a shared directory (for example
            // materials/models/weapons/melee/bat.vmt), so the file stem counts as well.
            string root = path.StartsWith(MeleeMaterialRoot, StringComparison.Ordinal)
                ? MeleeMaterialRoot
                : "materials/models/";
            return MatchMaterialPath(path, root);
        }

        return null;
    }

    private const string MeleeMaterialRoot = "materials/models/weapons/melee/";

    private static bool IsMaterialFile(string path)
    {
        return path.EndsWith(".vmt", StringComparison.Ordinal)
            || path.EndsWith(".vtf", StringComparison.Ordinal);
    }

    private static ReplacementSlot? MatchMaterialPath(string path, string root)
    {
        // Directory names first, from the material root down to the containing directory.
        int start = root.Length;
        int lastSlash = path.LastIndexOf('/');
        while (start < lastSlash)
        {
            int end = path.IndexOf('/', start);
            if (end < 0)
            {
                end = lastSlash;
            }
            var segment = path.AsSpan(start, end - start).ToString();
            var slot = MatchSegment(segment);
            if (slot is not null)
            {
                return slot;
            }
            start = end + 1;
        }

        return MatchSegment(GetFileNameStem(path));
    }

    private static ReplacementSlot? MatchSegment(string segment)
    {
        if (segment.Length == 0)
        {
            return null;
        }

        if (s_exactOtherKeys.TryGetValue(segment, out var exactSlot))
        {
            return exactSlot;
        }
        return MatchPrefix(segment, s_otherKeys);
    }

    private static string GetFileNameStem(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        int start = lastSlash + 1;
        int dot = path.IndexOf('.', start);
        return dot >= 0 ? path[start..dot] : path[start..];
    }

    private static ReplacementSlot? MatchPrefix(string value, (string Key, ReplacementSlot Slot)[] keys)
    {
        foreach (var (key, slot) in keys)
        {
            if (value.StartsWith(key, StringComparison.Ordinal))
            {
                return slot;
            }
        }
        return null;
    }
}
