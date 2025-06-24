using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using MyBox;
using Newtonsoft.Json;
using UnityEngine;

namespace IMS;

public class InventoryManagementSystem
{
	public const float OrderWaitSeconds = 1;

	public static IDManager IdManager = Singleton<IDManager>.Instance;

	public List<StockRecord> StockRecords;

	public InventoryManagementSystem()
	{
		List<ProductSO> products = IdManager.Products;

		IMS.Logger.LogInfo($"Inventory Management System Initializing, adding {products.Count} products.");
		IMS.Logger.LogInfo($"If you are seeing this message multiple times, there is an issue with Inventory Management System, discontinue use.");

		StockRecords = new List<StockRecord>();
		foreach (ProductSO product in products)
		{
			StockRecords.Add(new StockRecord(product));
		}
	}

	public InventoryManagementSystem(List<StockRecordDTO> records)
	{
		List<ProductSO> products = IdManager.Products;

		StockRecords = new List<StockRecord>();

		foreach (StockRecordDTO stockRecordDTO in records)
		{
			StockRecords.Add(new StockRecord(stockRecordDTO));
		}
	}

	public IEnumerator RunOrder()
	{
		PriceManager priceManager = Singleton<PriceManager>.Instance;
		MoneyManager moneyManager = Singleton<MoneyManager>.Instance;
		CartManager cartManager = Singleton<CartManager>.Instance;

		List<OrderItem> order = new List<OrderItem>();

		foreach (StockRecord stockRecord in StockRecords)
		{
			//Check if item is being displayed
			if (!stockRecord.OnDisplay)
			{
				continue;
			}

			//Get order requirement
			float requiredBoxes = stockRecord.TargetStockBoxes;
			float averageBoxes = stockRecord.AverageSoldBoxes();
			float inStorageBoxes = stockRecord.ConvertToBoxCount(stockRecord.GetCurrentStorageCount());
			
			if (stockRecord.StockDelta > 0)
			{
				IMS.Logger.LogInfo($"{inStorageBoxes,5:N2}/{requiredBoxes,5:N2} supply based on average usage of {averageBoxes,5:N2} boxes per day for Item {stockRecord.ProductNamePrint()}");

				order.Add(new OrderItem
				{
					Id = stockRecord.Id,
					Name = stockRecord.Name,
					BoxCount = (int)Math.Ceiling(requiredBoxes - inStorageBoxes),
					PricePerItem = priceManager.CurrentCost(stockRecord.Id),
					PricePerBox = IdManager.ProductSO(stockRecord.Id).BoxPrice
				});
			}
		}

		if (order.Count < 1)
		{
			IMS.Logger.LogInfo($"No new order, all items are in stock!");
			yield break;
		}

		order = order.OrderByDescending(e => e.BoxCount).ToList();
		IMS.Logger.LogInfo($"New Order Generated:");
		foreach (OrderItem orderItem in order)
		{
			IMS.Logger.LogInfo(orderItem);
		}
		float totalPrice = order.Sum(e => e.PricePerBox * e.BoxCount);
		IMS.Logger.LogInfo($"Total price: {totalPrice:N2}");

		if (cartManager.MarketShoppingCart.TooLateToOrderGoods) //This could be moved up to the top for efficiency, but reporting is nice.
		{
			IMS.Logger.LogInfo("Cannot order at this time.");
			Singleton<ScannerDevice>.Instance.PlayAudio(true);
			yield break;
		}

		float startingMoney = moneyManager.Money;

		//ordering logic
		while (order.Count > 0)
		{
			OrderItem item = order.MaxBy(e => e.BoxCount);
			
			if ((moneyManager.Money - IMS.ConfigProtectedFunds.Value) < cartManager.MarketShoppingCart.GetTotalPrice() +
			    cartManager.MarketShoppingCart.CurrentShippingCost + item.PricePerBox)
			{
				if (cartManager.MarketShoppingCart.ItemCountInCart > 0)
				{
					cartManager.MarketShoppingCart.Purchase(false);
				}

				IMS.Logger.LogWarning($"Could not purchase all items! Player has ${moneyManager.Money}(${IMS.ConfigProtectedFunds.Value} is reserved leaving ${moneyManager.Money - IMS.ConfigProtectedFunds.Value}. Cart currently costs ${cartManager.MarketShoppingCart.GetTotalPrice() +
					cartManager.MarketShoppingCart.CurrentShippingCost} new item is {item.Name} ${item.PricePerBox}");
				yield break;
			}

			cartManager.AddCart(new ItemQuantity(item.Id, item.PricePerItem), SalesType.PRODUCT);
			//IMS.Logger.LogInfo($"Ordering one of: {item.Name}");
			item.BoxCount--;
			if (item.BoxCount < 1)
			{
				//IMS.Logger.LogInfo($"Removing: {item}");
				order.RemoveAll(e=>e.Id == item.Id);
			}

			//IMS.Logger.LogInfo($"Cart has {cartManager.MarketShoppingCart.ItemCountInCart}/{cartManager.MarketShoppingCart.m_MaxItemCount}");
			if (cartManager.MarketShoppingCart.CartMaxed(true))
			{
				cartManager.MarketShoppingCart.Purchase(false);
				IMS.Logger.LogInfo($"IMS order processing part (Cart maxed)");
				yield return new WaitForSeconds(OrderWaitSeconds);
			}
			else
			{
				yield return null;
			}
		}

		if (cartManager.MarketShoppingCart.ItemCountInCart > 0)
		{
			cartManager.MarketShoppingCart.Purchase(false);
		}
		IMS.Logger.LogInfo($"IMS order complete!");
	}

	public void RolloverSales()
	{
		foreach (StockRecord stockRecord in StockRecords)
		{
			stockRecord.RolloverDay();
		}

		List<StockRecord> data = StockRecords.OrderByDescending(e => e.AverageSoldBoxes()).ToList();
		foreach (StockRecord stockRecord in data)
		{
			IMS.Logger.LogInfo(stockRecord.RollingDayInfo());
		}
	}

	public void ProductSold(Product product)
	{
		StockRecord? stockEntry = StockRecords.FirstOrDefault(e => e.Id == product.ProductSO.ID);

		if (stockEntry is null)
		{
			IMS.Logger.LogError($"No stock record found for product ({product.ProductSO.ID}){product.ProductSO.ProductName}");
			return;
		}

		product.TryGetComponent<ProductPaperBag>(out ProductPaperBag? component);

		stockEntry.AddSale(component?.Count);
	}

	public static bool TryLoad([NotNullWhen(true)]out InventoryManagementSystem? imsInstance)
	{
		imsInstance = null;

		try
		{
			if (File.Exists(IMS.SaveFilePath))
			{
				string contents = File.ReadAllText(IMS.SaveFilePath);

				List<StockRecordDTO>? saveData = JsonConvert.DeserializeObject<List<StockRecordDTO>>(contents);
				if (saveData is null)
				{
					throw new Exception("Save data could not load correctly");
				}

				imsInstance = new InventoryManagementSystem(saveData);

				IMS.Logger.LogInfo($"Loaded Inventory Data from {IMS.SaveFilePath}");
				return true;
			}
			else
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			IMS.Logger.LogError($"Error loading IMS data:\n{ex.Message}");
			return false;
		}
	}

	public void Save()
	{
		List<StockRecordDTO> saveData = StockRecords.Select(e => e.ToDTO()).ToList();

		try
		{
			string contents = JsonConvert.SerializeObject(saveData, Formatting.Indented);
			File.WriteAllText(IMS.SaveFilePath, contents);
		}
		catch (Exception ex)
		{
			IMS.Logger.LogError($"Cannot save data Data at {IMS.SaveFilePath} {ex.Message}");
		}

		IMS.Logger.LogInfo($"Saved Inventory Data to {IMS.SaveFilePath}");
	}

	public (List<StockRecord> top, List<StockRecord> bottom) GetStockDeltaOutliers(int topLimit = 5, int minDeltaBoxes = 0)
	{
		List<StockRecord> top = StockRecords.Where(e => e.OnDisplay && Math.Abs(e.StockDelta) >= minDeltaBoxes && e.StockDelta > 0).OrderByDescending(e => e.StockDelta)
			.Take(topLimit).ToList();
		List<StockRecord> bottom = StockRecords.Where(e => e.OnDisplay && Math.Abs(e.StockDelta) >= minDeltaBoxes && e.StockDelta < 0).OrderBy(e => e.StockDelta)
			.Take(topLimit).ToList();

		return (top, bottom);
	}

	public void LogOutliers()
	{
		const int min = 1;
		(List<StockRecord> top, List<StockRecord> bottom) = GetStockDeltaOutliers(10, min);

		string log = $"\n===================================\nShowing deltas with a minimum of {min} \nTop stock deltas:";
		if (top.Count > 0)
		{
			foreach (StockRecord r in top)
			{
				log += $"\nΔBox {r.StockDelta,8:N2} | Stock: {r.CurrentStockBoxes,5:N1} | {r.ProductNamePrint()}";
			}
		}
		else
		{
			log += "\n[no positive deltas]";
		}

		log += "\n\nBottom stock deltas:";
		if (bottom.Count > 0)
		{
			foreach (StockRecord r in bottom)
			{
				log += $"\nΔBox {r.StockDelta,8:N2} | Stock: {r.CurrentStockBoxes,5:N1} | {r.ProductNamePrint()}";
			}
		}
		else
		{
			log += "\n[no negative deltas]";
		}

		IMS.Logger.LogInfo(log);
	}
}