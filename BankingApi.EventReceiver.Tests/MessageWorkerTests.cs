using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Linq.Expressions;

namespace BankingApi.EventReceiver.Tests
{
	[TestFixture]
	public class MessageWorkerTests
	{
		private Mock<IServiceBusReceiver>? _mockServiceBusReceiver;
		private Mock<BankingApiDbContext>? _mockDbContext;
		private Mock<ILogger<MessageWorker>>? _mockLogger;
		private MessageWorker? _messageWorker;

		[SetUp]
		public void Setup()
		{
			_mockServiceBusReceiver = new Mock<IServiceBusReceiver>();
			_mockDbContext = new Mock<BankingApiDbContext>(new DbContextOptions<BankingApiDbContext>());
			_mockLogger = new Mock<ILogger<MessageWorker>>();
			_messageWorker = new MessageWorker(_mockServiceBusReceiver.Object, _mockDbContext.Object, _mockLogger.Object);
		}

		[Test]
		public async Task Start_WhenNoMessages_ShouldLogWaitingMessage()
		{
			_mockServiceBusReceiver?.Setup(m => m.Peek()).ReturnsAsync((EventMessage?)null);

			var cts = new CancellationTokenSource();
			var startTask = _messageWorker?.Start();

			await Task.Delay(100);

			_mockLogger?.Verify(m => m.LogInformation("No messages found, waiting for 10 seconds."), Times.Once);
		}


		[Test]
		public async Task Start_WhenValidMessage_ShouldProcessAndComplete()
		{
			var bankAccountId = Guid.NewGuid();
			var messageId = Guid.NewGuid().ToString();
			var eventMessage = new EventMessage
			{
				BankAccountId = bankAccountId,
				Amount = 100,
				MessageType = MessageType.Credit,
				ProcessingCount = 1
			};

			_mockServiceBusReceiver?.Setup(m => m.Peek()).ReturnsAsync(eventMessage); // Returns ServiceBusReceivedMessage
			_mockDbContext?.Setup(m => m.BankAccounts.FirstOrDefaultAsync(It.IsAny<Expression<Func<BankAccount, bool>>>(), default))
						  .ReturnsAsync(new BankAccount { Id = eventMessage.BankAccountId, Balance = 1000 });

			_mockServiceBusReceiver?.Setup(m => m.Complete(eventMessage)).Returns(Task.CompletedTask);

			await _messageWorker?.Start();

			_mockLogger?.Verify(m => m.LogInformation($"Message received with Id: {messageId}"), Times.Once);
			_mockLogger?.Verify(m => m.LogInformation($"Message {messageId} processed successfully."), Times.Once);
		}


		[Test]
		public async Task Start_WhenTransientFailure_ShouldLogWarningAndReschedule()
		{
			var messageId = Guid.NewGuid().ToString();
			var bankAccountId = Guid.NewGuid();
			var eventMessage = new EventMessage
			{
				BankAccountId = bankAccountId,
				Amount = 100,
				MessageType = MessageType.Credit,
				ProcessingCount = 1,
			};

			_mockServiceBusReceiver?.Setup(m => m.Peek()).ReturnsAsync(eventMessage);
			_mockDbContext?.Setup(m => m.BankAccounts.FirstOrDefaultAsync(It.IsAny<Expression<Func<BankAccount, bool>>>(), default))
						  .ReturnsAsync(new BankAccount { Id = eventMessage.BankAccountId, Balance = 1000 });

			_mockServiceBusReceiver?.Setup(m => m.Complete(eventMessage)).ThrowsAsync(new ServiceBusException("Transient failure", ServiceBusFailureReason.GeneralError));

			await _messageWorker?.Start();

			_mockLogger?.Verify(m => m.LogWarning(It.IsAny<string>(), It.IsAny<object[]>()), Times.Once);
			_mockServiceBusReceiver?.Verify(m => m.ReSchedule(eventMessage, It.IsAny<DateTime>()), Times.Once);
		}

		[Test]
		public async Task Start_WhenBankAccountNotFound_ShouldLogErrorAndMoveToDeadLetter()
		{
			var messageId = Guid.NewGuid().ToString();
			var message = new EventMessage
			{
				BankAccountId = Guid.NewGuid(),
				Amount = 100,
				MessageType = MessageType.Credit,
				ProcessingCount = 1
			};

			_mockServiceBusReceiver?.Setup(m => m.Peek()).ReturnsAsync(message);
			_mockDbContext?.Setup(m => m.BankAccounts.FirstOrDefaultAsync(It.IsAny<Expression<Func<BankAccount, bool>>>(), default))
				.ReturnsAsync((BankAccount)null);

			await _messageWorker?.Start();

			_mockLogger?.Verify(m => m.LogError(It.IsAny<string>(), It.IsAny<object[]>()), Times.Once);
			_mockServiceBusReceiver?.Verify(m => m.MoveToDeadLetter(message), Times.Once);
		}

		[Test]
		public async Task DeserializeMessage_WhenInvalidJson_ShouldLogError()
		{
			var invalidMessageBody = "Invalid Json";
			var message = new EventMessage
			{
				BankAccountId = Guid.NewGuid(),
				Amount = 100,
				MessageType = MessageType.Credit,
				ProcessingCount = 1,
				MessageBody = invalidMessageBody

			};

			var result = _messageWorker?.DeserializeMessage(invalidMessageBody);

			Assert.That(result, Is.Null);
			_mockLogger?.Verify(m => m.LogError(It.IsAny<string>(), It.IsAny<object[]>()), Times.Once);
		}
	}
}
