namespace mtg_game_tracker.Models
{
    public class Game
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Notes { get; set; } = string.Empty;
        public int? WinnerId { get; set; }
        public Person? Winner { get; set; }
        public ICollection<Participant> Participants { get; set; } = new List<Participant>();
    }
}
