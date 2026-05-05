using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Domain;

namespace SportRental.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    private Guid? _tenantId;
    
    // Getter używany przez query filters - EF Core potrzebuje metody/property żeby prawidłowo re-evaluować
    public Guid? TenantId => _tenantId;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public void SetTenant(Guid? tenantId)
    {
        _tenantId = tenantId;
    }
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Rental> Rentals => Set<Rental>();
    public DbSet<RentalItem> RentalItems => Set<RentalItem>();
    public DbSet<ReservationHold> ReservationHolds => Set<ReservationHold>();
    public DbSet<ContractTemplate> ContractTemplates => Set<ContractTemplate>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeePermissions> EmployeePermissions => Set<EmployeePermissions>();
    public DbSet<CompanyInfo> CompanyInfos => Set<CompanyInfo>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
    public DbSet<SmsConfirmation> SmsConfirmations => Set<SmsConfirmation>();
    public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();
    public DbSet<EmployeeInvitation> EmployeeInvitations => Set<EmployeeInvitation>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<CheckoutSession> CheckoutSessions => Set<CheckoutSession>();
    public DbSet<RentalConfirmation> RentalConfirmations => Set<RentalConfirmation>();
    public DbSet<RentalReview> RentalReviews => Set<RentalReview>();
    public DbSet<RentalItemReview> RentalItemReviews => Set<RentalItemReview>();
    public DbSet<CustomerReview> CustomerReviews => Set<CustomerReview>();
    public DbSet<UserFeedback> UserFeedbacks => Set<UserFeedback>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Allow API-specific entities to be configured from external assemblies
        // Example: SportRental.Api can register RefreshToken via ApiDbContextExtensions.ConfigureApiEntities()

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(256).IsRequired();
            entity.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Producer).HasMaxLength(100);
            entity.Property(p => p.Model).HasMaxLength(100);
            entity.Property(p => p.SerialNumber).HasMaxLength(100);
            entity.Property(p => p.Description).HasMaxLength(5000);
            entity.Property(p => p.QrCode).HasMaxLength(500);
            entity.Property(p => p.DailyPrice).HasPrecision(18, 2);
            entity.HasIndex(p => new { p.TenantId, p.Sku }).IsUnique();
            entity.HasIndex(p => new { p.TenantId, p.Category });
            entity.HasIndex(p => new { p.TenantId, p.Type });
            entity.HasIndex(p => new { p.TenantId, p.CategoryId });
            entity.HasQueryFilter(p => TenantId == null || p.TenantId == TenantId);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.FullName).HasMaxLength(256).IsRequired();
            entity.Property(c => c.Email).HasMaxLength(256);
            entity.Property(c => c.PhoneNumber).HasMaxLength(32);
            entity.Property(c => c.DocumentNumber).HasMaxLength(64);
            entity.HasIndex(c => new { c.TenantId, c.Email });
            entity.HasIndex(c => new { c.TenantId, c.FullName });
            entity.HasIndex(c => new { c.TenantId, c.PhoneNumber });
            entity.HasQueryFilter(c => TenantId == null || c.TenantId == TenantId);
        });

        modelBuilder.Entity<Rental>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.TotalAmount).HasPrecision(18, 2);
            entity.Property(r => r.PaymentIntentId).HasMaxLength(64);
            entity.HasOne(r => r.Customer)
                .WithMany()
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(r => r.Items)
                .WithOne(i => i.Rental)
                .HasForeignKey(i => i.RentalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(r => new { r.TenantId, r.CreatedAtUtc });
            entity.HasIndex(r => new { r.TenantId, r.StartDateUtc, r.EndDateUtc });
            entity.HasIndex(r => new { r.TenantId, r.CustomerId, r.StartDateUtc });
            entity.HasIndex(r => new { r.TenantId, r.IdempotencyKey }).IsUnique();
            entity.HasQueryFilter(r => TenantId == null || r.TenantId == TenantId);
        });

        modelBuilder.Entity<RentalItem>(entity =>
        {
            entity.HasKey(ri => ri.Id);
            entity.Property(ri => ri.PricePerDay).HasPrecision(18, 2);
            entity.Property(ri => ri.Subtotal).HasPrecision(18, 2);
            entity.HasOne(ri => ri.Product)
                .WithMany()
                .HasForeignKey(ri => ri.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(ri => ri.ProductId);
            entity.HasIndex(ri => ri.RentalId);
        });

        modelBuilder.Entity<ReservationHold>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.Property(h => h.Quantity).IsRequired();
            entity.Property(h => h.ExpiresAtUtc).IsRequired();
            entity.HasIndex(h => new { h.TenantId, h.ProductId, h.StartDateUtc, h.EndDateUtc });
            entity.HasIndex(h => h.ExpiresAtUtc);
            entity.HasQueryFilter(h => TenantId == null || h.TenantId == TenantId);
        });

        modelBuilder.Entity<ContractTemplate>(entity =>
        {
            entity.HasKey(ct => ct.Id);
            entity.Property(ct => ct.Content).IsRequired();
            entity.HasIndex(ct => ct.TenantId).IsUnique();
            entity.HasQueryFilter(ct => TenantId == null || ct.TenantId == TenantId);
        });

        modelBuilder.Entity<TenantUser>(entity =>
        {
            entity.HasKey(tu => tu.Id);
            entity.HasIndex(tu => new { tu.TenantId, tu.UserId }).IsUnique();
            entity.HasQueryFilter(tu => TenantId == null || tu.TenantId == TenantId);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(200).IsRequired();
            entity.Property(e => e.City).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Telephone).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Comment).HasMaxLength(500);
            entity.Property(e => e.Position).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.Email });
            entity.HasIndex(e => new { e.TenantId, e.FullName });
            entity.HasQueryFilter(e => TenantId == null || e.TenantId == TenantId);
        });

        modelBuilder.Entity<EmployeePermissions>(entity =>
        {
            entity.HasKey(ep => ep.Id);
            entity.HasOne(ep => ep.Employee)
                .WithOne(e => e.Permissions)
                .HasForeignKey<EmployeePermissions>(ep => ep.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(ep => ep.EmployeeId).IsUnique();
            entity.HasQueryFilter(ep => TenantId == null || ep.TenantId == TenantId);
        });

        modelBuilder.Entity<CompanyInfo>(entity =>
        {
            entity.HasKey(ci => ci.Id);
            entity.Property(ci => ci.Name).HasMaxLength(200);
            entity.Property(ci => ci.Address).HasMaxLength(300);
            entity.Property(ci => ci.NIP).HasMaxLength(20);
            entity.Property(ci => ci.Email).HasMaxLength(200);
            entity.Property(ci => ci.PhoneNumber).HasMaxLength(20);
            entity.HasIndex(ci => ci.TenantId).IsUnique();
            entity.HasQueryFilter(ci => TenantId == null || ci.TenantId == TenantId);
        });

        modelBuilder.Entity<ProductCategory>(entity =>
        {
            entity.HasKey(pc => pc.Id);
            entity.Property(pc => pc.Name).HasMaxLength(100).IsRequired();
            entity.Property(pc => pc.Description).HasMaxLength(500);
            entity.HasIndex(pc => new { pc.TenantId, pc.Name }).IsUnique();
            entity.HasMany(pc => pc.Products)
                .WithOne(p => p.ProductCategory)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasQueryFilter(pc => TenantId == null || pc.TenantId == TenantId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(al => al.Id);
            entity.Property(al => al.Message).HasMaxLength(1000).IsRequired();
            entity.Property(al => al.Action).HasMaxLength(100);
            entity.Property(al => al.EntityType).HasMaxLength(100);
            entity.Property(al => al.Level).HasMaxLength(50);
            entity.HasIndex(al => new { al.TenantId, al.Date });
            entity.HasIndex(al => new { al.TenantId, al.EntityType, al.EntityId });
            entity.HasQueryFilter(al => TenantId == null || al.TenantId == TenantId);
        });

        modelBuilder.Entity<ErrorLog>(entity =>
        {
            entity.HasKey(el => el.Id);
            entity.Property(el => el.Message).HasMaxLength(2000).IsRequired();
            entity.Property(el => el.StackTrace).HasMaxLength(5000);
            entity.Property(el => el.Source).HasMaxLength(200);
            entity.Property(el => el.Severity).HasMaxLength(50);
            entity.HasIndex(el => new { el.TenantId, el.Date });
            entity.HasIndex(el => new { el.TenantId, el.Severity });
            entity.HasQueryFilter(el => TenantId == null || el.TenantId == TenantId);
        });

        modelBuilder.Entity<UserFeedback>(entity =>
        {
            entity.HasKey(uf => uf.Id);
            entity.Property(uf => uf.Message).HasMaxLength(8000).IsRequired();
            entity.Property(uf => uf.UserEmail).HasMaxLength(256);
            entity.Property(uf => uf.UserRole).HasMaxLength(64);
            entity.Property(uf => uf.CurrentPage).HasMaxLength(512);
            entity.Property(uf => uf.ResolvedBy).HasMaxLength(256);
            entity.Property(uf => uf.ResolutionNotes).HasMaxLength(2000);
            // ContextJson trzymany jako jsonb w Postgresie — w InMemory ignorowane (string).
            entity.Property(uf => uf.ContextJson).HasColumnType("jsonb");
            entity.HasIndex(uf => new { uf.TenantId, uf.CreatedAtUtc });
            entity.HasIndex(uf => new { uf.TenantId, uf.Type });
            entity.HasIndex(uf => new { uf.TenantId, uf.IsResolved });
            // SuperAdmin (TenantId = null) widzi wszystko cross-tenant.
            entity.HasQueryFilter(uf => TenantId == null || uf.TenantId == TenantId);
        });

        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.UserEmail).HasMaxLength(256);
            entity.Property(c => c.UserRole).HasMaxLength(64);
            entity.Property(c => c.Source).HasMaxLength(16);
            entity.HasIndex(c => new { c.TenantId, c.UserId, c.StartedAtUtc });
            entity.HasQueryFilter(c => TenantId == null || c.TenantId == TenantId);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Role).HasMaxLength(16).IsRequired();
            entity.Property(m => m.Content).IsRequired();
            entity.Property(m => m.CurrentPage).HasMaxLength(512);
            entity.Property(m => m.ToolCallsJson).HasColumnType("jsonb");
            entity.HasOne(m => m.Conversation)
                  .WithMany(c => c.Messages)
                  .HasForeignKey(m => m.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(m => new { m.ConversationId, m.CreatedAtUtc });
            entity.HasIndex(m => new { m.TenantId, m.CreatedAtUtc });
            entity.HasQueryFilter(m => TenantId == null || m.TenantId == TenantId);
        });

        modelBuilder.Entity<SmsConfirmation>(entity =>
        {
            entity.HasKey(sc => sc.Id);
            entity.Property(sc => sc.Code).HasMaxLength(10).IsRequired();
            entity.Property(sc => sc.PhoneNumber).HasMaxLength(20).IsRequired();
            entity.HasOne(sc => sc.Rental)
                .WithMany()
                .HasForeignKey(sc => sc.RentalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(sc => new { sc.TenantId, sc.RentalId });
            entity.HasIndex(sc => new { sc.Code, sc.RentalId }).IsUnique();
            entity.HasQueryFilter(sc => TenantId == null || sc.TenantId == TenantId);
        });

        modelBuilder.Entity<TenantInvitation>(entity =>
        {
            entity.HasKey(ti => ti.Id);
            entity.Property(ti => ti.Email).HasMaxLength(256).IsRequired();
            entity.Property(ti => ti.TenantName).HasMaxLength(200);
            entity.Property(ti => ti.Token).HasMaxLength(128).IsRequired();
            entity.Property(ti => ti.Notes).HasMaxLength(500);
            entity.HasIndex(ti => ti.Token).IsUnique();
            entity.HasIndex(ti => ti.Email);
            entity.HasIndex(ti => ti.ExpiresAtUtc);
        });

        modelBuilder.Entity<EmployeeInvitation>(entity =>
        {
            entity.HasKey(ei => ei.Id);
            entity.Property(ei => ei.Email).HasMaxLength(256).IsRequired();
            entity.Property(ei => ei.FullName).HasMaxLength(200);
            entity.Property(ei => ei.Token).HasMaxLength(128).IsRequired();
            entity.Property(ei => ei.Notes).HasMaxLength(500);
            entity.HasIndex(ei => ei.Token).IsUnique();
            entity.HasIndex(ei => new { ei.TenantId, ei.Email });
            entity.HasIndex(ei => ei.ExpiresAtUtc);
            entity.HasQueryFilter(ei => TenantId == null || ei.TenantId == TenantId);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(rt => rt.Id);
            entity.HasIndex(rt => rt.Token).IsUnique();
            entity.HasIndex(rt => rt.UserId);
            entity.HasIndex(rt => rt.ExpiresAtUtc);
            entity.Property(rt => rt.Token).HasMaxLength(128).IsRequired();
            entity.Property(rt => rt.RevokedReason).HasMaxLength(200);
            entity.Property(rt => rt.ReplacedByToken).HasMaxLength(128);
        });

        modelBuilder.Entity<RentalConfirmation>(entity =>
        {
            entity.HasKey(rc => rc.Id);
            entity.Property(rc => rc.Token).HasMaxLength(128).IsRequired();
            entity.Property(rc => rc.PhoneNumber).HasMaxLength(20);
            entity.Property(rc => rc.Email).HasMaxLength(256);
            entity.Property(rc => rc.ConfirmedFromIp).HasMaxLength(45);
            entity.Property(rc => rc.ConfirmedUserAgent).HasMaxLength(500);
            entity.Property(rc => rc.RegulationsHash).HasMaxLength(64);
            entity.HasOne(rc => rc.Rental)
                .WithMany()
                .HasForeignKey(rc => rc.RentalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(rc => rc.Token).IsUnique();
            entity.HasIndex(rc => new { rc.TenantId, rc.RentalId });
            entity.HasQueryFilter(rc => TenantId == null || rc.TenantId == TenantId);
        });

        modelBuilder.Entity<RentalReview>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Comment).HasMaxLength(2000);
            entity.HasOne(r => r.Rental)
                .WithMany()
                .HasForeignKey(r => r.RentalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.Customer)
                .WithMany()
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(r => r.RentalId).IsUnique();
            entity.HasIndex(r => new { r.TenantId, r.CreatedAtUtc });
            entity.HasQueryFilter(r => TenantId == null || r.TenantId == TenantId);
            entity.HasMany(r => r.ItemReviews)
                .WithOne(ir => ir.RentalReview)
                .HasForeignKey(ir => ir.RentalReviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RentalItemReview>(entity =>
        {
            entity.HasKey(ir => ir.Id);
            entity.Property(ir => ir.Comment).HasMaxLength(1000);
            entity.HasOne(ir => ir.RentalItem)
                .WithMany()
                .HasForeignKey(ir => ir.RentalItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(ir => ir.Product)
                .WithMany()
                .HasForeignKey(ir => ir.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            // Nie można dwa razy ocenić tego samego itemu w ramach jednej opinii
            entity.HasIndex(ir => new { ir.RentalReviewId, ir.RentalItemId }).IsUnique();
        });

        modelBuilder.Entity<CustomerReview>(entity =>
        {
            entity.HasKey(cr => cr.Id);
            entity.HasOne(cr => cr.Customer)
                .WithMany()
                .HasForeignKey(cr => cr.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(cr => cr.Rental)
                .WithMany()
                .HasForeignKey(cr => cr.RentalId)
                .OnDelete(DeleteBehavior.Cascade);
            // Jedna ocena per wynajem — wypożyczalnia ocenia tylko raz po zwrocie.
            entity.HasIndex(cr => cr.RentalId).IsUnique();
            // Cross-tenant agregat — query dla per-customer trust nie filtruje po TenantId
            // (każdy admin widzi globalny poziom zaufania), ale ENDPOINT-level autoryzacja
            // filtruje listę szczegółową do tenant-a wystawiającego.
            entity.HasIndex(cr => new { cr.CustomerId, cr.CreatedAtUtc });
            entity.HasIndex(cr => new { cr.TenantId, cr.CreatedAtUtc });
            entity.HasQueryFilter(cr => TenantId == null || cr.TenantId == TenantId);
        });

        modelBuilder.Entity<CheckoutSession>(entity =>
        {
            entity.ToTable("CheckoutSessions");
            entity.HasKey(cs => cs.Id);
            entity.HasIndex(cs => cs.IdempotencyKey).IsUnique();
            entity.HasIndex(cs => cs.StripeSessionId);
            entity.HasIndex(cs => cs.ExpiresAtUtc);
            entity.Property(cs => cs.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(cs => cs.StripeSessionId).HasMaxLength(200);
        });
    }
}
