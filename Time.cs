using System.ComponentModel.DataAnnotations;
namespace PPImport
{
    public class Time
    {

        public Time() {

        }

        public Time(DateTime date, int playerId, int track, bool glitch, bool flap, int minutes, int seconds, int milliseconds, string link, string ghost) {
            Date = date;
            PlayerId = playerId;
            Track = track;
            Glitch = glitch;
            Flap = flap;
            Minutes = minutes;
            Seconds = seconds;
            Milliseconds = milliseconds;
            Link = link;
            Ghost = ghost;
        }

        public int Id { get; set; }

        public DateTime? Date { get; set; }

        [Required]
        public int PlayerId { get; set; }

        [Required]
        public int Track { get; set; }

        [Required]
        public bool Glitch { get; set; }

        [Required]
        public bool Flap { get; set; }

        [Required]
        public int Minutes { get; set; }

        [Required]
        public int Seconds { get; set; }

        [Required]
        public int Milliseconds { get; set; }

        public string? Link { get; set; }

        public string? Ghost { get; set; }
    }
}