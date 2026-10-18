using Microsoft.EntityFrameworkCore;
using WoobackVash.Api.Models;

namespace WoobackVash.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Member> Members => Set<Member>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<RaidEvent> RaidEvents => Set<RaidEvent>();
    public DbSet<BoardLayout> BoardLayouts => Set<BoardLayout>();
    public DbSet<LootAward> LootAwards => Set<LootAward>();
    public DbSet<LootRoll> LootRolls => Set<LootRoll>();
    public DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();
    public DbSet<CharacterGearSnapshot> GearSnapshots => Set<CharacterGearSnapshot>();
    public DbSet<RosterImportCandidate> RosterImportCandidates => Set<RosterImportCandidate>();
    public DbSet<LootPrioExclusion> LootPrioExclusions => Set<LootPrioExclusion>();
    public DbSet<PollResponse> PollResponses => Set<PollResponse>();
    public DbSet<GuildApplication> GuildApplications => Set<GuildApplication>();
    public DbSet<LaunchPollResponse> LaunchPollResponses => Set<LaunchPollResponse>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<PhotoImage> PhotoImages => Set<PhotoImage>();
    public DbSet<PhotoRejection> PhotoRejections => Set<PhotoRejection>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<GuildApplication>(e =>
        {
            // Same treatment as PollResponse.Answers — both answer maps are jsonb, not text.
            e.Property(x => x.Choices).HasColumnType("jsonb");
            e.Property(x => x.Text).HasColumnType("jsonb");
            // The review page lists newest first.
            e.HasIndex(x => x.SubmittedAt);
        });

        b.Entity<LaunchPollResponse>(e =>
        {
            // One response per Discord user — POST upserts on it.
            e.HasIndex(x => x.Uid).IsUnique();
            e.Property(x => x.Choices).HasColumnType("jsonb");
            e.Property(x => x.Text).HasColumnType("jsonb");
        });

        b.Entity<Photo>(e =>
        {
            e.Property(x => x.Tags).HasColumnType("jsonb");
            e.Property(x => x.Moderation).HasColumnType("jsonb");
            // The book lists newest first, split by status.
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne(x => x.Image)
             .WithOne()
             .HasForeignKey<PhotoImage>(x => x.PhotoId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PhotoImage>(e => e.HasKey(x => x.PhotoId));

        b.Entity<PhotoRejection>(e =>
        {
            e.Property(x => x.Categories).HasColumnType("jsonb");
            e.Property(x => x.Scores).HasColumnType("jsonb");
            // Listed newest first. No FK on PhotoId: the record outlives the photo being deleted.
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<Member>(e =>
        {
            e.HasIndex(m => m.DiscordUserId).IsUnique();
            e.HasMany(m => m.Characters)
             .WithOne(c => c.Member)
             .HasForeignKey(c => c.MemberId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Character>(e =>
        {
            e.HasIndex(c => c.Name);
            // Every roster/loot/attendance read filters ignored characters out.
            e.HasIndex(c => c.Ignored);
        });

        b.Entity<RaidEvent>(e =>
        {
            // Unique only when present; Postgres treats multiple NULLs as distinct.
            e.HasIndex(r => r.RhEventId).IsUnique();
            e.HasIndex(r => r.WclReportCode).IsUnique();
        });

        b.Entity<BoardLayout>(e =>
        {
            // Store the snapshot as jsonb, not text — lets us query into it later.
            e.Property(x => x.State).HasColumnType("jsonb");
            // One layout per storage key (event id or "default").
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.RaidEvent)
             .WithMany(r => r.BoardLayouts)
             .HasForeignKey(x => x.RaidEventId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LootAward>(e =>
        {
            // Both optional now: a Gargul import carries no event key, and a
            // disenchant has no winning character. Detach (don't cascade) on delete.
            e.HasOne(x => x.RaidEvent)
             .WithMany(r => r.LootAwards)
             .HasForeignKey(x => x.RaidEventId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Character)
             .WithMany()
             .HasForeignKey(x => x.CharacterId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
            // Gargul's per-award id — unique when present so re-import is idempotent.
            // Postgres treats multiple NULLs as distinct, so manual awards are unaffected.
            e.HasIndex(x => x.Checksum).IsUnique();
        });

        b.Entity<LootRoll>(e =>
        {
            e.HasOne(x => x.LootAward)
             .WithMany(a => a.Rolls)
             .HasForeignKey(x => x.LootAwardId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Character)
             .WithMany()
             .HasForeignKey(x => x.CharacterId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AttendanceRecord>(e =>
        {
            e.HasOne(x => x.RaidEvent)
             .WithMany(r => r.Attendance)
             .HasForeignKey(x => x.RaidEventId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Character)
             .WithMany()
             .HasForeignKey(x => x.CharacterId)
             .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<RosterImportCandidate>(e =>
        {
            // A scratch table rebuilt each sync; one row per roster name.
            e.HasIndex(c => c.Name).IsUnique();
        });

        b.Entity<LootPrioExclusion>(e =>
        {
            // At most one mute per character per item name per raid — POST upserts on it.
            e.HasIndex(x => new { x.CharacterId, x.Raid, x.ItemName }).IsUnique();
            e.HasOne(x => x.Character)
             .WithMany()
             .HasForeignKey(x => x.CharacterId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PollResponse>(e =>
        {
            // One response per Discord user — POST upserts on it.
            e.HasIndex(x => x.Uid).IsUnique();
            // Same treatment as BoardLayout.State — the answers map is jsonb, not text.
            e.Property(x => x.Answers).HasColumnType("jsonb");
        });

        b.Entity<CharacterGearSnapshot>(e =>
        {
            // Same treatment as BoardLayout.State — jsonb, not text.
            e.Property(x => x.Items).HasColumnType("jsonb");
            // One snapshot per character per report, so a re-import updates in place.
            e.HasIndex(x => new { x.CharacterId, x.WclReportCode }).IsUnique();
            e.HasOne(x => x.Character)
             .WithMany()
             .HasForeignKey(x => x.CharacterId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RaidEvent)
             .WithMany()
             .HasForeignKey(x => x.RaidEventId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
