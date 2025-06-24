using System.Collections.Generic;

namespace IMS;

public class StockRecordDTO
{
	public int Id { get; set; }

	public List<int> SaleCounts { get; set; }
	public int currentDayCount { get; set; }
}