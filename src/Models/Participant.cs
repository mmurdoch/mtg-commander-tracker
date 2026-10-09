namespace mtg_game_tracker.Models
{
    public class Participant
    {
        public int Id { get; set; }
        public int GameId { get; set; }
        public Game? Game { get; set; }
        public int PlayerId { get; set; }
        public Person? Player { get; set; }
        public int? DeckId { get; set; }
        public Deck? Deck { get; set; }
    }
}
