using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<ColorData> Colors => Set<ColorData>();
        public DbSet<Person> People => Set<Person>();
        public DbSet<Deck> Decks => Set<Deck>();
        public DbSet<Game> Games => Set<Game>();
        public DbSet<Participant> Participants => Set<Participant>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Participant>()
                .HasOne(p => p.Game)
                .WithMany(g => g.Participants)
                .HasForeignKey(p => p.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Participant>()
                .HasOne(p => p.Player)
                .WithMany()
                .HasForeignKey(p => p.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Participant>()
                .HasOne(p => p.Deck)
                .WithMany()
                .HasForeignKey(p => p.DeckId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Game>()
                .HasOne(g => g.Winner)
                .WithMany()
                .HasForeignKey(g => g.WinnerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Deck>()
                .HasOne(d => d.Owner)
                .WithMany()
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Deck>()
                .HasOne(d => d.ColorData)
                .WithMany()
                .HasForeignKey(d => d.ColorDataId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
