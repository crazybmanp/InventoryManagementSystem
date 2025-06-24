namespace IMS;

public class OrderItem
{
	public int Id { get; set; }
	public string Name { get; set; }
	public int BoxCount { get; set; }
	public float PricePerItem { get; set; }
	public float PricePerBox { get; set; }

	public override string ToString()
	{
		return $"{BoxCount,3} boxes of ({Id,4}){Name} at price {PricePerBox}";
	}
}