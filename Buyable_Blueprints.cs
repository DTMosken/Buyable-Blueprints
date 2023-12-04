using HarmonyLib;
using HMLLibrary;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;
using UnityEngine.SceneManagement;
using RaftModLoader;
using System.Runtime.CompilerServices;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;


public class BuyableBlueprints : Mod
{
    public static BuyableBlueprints instance;
    Harmony harmony;
    public bool buyBlueprints = false;
    public void Start()
    {
        instance = this;
        (harmony = new Harmony("com.MOSKEN.BuyableBlueprints")).PatchAll();
        Log("Mod has been loaded!");
    }

    public void OnModUnload()
    {
        Log("Mod has been unloaded!");
    }

    static HarmonyLib.Traverse ExtraSettingsAPI_Traverse;
    public bool ExtraSettingsAPI_Loaded = false;

    public void ExtraSettingsAPI_Load() // Occurs when the API loads the mod's settings
    {
        LoadSettings();
    }

    public void ExtraSettingsAPI_SettingsClose() // Occurs when user closes the settings menu
    {
        LoadSettings();
    }

    public void LoadSettings()
    {
        buyBlueprints = ExtraSettingsAPI_GetCheckboxState("buyBlueprints");
    }

    public bool ExtraSettingsAPI_GetCheckboxState(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded)
            return ExtraSettingsAPI_Traverse.Method("getCheckboxState", new object[] { this, SettingName }).GetValue<bool>();
        return false;
    }
}


[HarmonyPatch(typeof(TradingPostUI),nameof(TradingPostUI.Open))]
static class Patch_OpenTradingUI
{
    static int GetTier(Item_Base item) => item.UniqueName.Contains("Electric") || item.UniqueName.Contains("Titanium") ? 3 : item.UniqueName.Contains("Advanced") ? 2 : 1;
    static List<SO_TradingPost_Buyable.Instance> _b;
    public static List<SO_TradingPost_Buyable.Instance> CustomBuyables
    {
        get
        {
            if (_b == null)
            {
                _b = new List<SO_TradingPost_Buyable.Instance>();
                var b = new List<Item_Base>();
                Item_Base coin = null;
                Item_Base cube = null;
                foreach (var i in ItemManager.GetAllItems())
                    if (i.UniqueName.StartsWith("Blueprint_") && ComponentManager<Inventory_ResearchTable>.Value && ComponentManager<Inventory_ResearchTable>.Value.CanResearchBlueprint(i))
                        b.Add(i);
                    else if (i.UniqueName == "TradeToken")
                        coin = i;
                    else if (i.UniqueName == "Trashcube")
                        cube = i;

                foreach (var i in b)
                {
                    var t = GetTier(i);
                    _b.Add(new SO_TradingPost_Buyable.Instance()
                    {
                        cost = new[]
                        {
                            new Cost(cube, t * 2),
                            new Cost(coin, t * 2)
                        },
                        reward = new Cost(i, 1),
                        stock = 1,
                        tier = (TradingPost.Tier)Enum.Parse(typeof(TradingPost.Tier), "tier" + t, true)
                    });
                }
            }
            return _b;
        }
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var ind = code.FindIndex(x => x.opcode == OpCodes.Ldfld && x.operand is FieldInfo f && f.Name == "buyableItems");
        code.Insert(ind + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_OpenTradingUI), nameof(ModifyBuyableItems))));
        return code;
    }

    static List<SO_TradingPost_Buyable.Instance> ModifyBuyableItems(List<SO_TradingPost_Buyable.Instance> original)
    {
        if (!BuyableBlueprints.instance.buyBlueprints)
            return original;
        var l = original.ToList();
        foreach (var i in CustomBuyables)
            if (i.reward.item && !original.Any(x => x.reward.item && x.reward.item.UniqueIndex == i.reward.item.UniqueIndex))
                l.Add(i);
        return l;
    }
}

[HarmonyPatch(typeof(TradingPost), nameof(TradingPost.PurchaseItem))]
static class Patch_BuyTradingItem
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var ind = code.FindIndex(x => x.opcode == OpCodes.Stloc_0);
        code.InsertRange(ind, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_BuyTradingItem), nameof(DefaultInstance)))
        });
        return code;
    }

    static SO_TradingPost_Buyable.Instance DefaultInstance(SO_TradingPost_Buyable.Instance original, int itemIndex)
    {
        if (!BuyableBlueprints.instance.buyBlueprints)
            return original;
        foreach (var i in Patch_OpenTradingUI.CustomBuyables)
            if (i.reward.item && i.reward.item.UniqueIndex == itemIndex)
                return i;
        return original;
    }
}