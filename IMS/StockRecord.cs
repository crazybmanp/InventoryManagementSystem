using System.Collections.Generic;
using System.Linq;
using MyBox;

namespace IMS;

public class StockRecord
{
	public static IDManager IdManager = Singleton<IDManager>.Instance;
	public static DisplayManager DisplayManager = Singleton<DisplayManager>.Instance;
	public static InventoryManager InventoryManager = Singleton<InventoryManager>.Instance;

	public int Id { get; }
	public string Name { get; }
	public string? Brand { get; }
	public string? Category { get; }

	public int BoxSize { get; }

	private readonly List<int> saleCounts;

	private int currentDaySaleCount;

	public bool OnDisplay => DisplayManager.DisplayedProducts.ContainsKey(Id);

	public float TargetStockBoxes
	{
		get
		{
			float averageBoxes = AverageSoldBoxes();
			float orderQuantity = averageBoxes * IMS.ConfigDaysToStock.Value;
			if (orderQuantity < IMS.ConfigMinBoxes.Value)
			{
				orderQuantity = IMS.ConfigMinBoxes.Value;
			}
			return orderQuantity;
		}
	}

	public float CurrentStockBoxes => ConvertToBoxCount(GetCurrentStorageCount());

	public float StockDelta => TargetStockBoxes - CurrentStockBoxes;

	public StockRecord(ProductSO product)
	{
		Id = product.ID;
		Name = product.ProductName;
		Brand = product.ProductBrand;
		Category = product.name;

		BoxSize = product.GridLayoutInBox.productCount;

		saleCounts = new List<int>();
	}

	public StockRecord(StockRecordDTO dto)
	{
		Id = dto.Id;

		ProductSO product = IdManager.ProductSO(Id);
		Name = product.ProductName;
		Brand = product.ProductBrand;
		Category = product.name;
		BoxSize = product.GridLayoutInBox.productCount;

		saleCounts = dto.SaleCounts.ToList();

		currentDaySaleCount = dto.currentDayCount;
	}

	public StockRecordDTO ToDTO()
	{
		return new StockRecordDTO
		{
			Id = Id,
			SaleCounts = saleCounts.ToList(),
			currentDayCount = currentDaySaleCount,
		};
	}

	public float AverageSold()
	{
		if (saleCounts.Count == 0)
		{
			return 0;
		}
		return saleCounts.Sum() / (float)saleCounts.Count;
	}

	public float ConvertToBoxCount(float count)
	{

		return count / (float)BoxSize;
	}

	public float ConvertToBoxCount(int count)
	{
		return count / (float)BoxSize;
	}

	public int ConvertToWholeBoxCount(int count)
	{
		return count / BoxSize;
	}

	public float AverageSoldBoxes()
	{
		return ConvertToBoxCount(AverageSold());
	}

	public int GetCurrentDisplayedCount()
	{
		return DisplayManager.GetDisplayedProductCount(Id);
	}

	public int GetCurrentStorageCount()
	{
		if (IMS.ConfigIncludeDisplay.Value)
		{
			return InventoryManager.GetInventoryAmount(Id);
		}
		else
		{
			(int displayCount, int boxCount, int streetCount) = InventoryManager.GetInventoryAmountEach(Id);

			return boxCount + streetCount;
		}
	}

	public string ProductNamePrint()
	{
		return $"{Name}{(Brand is null ? "" : ($" - {Brand}"))}({Id})";
	}

	public string PrintInfo()
	{
		return $"{ProductNamePrint()} | Current stock {GetCurrentStorageCount()} | Average Sales {AverageSold()}";
	}

	public void RolloverDay()
	{
		saleCounts.Add(currentDaySaleCount);
		while (saleCounts.Count > IMS.ConfigAveragingDays.Value)
		{
			saleCounts.RemoveAt(0);
		}
		currentDaySaleCount = 0;
		//IMS.Logger.LogInfo(RollingDayInfo());
	}

	public void AddSale(int? count)
	{
		if (count is not null)
		{
			currentDaySaleCount += count.Value;
			//IMS.Logger.LogInfo(SaleInfo());
		}
		else
		{
			currentDaySaleCount++;
		}
	}

	public string SaleInfo()
	{
		return $"Salecount {currentDaySaleCount,4} for product {ProductNamePrint()}.";
	}

	public string RollingDayInfo()
	{
		return $"Average/Day:{AverageSold(),6:N2} count, {AverageSoldBoxes(),5:N2} Boxes for {ProductNamePrint()}";
	}
}