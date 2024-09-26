namespace BankingApi.EventReceiver;

public class EventMessage
{
    public Guid Id { get; set; }
    public string? MessageBody { get; set; }
    public int ProcessingCount { get; set; }
	public MessageType MessageType { get; set; }
	public Guid BankAccountId { get; set; }
	public decimal Amount { get; set; }
}


public enum MessageType
{
	Credit,
	Debit
}