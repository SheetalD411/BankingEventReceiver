using Azure.Messaging.ServiceBus;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BankingApi.EventReceiver
{
	public class MessageWorker
	{
		private readonly IServiceBusReceiver _serviceBusReceiver;
		private readonly BankingApiDbContext _dbContext;
		private readonly ILogger<MessageWorker> _logger;

		public MessageWorker(IServiceBusReceiver serviceBusReceiver, BankingApiDbContext dbContext, ILogger<MessageWorker> logger)
		{
			_serviceBusReceiver = serviceBusReceiver;
			_dbContext = dbContext;
			_logger = logger;
		}

		public async Task Start()
		{
			_logger.LogInformation("MessageWorker started.");

			while (true)
			{
				var message = await _serviceBusReceiver.Peek();

				if (message == null)
				{
					_logger.LogInformation("No messages found, waiting for 10 seconds.");
					await Task.Delay(TimeSpan.FromSeconds(10));
					continue;
				}

				_logger.LogInformation($"Message received with Id: {message.Id}");

				var eventMessage = DeserializeMessage(message.MessageBody);
				ValidateMessage(eventMessage);
				try
				{
					await ProcessMessage(eventMessage);
					//Call complete once the processing is done
					await _serviceBusReceiver.Complete(message);
					_logger.LogInformation($"Message {message.Id} processed successfully.");
				}
				//Could be more - that could be treated as transient like Network exceptions
				catch (Exception ex) when (ex is SqlException || ex is ServiceBusException)
				{
					_logger.LogWarning($"Transient failure processing message {message.Id}: {ex.Message}");
					await HandleTransientFailure(message, ex.Message);
				}
				catch (Exception ex)
				{
					_logger.LogError($"Non-transient failure processing message {message.Id}: {ex.Message}");
					await MoveToDeadLetter(message, ex.Message);
				}
			}
		}

		public EventMessage? DeserializeMessage(string? messageBody)
		{
			try
			{
				return JsonSerializer.Deserialize<EventMessage>(messageBody);
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error deserializing message body: {ex.Message}");
				return null;
			}
		}
		
		private async Task ProcessMessage(EventMessage eventMessage)
		{
			using (var transaction = await _dbContext.Database.BeginTransactionAsync())
			{
				var bankAccount = await _dbContext.BankAccounts
					.FirstOrDefaultAsync(a => a.Id == eventMessage.BankAccountId);

				if (bankAccount == null)
				{
					_logger.LogError($"Bank account {eventMessage.BankAccountId} not found.");
					throw new Exception("Bank account not found");
				}

				//Good practice to maintain transaction records
				var transactionRecord = new Transaction
				{
					Id = Guid.NewGuid(),
					BankAccountId = bankAccount.Id,
					Amount = eventMessage.Amount,
					TransactionType = eventMessage.MessageType,
					Timestamp = DateTime.UtcNow
				};

				if (eventMessage.MessageType == MessageType.Credit)
				{
					bankAccount.Balance += eventMessage.Amount;
					_logger.LogInformation($"Credited {eventMessage.Amount} to account {bankAccount.Id}. New balance: {bankAccount.Balance}");
				}
				else if (eventMessage.MessageType == MessageType.Debit)
				{
					if (bankAccount.Balance < eventMessage.Amount)
					{
						_logger.LogError($"Insufficient funds for account {bankAccount.Id}");
						throw new Exception("Insufficient funds");
					}
					bankAccount.Balance -= eventMessage.Amount;
					_logger.LogInformation($"Debited {eventMessage.Amount} from account {bankAccount.Id}. New balance: {bankAccount.Balance}");
				}

				_dbContext.Transactions.Add(transactionRecord);
				await _dbContext.SaveChangesAsync();
				await transaction.CommitAsync();
			}
		}

		private void ValidateMessage(EventMessage message)
		{
			if (message.Amount <= 0)
			{
				throw new ArgumentException("Invalid amount.");
			}

			if (!Enum.IsDefined(typeof(MessageType), message.MessageType))
			{
				throw new ArgumentException("Invalid message type.");
			}

			if (message.BankAccountId == Guid.Empty)
			{
				throw new ArgumentException("Invalid BankAccountId.");
			}
		}

		private async Task MoveToDeadLetter(EventMessage message, string reason)
		{
			_logger.LogError($"Moving message {message.Id} to dead-letter: {reason}");
			await _serviceBusReceiver.MoveToDeadLetter(message);
		}

		internal async Task HandleTransientFailure(EventMessage message,string reason)
		{
			if (message.ProcessingCount >= 3)
			{
				await MoveToDeadLetter(message, reason);
				return;
			}

			var delay = TimeSpan.FromSeconds(Math.Pow(5, message.ProcessingCount));
			await _serviceBusReceiver.ReSchedule(message, DateTime.Now.Add(delay));
		}
	}
}
