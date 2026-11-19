using Microsoft.EntityFrameworkCore;

namespace KargoNet.Shipment.Api.Data
{
    using Shipment = KargoNet.Shipment.Api.Entities.Shipment;
    using OutboxMessage = KargoNet.Shipment.Api.Entities.OutboxMessage;

    public class ShipmentDbContext : DbContext
    {
        public ShipmentDbContext(DbContextOptions<ShipmentDbContext> options)
            : base(options)
        {
        }

        public DbSet<Shipment> Shipments => Set<Shipment>();

        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Shipment>(entity =>
            {
                entity.Property(s => s.TrackingNumber).HasMaxLength(64);
                entity.Property(s => s.Destination).HasMaxLength(256);
                entity.Property(s => s.Status).HasConversion<string>();
                entity.HasIndex(s => s.TrackingNumber).IsUnique();
            });

            modelBuilder.Entity<OutboxMessage>(entity =>
            {
                entity.Property(o => o.EventType).HasMaxLength(64);
                entity.Property(o => o.Status).HasConversion<string>();
                entity.HasIndex(o => new { o.Status, o.CreatedAt });
            });
        }
    }
}
