using Microsoft.EntityFrameworkCore;

namespace BankingApi.EventReceiver
{
    public class BankingApiDbContext : DbContext
    {
        public DbSet<BankAccount> BankAccounts { get; set; }
		public DbSet<Transaction> Transactions { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlServer("Data Source=.\\SQLEXPRESS;Initial Catalog=BankingApiTest;Integrated Security=True;TrustServerCertificate=True;");

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<BankAccount>()
				.Property(b => b.Balance)
				.HasColumnType("decimal(18, 2)"); // Specify the type explicitly

			modelBuilder.Entity<Transaction>()
				.Property(t => t.Amount)
				.HasColumnType("decimal(18, 2)"); // Specify the type explicitly
		}
	}
}
