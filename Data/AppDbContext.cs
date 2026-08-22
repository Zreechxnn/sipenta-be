using Microsoft.EntityFrameworkCore;
using SIAP.Api.Entities;

namespace SIAP.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Bidang> Bidangs { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<DocumentContent> DocumentContents { get; set; }
    public DbSet<DocumentChunk> DocumentChunks { get; set; }
    public DbSet<DocumentAccess> DocumentAccesses { get; set; }
    public DbSet<DocumentImage> DocumentImages { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<ChatSession> ChatSessions { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("vector");

        // Bidang config
        modelBuilder.Entity<Bidang>(entity =>
        {
            entity.HasIndex(b => b.Nama).IsUnique();
        });

        modelBuilder.Entity<Document>()
            .HasOne(d => d.Content)
            .WithOne(c => c.Document)
            .HasForeignKey<DocumentContent>(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Document>()
            .Property(d => d.Status)
            .HasConversion<string>();

        modelBuilder.Entity<DocumentContent>()
            .Property(c => c.ParseStatus)
            .HasConversion<string>();

        modelBuilder.Entity<DocumentContent>()
            .Property(c => c.OcrStatus)
            .HasConversion<string>();

        modelBuilder.Entity<DocumentChunk>()
            .HasOne(c => c.Document)
            .WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentImage>()
            .HasOne(di => di.Document)
            .WithMany(d => d.Images)
            .HasForeignKey(di => di.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Document>()
            .HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Document>()
            .HasOne(d => d.Bidang)
            .WithMany(b => b.Documents)
            .HasForeignKey(d => d.BidangId)
            .OnDelete(DeleteBehavior.SetNull);

        // DocumentAccess config
        modelBuilder.Entity<DocumentAccess>(entity =>
        {
            entity.HasIndex(da => new { da.DocumentId, da.UserId }).IsUnique();

            entity.HasOne(da => da.Document)
                .WithMany(d => d.Accesses)
                .HasForeignKey(da => da.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(da => da.User)
                .WithMany(u => u.DocumentAccesses)
                .HasForeignKey(da => da.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(da => da.SharedByUser)
                .WithMany()
                .HasForeignKey(da => da.SharedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Role config
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasIndex(r => r.Name).IsUnique();
        });

        // User config
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();
            
            entity.HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(u => u.Bidang)
                .WithMany(b => b.Users)
                .HasForeignKey(u => u.BidangId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ChatSession>()
            .HasOne(cs => cs.User)
            .WithMany()
            .HasForeignKey(cs => cs.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ChatMessage>()
            .HasOne(cm => cm.ChatSession)
            .WithMany(cs => cs.Messages)
            .HasForeignKey(cm => cm.ChatSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Seeding Data
        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        // 1. Bidang Seeds
        modelBuilder.Entity<Bidang>().HasData(
            new Bidang { Id = 1, Nama = "Bidang APTIKA", Kode = "APTIKA", Deskripsi = "Aplikasi Informatika", CreatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Bidang { Id = 2, Nama = "Bidang TIK", Kode = "TIK", Deskripsi = "Teknologi Informasi dan Komunikasi", CreatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Bidang { Id = 3, Nama = "Bidang IKP", Kode = "IKP", Deskripsi = "Informasi dan Komunikasi Publik", CreatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Bidang { Id = 4, Nama = "Bidang Statistik", Kode = "STATISTIK", Deskripsi = "Statistik Sektoral", CreatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Bidang { Id = 5, Nama = "Bidang Persandian dan Keamanan Informasi", Kode = "SANIKAMI", Deskripsi = "Persandian dan Keamanan Informasi", CreatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Bidang { Id = 6, Nama = "Sekretariat", Kode = "SEKRETARIAT", Deskripsi = "Sekretariat Diskominfo", CreatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );

        // 2. Roles
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = "kasubag" },
            new Role { Id = 2, Name = "admin" },
            new Role { Id = 3, Name = "user" }
        );

        // 3. Users (SuperAdmin & Admin)
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        
        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = adminId,
                Username = "admin",
                Email = "admin@example.com",
                FullName = "Administrator SIAP",
                PasswordHash = "$2b$12$C0FnFmFwP8AhaBDKbwQOZ.tPOThfbDRIG2gRw8jwxCMZH2ev/Ruf6", 
                RoleId = 2,
                BidangId = 6,
                IsApproved = true,
                CreatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
