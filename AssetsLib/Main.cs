using BepInEx;
using HarmonyLib;
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using Moonlighter.DungeonGeneration;

namespace AssetsLib
{
    [BepInPlugin("Aidanamite.AssetsLib", "AssetsLib", "1.0.8")]
    internal class Main : BaseUnityPlugin
    {
        internal static Assembly modAssembly = Assembly.GetExecutingAssembly();
        internal static string modName = $"{modAssembly.GetName().Name}";
        internal static string modDir = $"{Environment.CurrentDirectory}\\BepInEx\\{modName}";
        internal static Harmony harmony;
        static Dictionary<Type, Dictionary<string, UnityEngine.Object>> customAssets = new Dictionary<Type, Dictionary<string, UnityEngine.Object>>();
        internal static Dictionary<string, Dictionary<string, string>> translations = new Dictionary<string, Dictionary<string, string>>();
        internal static List<ItemMaster> items = new List<ItemMaster>();
        internal static List<EnchantmentRecipe> enchantments = new List<EnchantmentRecipe>();
        internal static List<RecipeCollection> blacksmithRecipes = new List<RecipeCollection>();
        internal static List<RecipeCollection> witchRecipes = new List<RecipeCollection>();
        internal static Dictionary<string, SpawnWeight> overrideSpawnData = new Dictionary<string, SpawnWeight>();
        internal static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            harmony = new Harmony($"com.Aidanamite.{modName}");
            harmony.PatchAll(modAssembly);
            Logger.LogInfo($"{modName} has loaded");
        }

        internal static bool TryGetCustomAsset<T>(string name, out T value) where T : UnityEngine.Object
        {
            value = null;
            if (!TryGetCustomAsset(typeof(T), name, out UnityEngine.Object v))
                return false;
            value = v as T;
            return true;
        }
        internal static bool TryGetCustomAsset(Type type, string name, out UnityEngine.Object value)
        {
            //Debug.Log($"Requested asset \"{name}\" of type {type.Name}");
            value = null;
            if (name.IsNullOrWhiteSpace() || !customAssets.ContainsKey(type))
                return false;
            var set = customAssets[type];
            if (!set.ContainsKey(name))
                return false;
            //Debug.Log($"Found asset in custom set. returning");
            value = set[name];
            return true;
        }
        internal static string RegisterAsset<T>(string name, T obj) where T : UnityEngine.Object
        {
            if (!obj)
                throw new ArgumentNullException("Argument \"obj\" cannot be null");
            var b = GetBundleSet(typeof(T));
            if (b.ContainsKey(name))
                throw new ExistingRegistryException(name);
            b.Add(name, obj);
            return name;
        }

        internal static Dictionary<string, UnityEngine.Object> GetBundleSet(Type type)
        {
            if (customAssets.ContainsKey(type))
                return customAssets[type];
            var c = new Dictionary<string, UnityEngine.Object>();
            customAssets.Add(type, c);
            //harmony.Patch(typeof(ItemDatabase).GetMethod("LoadFromBundle").MakeGenericMethod(type), new HarmonyMethod(typeof(Main).GetMethod("GetAssetPatch",BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(type)));
            //harmony.Patch(typeof(PrefabRegister).GetMethod("Get").MakeGenericMethod(type), new HarmonyMethod(typeof(Main).GetMethod("GetAssetPatch", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(type)));
            return c;
        }

        internal static bool GetAssetPatch<T>(string name, ref T __result) where T : UnityEngine.Object => !TryGetCustomAsset<T>(name, out __result);
        //internal static bool GetAssetPatch(MethodBase __originalMethod, string __0, ref UnityEngine.Object __result) => !TryGetCustomAsset(__originalMethod.GetGenericArguments()[0], __0, out __result);
    }

    public static class AssetsLibTools
    {
        public static string RegisterLocalization(string name, Dictionary<string, string> values)
        {
            name = Assembly.GetCallingAssembly().GetName().Name + "." + name;
            if (Main.translations.ContainsKey(name))
                throw new ExistingRegistryException(name);
            Main.translations.Add(name, values);
            if (!values.ContainsKey("default"))
                Main.Log.LogWarning($"No \"default\" key found in registered localization \"{name}\". This may cause issues");
            return name;
        }

        public static Texture2D LoadImage(string filename, int width, int height)
        {
            var a = Assembly.GetCallingAssembly();
            var spriteData = a.GetManifestResourceStream(a.GetName().Name + "." + filename);
            var rawData = new byte[spriteData.Length];
            spriteData.Read(rawData, 0, rawData.Length);
            var tex = new Texture2D(width, height);
            tex.LoadImage(rawData);
            tex.filterMode = FilterMode.Point;
            return tex;
        }

        public static string RegisterAsset<T>(string name, T obj) where T : UnityEngine.Object => Main.RegisterAsset(Assembly.GetCallingAssembly().GetName().Name + "." + name, obj);
        public static string RegisterEffect<T>() where T : MoonlighterEffect { Main.RegisterAsset(typeof(T).Name, new GameObject(typeof(T).Name, typeof(T))); return typeof(T).Name; }
        public static StatsModificator CreateStatModifier(int Health = 0, int Defence = 0, int Speed = 0, int MeleeDamage = 0, int RangedDamage = 0, Action<StatsModificator, int> PlusLevelModifier = null)
            => new CustomStatsModificator() { armor = Defence, health = Health, speed = Speed, intelligence = RangedDamage, strength = MeleeDamage, generator = PlusLevelModifier };

        public static void CreateAndRegisterEnchantmentRecipe(EquipmentItemMaster Item,
            StatsModificator StatModifier, int Cost, List<RecipeIngredient> Ingredients,
            int MinPlusLevel = 0, string EffectAssetName = null)
        {

            if (Patch_RecipeManager_LoadFromFile.HasLoaded)
                throw new AccessViolationException("Cannot register a new enchantment recipe at this time");
            if (Item == null)
                throw new ArgumentNullException("Item cannot be null");
            Main.enchantments.Add(new EnchantmentRecipe
            {
                itemName = Item.name,
                enchantmentElementalName = EffectAssetName,
                enchantmentType = (string.IsNullOrEmpty(EffectAssetName) || EffectAssetName.IsNullOrWhiteSpace()) ? EnchantmentRecipe.EnchantmentType.Standard : EnchantmentRecipe.EnchantmentType.Elemental,
                ingredients = Ingredients,
                labourCost = Cost,
                plusLevel = MinPlusLevel,
                stats = StatModifier,
            });
        }

        public static Recipe CreateRecipe(ItemMaster CraftedItem,
            List<RecipeIngredient> Ingredients, int Cost,
            int MinPlusLevel = 0, bool PriceIsFixed = true,
            int SortingIndex = int.MaxValue, bool UnlockedAtStart = true,
            string PlaceAfterItem = null)
            => new Recipe {
                craftedItemName = CraftedItem.name,
                craftedItemNameKey = CraftedItem.nameKey,
                hasFixedPrice = PriceIsFixed,
                ingredients = Ingredients,
                labourCostGold = Cost,
                plusLevel = MinPlusLevel,
                sorting = SortingIndex,
                unlockedAtStart = UnlockedAtStart
            };
        public static void RegisterBlacksmithRecipeSet(string Id, params Recipe[] Recipes)
        {
            if (Patch_RecipeManager_LoadFromFile.HasLoaded)
                throw new AccessViolationException("Cannot register a new blacksmith recipe at this time");
            if (Recipes.Length == 0)
                throw new ArgumentNullException("No recipes specified");
            if (string.IsNullOrEmpty(Id))
                throw new ArgumentNullException("Given recipe id is invalid");
            if (Main.blacksmithRecipes.Exists((x) => x.collectionName == Id))
                Main.blacksmithRecipes.Find((x) => x.collectionName == Id).recipes.AddRange(Recipes);
            else
                Main.blacksmithRecipes.Add(new RecipeCollection
                {
                    collectionName = Id,
                    recipes = new List<Recipe>(Recipes)
                });
        }
        public static void RegisterWitchRecipeSet(string Id, params Recipe[] Recipes)
        {
            if (Patch_RecipeManager_LoadFromFile.HasLoaded)
                throw new AccessViolationException("Cannot register a new witch recipe at this time");
            if (Recipes.Length == 0)
                throw new ArgumentNullException("No recipes specified");
            if (string.IsNullOrEmpty(Id))
                throw new ArgumentNullException("Given recipe id is invalid");
            if (Main.witchRecipes.Exists((x) => x.collectionName == Id))
                Main.witchRecipes.Find((x) => x.collectionName == Id).recipes.AddRange(Recipes);
            else
                Main.witchRecipes.Add(new RecipeCollection
                {
                    collectionName = Id,
                    recipes = new List<Recipe>(Recipes)
                });
        }
    }

    public static class RecipeIds
    {
        public static class Blacksmith
        {
            public const string Helmets = "headArmors";
            public const string Chestplates = "bodyArmors";
            public const string Boots = "feetArmors";
            public const string ShortSwords = "shortSwords";
            public const string BigSwords = "bigSwords";
            public const string Spears = "spears";
            public const string Gloves = "gloves";
            public const string Bows = "bows";
        }
        public static class Witch
        {
            public const string WandererLevel1to2 = "dungeonLvl1to2Potions";
            public const string WandererLevel3to4 = "dungeonLvl3to4Potions";
            public const string WandererLevel5to6 = "dungeonLvl5to6Potions";
            public const string WandererLevel7to8 = "dungeonLvl7to8Potions";
            public const string WandererLevel9to10 = "dungeonLvl9to10Potions";
            public const string Potions = "potions";
        }
    }

    public class ExistingRegistryException : Exception { public ExistingRegistryException(string message) : base($"An asset has already been registered with the name \"{message}\"") { } }

    public static class ExtentionMethods
    {
        public static Sprite CreateSprite(this Texture2D texture) => Sprite.Create(texture, new Rect(0,0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 1);

        public static void RegisterItem(this ItemMaster Item)
        {
            if (Patch_ItemDatabase_LoadItems.HasLoaded)
                throw new AccessViolationException("Cannot register a new item at this time");
            else if (!Main.items.Exists((x) => x.name == Item.name))
                Main.items.Add(Item);
        }
        public static T MemberwiseClone<T>(this T obj)
        {
            foreach (var constructor in obj.GetType().GetConstructors((BindingFlags)(-1)))
                if (constructor.GetParameters().Length == 0 && !constructor.ContainsGenericParameters)
                {
                    var nObj = constructor.Invoke(new object[0]);
                    var t = obj.GetType();
                    while (t != typeof(object))
                    {
                        foreach (var f in t.GetFields((BindingFlags)(-1)))
                            if (!f.IsStatic)
                                f.SetValue(nObj,f.GetValue(obj));
                        t = t.BaseType;
                    }
                    return (T)nObj;
                }
            return default(T);
        }
        public static List<T> MemberwiseClone<T>(this IEnumerable<T> collection, Predicate<T> predicate = null)
        {
            var l = new List<T>();
            foreach (var i in collection)
                if (predicate == null || predicate.Invoke(i))
                    l.Add(i.MemberwiseClone());
            return l;
        }
        public static void OverrideChestSpawns(this ItemMaster Item, SpawnWeight weightData)
        {
            if (Main.overrideSpawnData.ContainsKey(Item.name))
                Main.overrideSpawnData[Item.name] = weightData;
            Main.overrideSpawnData.Add(Item.name, weightData);
        }
        public static void RemoveOverrideChestSpawns(this ItemMaster Item) => Main.overrideSpawnData.Remove(Item.name);
        public static void SetupBasicItem(this ItemMaster item, string Id,
            string NameLocalizationKey, string DescriptionLocalizationKey, string SpriteAssetName,
            int GoldValue = 0, ItemMaster.Culture Culture = ItemMaster.Culture.Merchant,
            bool CanAppearInChest = false, float SpawnWeight = 1,
            int MaxStackSize = 1, int MaxSpawnStack = 1, int MinSpawnStack = 1,
            int MinPlusLevel = 0, ItemMaster.Tier Tier = ItemMaster.Tier.Tier1,
            int WandererWeaponGoldCost = 0, int WandererWeaponSlimeCost = 0,
            bool DestroyedOnDungeonExit = false)
        {
            item.canAppearInChest = CanAppearInChest;
            item.chestWeight = SpawnWeight;
            item.culture = Culture;
            item.description = "???";
            item.descriptionKey = DescriptionLocalizationKey;
            item.fixedChestStack = MaxSpawnStack == MinSpawnStack ? MinSpawnStack : 0;
            item.goldValue = GoldValue;
            item.isDestroyedOnRunEnded = DestroyedOnDungeonExit;
            item.maxChestStack = MaxSpawnStack;
            item.maxStack = MaxStackSize;
            item.minChestStack = MinSpawnStack;
            item.name = Assembly.GetCallingAssembly().GetName().Name + "." + Id;
            item.nameKey = NameLocalizationKey;
            item.plusLevel = MinPlusLevel;
            item.tier = Tier;
            item.wandererWeaponGoldCost = WandererWeaponGoldCost;
            item.wandererWeaponSlimeCost = WandererWeaponSlimeCost;
            item.worldSpriteName = SpriteAssetName;
        }
        public static void SetupEquipmentItem(this EquipmentItemMaster item, StatsModificator StatModifier,
            string WearingArtAssetName = null, string ExtraWearingArtAssetName = null,
            Color ExtraWearingBaseColor1 = default(Color), Color ExtraWearingBaseColor2 = default(Color), Color ExtraWearingBaseColor3 = default(Color),
            Color ExtraWearingFinalColor1 = default(Color), Color ExtraWearingFinalColor2 = default(Color), Color ExtraWearingFinalColor3 = default(Color))
        {
            item.stats = StatModifier;
            item.inGameArtPrefabName = WearingArtAssetName;
            item.inGameExtraPieceArtPrefabName = ExtraWearingArtAssetName;
            item.extraPieceBaseColor1 = ExtraWearingBaseColor1;
            item.extraPieceBaseColor2 = ExtraWearingBaseColor2;
            item.extraPieceBaseColor3 = ExtraWearingBaseColor3;
            item.extraPieceFinalColor1 = ExtraWearingFinalColor1;
            item.extraPieceFinalColor2 = ExtraWearingFinalColor2;
            item.extraPieceFinalColor3 = ExtraWearingFinalColor3;
        }
        public static void SetupAmuletItem(this AmuletEquipmentMaster item,
            string EffectAssetName, float SpawnChance = 0.2f,
            bool IsDroppedByBoss = false, bool CanRespawnIfLost = false)
        {
            item.equipmentSlot = EquipmentItemMaster.EquipmentSlot.Amulet;
            item.amuletEffectName = EffectAssetName;
            item.appearProbability = SpawnChance;
            item.bossDrop = IsDroppedByBoss;
            item.canAppearMoreThanOnce = CanRespawnIfLost;
        }
        public static void SetupWeaponItem(this WeaponEquipmentMaster item,
            string EffectAssetName,
            float MainAttackCooldown, float SecondaryAttackCooldown,
            float MainAttackBackwardsPushForce, float SecondaryAttackBackwardsPushForce,
            string MainAttackCombo1EffectAssetName, string MainAttackCombo2EffectAssetName, string MainAttackCombo3EffectAssetName,
            string SecondaryAttackEffectAssetName, string SecondaryChargedAttackEffectAssetName, float SecondaryAttackDamageMultiplier,
            string MissAttackEffect)
        {
            item.equipmentSlot = EquipmentItemMaster.EquipmentSlot.Weapon;
            item.combatEffect = EffectAssetName;
            item.mainAttackCooldown = MainAttackCooldown;
            item.mainAttackEffectCombo1 = MainAttackCombo1EffectAssetName;
            item.mainAttackEffectCombo2 = MainAttackCombo2EffectAssetName;
            item.mainAttackEffectCombo3 = MainAttackCombo3EffectAssetName;
            item.mainAttackPushBackwardsForce = MainAttackBackwardsPushForce;
            item.missAttackEffect = MissAttackEffect;
            item.secondaryAttackChargedEffect = SecondaryChargedAttackEffectAssetName;
            item.secondaryAttackCooldown = SecondaryAttackCooldown;
            item.secondaryAttackDamageMultiplier = SecondaryAttackDamageMultiplier;
            item.secondaryAttackEffect = SecondaryAttackEffectAssetName;
            item.secondaryAttackPushBackwardsForce = SecondaryAttackBackwardsPushForce;
        }
    }

    internal class CustomStatsModificator : StatsModificator
    {
        public Action<StatsModificator, int> generator = null;
        public void Generate(int plusLevel)
        {
            if (generator == null)
                return;
            generator(this,plusLevel);
        }
    }

    public struct SpawnWeight
    {
        internal OverrideType type;
        internal Dictionary<WeightContext, float> manual;
        internal WeightColleciton<ItemMaster.Culture> genericCulture;
        internal WeightColleciton<ItemMaster.Tier> genericTier;
        internal float genericWeight;
        public SpawnWeight(WeightColleciton<ItemMaster.Culture> culture, WeightColleciton<ItemMaster.Tier> tier, float weight)
        {
            type = OverrideType.Generic;
            genericCulture = culture;
            genericTier = tier;
            genericWeight = weight;
            manual = null;
        }
        public SpawnWeight(Dictionary<WeightContext, float> weights)
        {
            type = OverrideType.Manual;
            genericCulture = new WeightColleciton<ItemMaster.Culture>();
            genericTier = new WeightColleciton<ItemMaster.Tier>();
            genericWeight = 0;
            manual = weights;
        }
        public float GetChance(Chest.chestType Chest, ItemMaster.Culture Culture, ItemMaster.Tier Tier)
        {
            if (type == OverrideType.Generic)
                return (genericCulture == Culture && genericTier == Tier) ? genericWeight : 0;
            float f = 0;
            foreach (var pair in manual)
                if (pair.Key.Contains(Chest, Culture, Tier) && f < pair.Value)
                    f = pair.Value;
            return f;
        }
    }

    public struct WeightColleciton<T> where T : Enum
    {
        private List<T> cultures;
        private bool all;
        public WeightColleciton(params T[] Cultures)
        {
            cultures = new List<T>(Cultures);
            all = false;
        }
        public WeightColleciton(params int[] Cultures)
        {
            cultures = new List<T>();
            all = false;
            foreach (var v in Cultures)
                if (v == -1)
                {
                    all = true;
                    cultures.Clear();
                    break;
                }
                else
                    cultures.Add((T)(object)v);
        }
        public static implicit operator WeightColleciton<T>(T obj) => new WeightColleciton<T>(obj);
        public static implicit operator WeightColleciton<T>(int obj) => new WeightColleciton<T>(obj);
        public bool Contains(T culture) => all || (int)(object)culture == -1 || cultures.Contains(culture);
        public static bool operator ==(WeightColleciton<T> a, T b) => a.Contains(b);
        public static bool operator !=(WeightColleciton<T> a, T b) => !(a == b);
        public static bool operator ==(T b, WeightColleciton<T> a) => a == b;
        public static bool operator !=(T b, WeightColleciton<T> a) => !(a == b);
    }

    public struct WeightContext
    {
        internal WeightColleciton<Chest.chestType> chest;
        internal WeightColleciton<ItemMaster.Culture> culture;
        internal WeightColleciton<ItemMaster.Tier> tier;
        public WeightContext(WeightColleciton<Chest.chestType> Chest, WeightColleciton<ItemMaster.Culture> Culture, WeightColleciton<ItemMaster.Tier> Tier)
        {
            chest = Chest;
            culture = Culture;
            tier = Tier;
        }
        public bool Contains(Chest.chestType Chest, ItemMaster.Culture Culture, ItemMaster.Tier Tier) => chest == Chest && culture == Culture && tier == Tier;
    }

    internal enum OverrideType
    {
        Generic,
        Manual
    }

    [HarmonyPatch(typeof(ItemDatabase), "ReadFile")]
    internal class Patch_ItemDatabase_LoadItems
    {
        public static bool HasLoaded = false;
        public static void Postfix()
        {
            HasLoaded = true;
            for (int i = 0; i < ItemDatabase.Instance.itemCollections.Length; i++)
            {
                List<ItemMaster> added = new List<ItemMaster>();
                foreach (var item in Main.items)
                    if (item.plusLevel == i)
                        ItemDatabase.Instance.itemCollections[i].items.Add(item);
                    else if (item.plusLevel < i)
                    {
                        var newItem = item.MemberwiseClone();
                        newItem.plusLevel = i;
                        if (newItem is EquipmentItemMaster && (newItem as EquipmentItemMaster).stats is CustomStatsModificator)
                        {
                            var e = newItem as EquipmentItemMaster;
                            e.stats = e.stats.MemberwiseClone();
                            (e.stats as CustomStatsModificator).Generate(i);
                        }
                        added.Add(newItem);
                        ItemDatabase.Instance.itemCollections[i].items.Add(newItem);
                    }
                ItemDatabase.PrepareNewGamePlusItems(added, i);
            }
        }
    }

    [HarmonyPatch(typeof(RecipeManager), "LoadFromFile")]
    internal class Patch_RecipeManager_LoadFromFile
    {
        public static bool HasLoaded = false;
        public static void Postfix()
        {
            HasLoaded = true;
            for (int i = 0; i < RecipeManager.Instance.originalData.Length; i++)
            {
                var brcl = new List<RecipeCollection>();
                foreach (var r in Main.blacksmithRecipes)
                    brcl.Add(new RecipeCollection { collectionName = r.collectionName, recipes = r.recipes.MemberwiseClone((x) => x.plusLevel <= i) });
                var wrcl = new List<RecipeCollection>();
                foreach (var r in Main.witchRecipes)
                    wrcl.Add(new RecipeCollection { collectionName = r.collectionName, recipes = r.recipes.MemberwiseClone((x) => x.plusLevel <= i) });
                var erl = Main.enchantments.MemberwiseClone((x) => x.plusLevel <= i);
                var data = new RecipeManager.RecipeJSONData() { blacksmithRecipes = brcl, enchantmentRecipes = erl, witchRecipes = wrcl };
                if (i > 0)
                    RecipeManager.Instance.UpgradeRecipeJSonToPlus(data, i);
                foreach (var r in data.enchantmentRecipes)
                    if (r.stats is CustomStatsModificator)
                    {
                        r.stats = r.stats.MemberwiseClone();
                        (r.stats as CustomStatsModificator).Generate(i);
                    }
                RecipeManager.Instance.originalData[i].enchantmentRecipes.AddRange(data.enchantmentRecipes);
                foreach (var r in data.witchRecipes)
                    if (RecipeManager.Instance.originalData[i].witchRecipes.Exists((x) => x.collectionName == r.collectionName))
                        RecipeManager.Instance.originalData[i].witchRecipes.Find((x) => x.collectionName == r.collectionName).recipes.AddRange(r.recipes);
                    else
                        RecipeManager.Instance.originalData[i].witchRecipes.Add(r);
                foreach (var r in data.blacksmithRecipes)
                    if (RecipeManager.Instance.originalData[i].blacksmithRecipes.Exists((x) => x.collectionName == r.collectionName))
                        RecipeManager.Instance.originalData[i].blacksmithRecipes.Find((x) => x.collectionName == r.collectionName).recipes.AddRange(r.recipes);
                    else
                        RecipeManager.Instance.originalData[i].blacksmithRecipes.Add(r);
            }
        }
    }

    [HarmonyPatch(typeof(BlacksmithPanel), "CompareArmors")]
    internal class Patch_BlacksmithRecipeSorting
    {
        static bool Prefix(Recipe x, Recipe y, ref int __result)
        {
            EquipmentItemMaster equipmentItemMaster = x.GetCraftedItem() as EquipmentItemMaster;
            EquipmentItemMaster equipmentItemMaster2 = y.GetCraftedItem() as EquipmentItemMaster;
            if ((x.LoadedFromDLC == y.LoadedFromDLC) && equipmentItemMaster.equipmentSlot == equipmentItemMaster2.equipmentSlot)
            {
                __result = x.sorting - y.sorting;
                if (__result != 0)
                    return false;
            }
            return true;
        }
    }

    /*[HarmonyPatch(typeof(ItemDatabase), "GetItemByName", new Type[] { typeof(string), typeof(int) })]
    internal class PatchGet { static void Postfix(string name, int plusLevel, ItemMaster __result) => Debug.Log($"Method call ItemDatabase.GetItemByName(string = \"{name}\", int = {plusLevel}) = {(__result == null ? "null" : __result.displayName)}"); }

    [HarmonyPatch(typeof(ItemDatabase), "GetItems")]
    internal class PatchGets { static void Postfix(int plusLevel, List<ItemMaster> __result) => Debug.Log($"Method call ItemDatabase.GetItems(Predicate = ?, int = {plusLevel}) = [{__result.Join((x) => x.name)}]"); }*/

    [HarmonyPatch(typeof(I2.Loc.LanguageSourceData), "TryGetTranslation")]
    internal class Patch_Localization
    {
        public static bool Prefix(string term, ref string Translation, string overrideLanguage, ref bool __result)
        {
            var lang = (overrideLanguage != null) ? overrideLanguage : I2.Loc.LocalizationManager.CurrentLanguage;
            if (!Main.translations.ContainsKey(term))
                return true;
            var set = Main.translations[term];
            lang = set.ContainsKey(lang) ? lang : "default";
            if (!set.ContainsKey(lang))
                return true;
            Translation = set[lang];
            __result = true;
            return false;
        }
    }

    // -----------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(ItemDatabase),"GetSpriteByItemName")]
    internal class PatchSpriteByName
    {
        static bool Prefix(string name, ref Sprite __result) => !Main.TryGetCustomAsset(name, out __result);
    }

    [HarmonyPatch(typeof(ItemDatabase), "GetSprite")]
    internal class PatchSpriteByItem
    {
        static bool Prefix(ItemMaster master, ref Sprite __result) { __result = (master == null) ? null : ItemDatabase.GetSpriteByItemName(master.worldSpriteName); return false; }
    }

    [HarmonyPatch(typeof(ItemDatabase), "GetPrefab")]
    internal class PatchItemPrefab
    {
        static bool Prefix(string name, ref GameObject __result) => !Main.TryGetCustomAsset(name, out __result);
    }

    [HarmonyPatch(typeof(PrefabRegister), "GetPrefabByName")]
    internal class PatchPrefab
    {
        static bool Prefix(string prefabName, ref GameObject __result) => !Main.TryGetCustomAsset(prefabName, out __result);
    }
    // -----------------------------------------------------------------------------------------------------------


    [HarmonyPatch(typeof(ItemDatabase), "GetItems")]
    internal class PatchGetItems
    {
        internal static bool overrideReturn = false;
        static void Prefix(ref Predicate<ItemMaster> predicate, Predicate<ItemMaster> __0)
        {
            if (overrideReturn)
            {
                if (predicate == null)
                    predicate = (x) => !Main.overrideSpawnData.ContainsKey(x.name);
                else
                    predicate = (x) => __0.Invoke(x) && !Main.overrideSpawnData.ContainsKey(x.name);
            }
        }
    }

    [HarmonyPatch(typeof(ChestGeneration), "RefreshWeightLists")]
    internal class PatchRefreshNormalWeights
    {
        internal static void Prefix() => PatchGetItems.overrideReturn = true;
        internal static void Postfix()
        {
            PatchGetItems.overrideReturn = false;
            if (Main.overrideSpawnData.Count == 0)
                return;
            int plusLevel = 0;
            if (GameManager.Instance)
                plusLevel = GameManager.Instance.GetCurrentGamePlusLevel();
            var customSpawns = new Dictionary<ItemMaster, SpawnWeight>();
            foreach (var spawn in Main.overrideSpawnData)
            {
                var i = ItemDatabase.GetItemByName(spawn.Key, plusLevel);
                if (i != null)
                    customSpawns.Add(i, spawn.Value);
            }
            List<List<float>> constantsProbArrays = ChestGeneration.GetConstantsProbArrays();
            foreach (var chestPair in ChestGeneration._weightItemLists)
                foreach (var culturePair in chestPair.Value)
                {
                    var tiers = culturePair.Value.Items();
                    bool flag = false;
                    var constants = constantsProbArrays[(int)chestPair.Key];
                    int ind = -1;
                    for (int tier = 0; tier < constants.Count; tier++)
                    {
                        if (constants[tier] <= 0)
                            continue;
                        ind++;
                        foreach (var spawn in customSpawns)
                        {
                            var c = spawn.Value.GetChance(chestPair.Key, culturePair.Key, (ItemMaster.Tier)tier);
                            if (c > 0)
                            {
                                flag = true;
                                tiers[ind].AddItem(spawn.Key, c);
                            }
                        }
                    }
                    if (flag)
                        culturePair.Value.SetItems(tiers, culturePair.Value.Weights());
                }
        }
    }
    [HarmonyPatch(typeof(ChestGeneration), "RefreshWandererWeightLists")]
    internal class PatchRefreshWandererWeights
    {
        static void Prefix() => PatchRefreshNormalWeights.Prefix();
        static void Postfix() => PatchRefreshNormalWeights.Postfix();
    }
}