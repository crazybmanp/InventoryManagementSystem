using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace IMS;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class IMS : BaseUnityPlugin
{
	internal new static ManualLogSource Logger = null!;

	internal static ConfigEntry<int> ConfigMinBoxes = null!;
	internal static ConfigEntry<int> ConfigDaysToStock = null!;
	internal static ConfigEntry<int> ConfigAveragingDays = null!;
	internal static ConfigEntry<bool> ConfigIncludeDisplay = null!;
	internal static ConfigEntry<int> ConfigProtectedFunds = null!;
	internal static ConfigEntry<bool> ConfigAutostockDailyMorning = null!;
	internal static ConfigEntry<KeyboardShortcut> RestockTriggerKeybind = null!;
	internal static ConfigEntry<KeyboardShortcut> ConfigDeltaKeybind = null!;

	internal static string SaveFilePath = Application.persistentDataPath + "/IMS.json";

	private static InventoryManagementSystem? imsInstance;

	private void Awake()
	{
		// Plugin startup logic
		Logger = base.Logger;

		// Handle Config
		RestockTriggerKeybind =
			Config.Bind("Keybinds", "TriggerKeybind", new KeyboardShortcut(KeyCode.R, [KeyCode.LeftShift]));
		ConfigDeltaKeybind =
			Config.Bind("Keybinds", "DeltaKeybind", new KeyboardShortcut(KeyCode.RightBracket));
		ConfigMinBoxes = Config.Bind("Stock", "MinBoxes", 2,
			"Minimum Boxes to keep stocked");
		ConfigDaysToStock = Config.Bind("Stock", "StockDays", 2,
			"Number of days to stock supplies for");
		ConfigIncludeDisplay = Config.Bind("Stock", "IncludeDisplay", false,
			"Whether to include items that are on display in counts. The default of off will keep X days of product in stock in the back, true will keep X days of product total.");
		ConfigProtectedFunds = Config.Bind("Stock", "ProtectedFunds", 0,
			"Funds that are not allowed to be spent by the ordering system."); 
		ConfigAutostockDailyMorning = Config.Bind("Stock", "AutostockDailyMorning", false,
			"If set to true, the restock will be run at the top of every day.");

		ConfigAveragingDays = Config.Bind("Stats", "AveragingDays", 3, "Number of days to average sales over.");


		//Harmony Patches
		Harmony harmony = new Harmony("crzbp.IMS");
		harmony.PatchAll(typeof(IMS));
	}

	[HarmonyPatch(typeof(DayCycleManager), "Update")]
	[HarmonyPostfix]
	private static void OnDayUpdate(ref DayCycleManager __instance)
	{
		if (imsInstance is null)
		{
			if (!InventoryManagementSystem.TryLoad(out imsInstance))
			{
				imsInstance = new InventoryManagementSystem();
			}
		}

		if (RestockTriggerKeybind.Value.IsDown())
		{
			__instance.StartCoroutine(imsInstance.RunOrder());
		}

		if (ConfigDeltaKeybind.Value.IsDown())
		{
			imsInstance.LogOutliers();
		}
	}

	[HarmonyPatch(typeof(Checkout), "ProductScanned")]
	[HarmonyPostfix]
	private static void ProductScanned(Product product, bool cashier = false)
	{
		if (imsInstance is not null)
		{
			imsInstance.ProductSold(product);
		}
		else
		{
			Logger.LogError("No stock manager instance on product scanned!");
		}
	}

	[HarmonyPatch(typeof(DayCycleManager), "FinishTheDay")]
	[HarmonyPostfix]
	private static void OnFinishTheDay(ref DayCycleManager __instance)
	{
		if (imsInstance is not null)
		{
			imsInstance.RolloverSales();
		}
		else
		{
			Logger.LogError("No stock manager instance on day start!");
		}
	}

	[HarmonyPatch(typeof(DayCycleManager), "StartNextDay")]
	[HarmonyPostfix]
	private static void OnStartNextDay(ref DayCycleManager __instance)
	{
		if (!IMS.ConfigAutostockDailyMorning.Value)
			return;
		if (imsInstance is null)
		{
			return;
		}

		__instance.StartCoroutine(imsInstance.RunOrder());
	}

	[HarmonyPatch(typeof(SaveManager), "Save")]
	[HarmonyPostfix]
	private static void Save()
	{
		if (imsInstance is not null)
		{
			imsInstance.Save();
		}
		else
		{
			Logger.LogError("No stock manager instance on save!");
		}
	}
}