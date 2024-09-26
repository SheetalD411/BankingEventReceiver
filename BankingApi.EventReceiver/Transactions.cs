namespace BankingApi.EventReceiver
{
    public class Transaction
    {
        public Guid Id { get; set; }
        public Guid BankAccountId { get; set; }
        public decimal Amount { get; set; }
        public MessageType TransactionType { get; set; } // Credit or Debit
        public DateTime Timestamp { get; set; }
        public BankAccount BankAccount { get; set; }
    }
}
