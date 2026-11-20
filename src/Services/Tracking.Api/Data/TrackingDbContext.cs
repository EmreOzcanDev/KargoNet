using KargoNet.Tracking.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace KargoNet.Tracking.Api.Data;

public class TrackingDbContext : DbContext
{
    public TrackingDbContext(DbContextOptions<TrackingDbContext> options)
        : base(options)
    {
    }

    public DbSet<ShipmentTracking> ShipmentTrackings => Set<ShipmentTracking>();

    public DbSet<TrackingEvent> TrackingEvents => Set<TrackingEvent>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShipmentTracking>(entity =>
        {
            entity.HasKey(t => t.TrackingNumber);
            entity.Property(t => t.TrackingNumber).HasMaxLength(64);
            entity.Property(t => t.Destination).HasMaxLength(256);
        });

        modelBuilder.Entity<TrackingEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.TrackingNumber).HasMaxLength(64);
            entity.Property(e => e.EventType).HasMaxLength(64);
            entity.HasIndex(e => new { e.TrackingNumber, e.OccurredAt });
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
        });
    }
}
